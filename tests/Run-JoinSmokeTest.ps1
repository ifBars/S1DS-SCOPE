#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Runs a real-process Organisations dedicated-server join smoke test.

.DESCRIPTION
    Launches the local S1DS server and one or more Schedule I clients using the
    dedicated-server CLI auto-join flags, waits for join/snapshot stability, and
    fails fast on known transport symptoms such as FishNet PacketId 65535 kicks.

    This is intentionally log-driven rather than an in-game test mod. It is meant
    for fast iteration on connection/sync regressions where the process logs are
    the evidence we need.
#>

param(
    [string]$ServerGameDir = "",
    [string]$ClientGameDir = "",
    [string[]]$ClientGameDirs = @(),
    [string]$ServerIp = "127.0.0.1",
    [int]$Port = 38465,
    [int]$ClientCount = 1,
    [string[]]$ClientSteamIds = @("76561198000000009", "76561198000000019"),
    [string[]]$ClientNames = @("Test1", "Test2"),
    [string[]]$ExtraServerArgs = @(),
    [string[]]$ExtraClientArgs = @(),
    [int]$ServerReadyTimeoutSeconds = 90,
    [int]$ClientStableSeconds = 75,
    [int]$ClientLaunchGapSeconds = 10,
    [switch]$WaitForClientJoinBeforeNextLaunch,
    [int]$ClientJoinBeforeNextLaunchTimeoutSeconds = 150,
    [switch]$BuildAndDeploy,
    [switch]$RunOrganisationWorkflowSmoke,
    [string]$SmokeOrganisationName = "RPSmokeCrew",
    [int]$WorkflowInviteDelaySeconds = 80,
    [switch]$AllowExtraMods,
    [switch]$ArchiveDedicatedServerSave,
    [switch]$HideClients,
    [switch]$KeepProcesses,
    [switch]$StopExistingProcesses,
    [switch]$KeepExistingProcesses,
    [string]$ResultsRoot = ""
)

$ErrorActionPreference = "Stop"

function Write-Step {
    param([string]$Message)
    Write-Host $Message -ForegroundColor Yellow
}

function Assert-Path {
    param(
        [string]$Path,
        [string]$Description
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "$Description not found: $Path"
    }
}

function Get-SteamApiCandidates {
    param([string]$GameDir)

    @(
        (Join-Path $GameDir "Schedule I_Data\Plugins\x86_64\steam_api64.dll"),
        (Join-Path $GameDir "steam_api64.dll")
    )
}

function Test-GoldbergSteamApi {
    param([string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        return $false
    }

    $versionInfo = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($Path)
    $metadata = @(
        $versionInfo.ProductName,
        $versionInfo.CompanyName,
        $versionInfo.FileDescription,
        $versionInfo.OriginalFilename
    ) -join " "

    return $metadata.IndexOf("GSE", [System.StringComparison]::OrdinalIgnoreCase) -ge 0 `
        -or $metadata.IndexOf("Goldberg", [System.StringComparison]::OrdinalIgnoreCase) -ge 0 `
        -or $metadata.IndexOf("gbe", [System.StringComparison]::OrdinalIgnoreCase) -ge 0
}

function Assert-GoldbergSteamApi {
    param(
        [string]$GameDir,
        [string]$Description
    )

    $candidatePaths = @(Get-SteamApiCandidates -GameDir $GameDir)
    $existingPaths = @($candidatePaths | Where-Object { Test-Path -LiteralPath $_ })
    foreach ($path in $existingPaths) {
        if (Test-GoldbergSteamApi -Path $path) {
            Write-Host "$Description Goldberg-compatible steam_api64.dll: $path" -ForegroundColor DarkGray
            return
        }
    }

    $pathList = if ($existingPaths.Count -gt 0) { $existingPaths -join "; " } else { $candidatePaths -join "; " }
    throw "$Description requires a Goldberg/GSE steam_api64.dll for local multi-client testing. Checked: $pathList"
}

function Get-DirectoryFileNames {
    param([string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        return @()
    }

    @(Get-ChildItem -LiteralPath $Path -File -ErrorAction SilentlyContinue | ForEach-Object { $_.Name })
}

function Assert-InstallContents {
    param(
        [string]$GameDir,
        [string]$Description,
        [string[]]$ExpectedMods,
        [string[]]$AllowedMods,
        [string[]]$AllowedUserLibs,
        [switch]$AllowExtra
    )

    $modsDir = Join-Path $GameDir "Mods"
    $userLibsDir = Join-Path $GameDir "UserLibs"
    $mods = @(Get-DirectoryFileNames -Path $modsDir)
    $userLibs = @(Get-DirectoryFileNames -Path $userLibsDir)
    Write-Host "$Description Mods: $($mods -join ', ')" -ForegroundColor DarkGray
    Write-Host "$Description UserLibs: $($userLibs -join ', ')" -ForegroundColor DarkGray

    foreach ($expectedMod in $ExpectedMods) {
        if (-not ($mods | Where-Object { [string]::Equals($_, $expectedMod, [System.StringComparison]::OrdinalIgnoreCase) })) {
            throw "$Description missing expected mod DLL: $expectedMod"
        }
    }

    if ($AllowExtra) {
        return
    }

    $unexpectedMods = @($mods | Where-Object {
        $name = $_
        -not ($AllowedMods | Where-Object { [string]::Equals($_, $name, [System.StringComparison]::OrdinalIgnoreCase) })
    })
    $unexpectedUserLibs = @($userLibs | Where-Object {
        $name = $_
        -not ($AllowedUserLibs | Where-Object { [string]::Equals($_, $name, [System.StringComparison]::OrdinalIgnoreCase) })
    })

    if ($unexpectedMods.Count -gt 0 -or $unexpectedUserLibs.Count -gt 0) {
        throw "$Description has unexpected install files. Mods=[$($unexpectedMods -join ', ')] UserLibs=[$($unexpectedUserLibs -join ', ')]. Re-run with -AllowExtraMods to acknowledge a dirty install."
    }
}

function Stop-ScheduleOneProcesses {
    param([string[]]$Roots)

    $fullRoots = $Roots | ForEach-Object { [System.IO.Path]::GetFullPath($_) }
    Get-Process -ErrorAction SilentlyContinue |
        Where-Object {
            if ([string]::IsNullOrWhiteSpace($_.Path)) {
                return $false
            }

            $path = [System.IO.Path]::GetFullPath($_.Path)
            foreach ($root in $fullRoots) {
                if ($path.StartsWith($root, [System.StringComparison]::OrdinalIgnoreCase)) {
                    return $true
                }
            }

            return $false
        } |
        ForEach-Object {
            Write-Host "Stopping existing process $($_.Id): $($_.Path)" -ForegroundColor DarkGray
            Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue
        }
}

function Set-GoldbergConfig {
    param(
        [string]$GameDir,
        [string]$SteamId,
        [string]$Name
    )

    $settingsDir = Join-Path $GameDir "Schedule I_Data\Plugins\x86_64\steam_settings"
    New-Item -ItemType Directory -Path $settingsDir -Force | Out-Null

    $config = @"
[user::general]
account_name=$Name
account_steamid=$SteamId
language=english
"@

    Set-Content -LiteralPath (Join-Path $settingsDir "configs.user.ini") -Value $config -Encoding UTF8
}

function Wait-ForLogPattern {
    param(
        [string]$Path,
        [string]$Pattern,
        [int]$TimeoutSeconds,
        [string]$Description
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        if (Test-Path -LiteralPath $Path) {
            $match = Select-String -LiteralPath $Path -Pattern $Pattern -SimpleMatch -Quiet
            if ($match) {
                return $true
            }
        }

        Start-Sleep -Milliseconds 500
    }

    throw "Timed out waiting for $Description in $Path"
}

function Test-LogHasPattern {
    param(
        [string]$Path,
        [string]$Pattern
    )

    return (Test-Path -LiteralPath $Path) -and (Select-String -LiteralPath $Path -Pattern $Pattern -Quiet)
}

function Normalize-ArgumentList {
    param([string[]]$Arguments)

    $normalized = New-Object System.Collections.Generic.List[string]
    foreach ($argument in $Arguments) {
        if ([string]::IsNullOrWhiteSpace($argument)) {
            continue
        }

        $parts = $argument.Split([char[]]@(','), [System.StringSplitOptions]::RemoveEmptyEntries)
        foreach ($part in $parts) {
            $trimmed = $part.Trim()
            if (-not [string]::IsNullOrWhiteSpace($trimmed)) {
                $normalized.Add($trimmed)
            }
        }
    }

    return $normalized.ToArray()
}

function Get-RelevantTimeline {
    param([string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        return @()
    }

    $patterns = @(
        "SendToClient cmd=",
        "OnServerMessageReceived cmd=",
        "OnClientMessageReceived cmd=",
        "Received organisation custom message",
        "[OrgSmoke]",
        "Received organisation command 'org_create_request'",
        "Received organisation command 'org_invite_request'",
        "Received organisation command 'org_invite_accept_request'",
        "Sending organisation snapshot",
        "Snapshot updated",
        "QuestScopeDiag",
        "QuestVariableDiag",
        "DeaddropDiag",
        "Applied scoped quest sync",
        "Quest system initialization completed",
        "PacketId of 65535",
        "Connection will be kicked",
        "Dedicated server connection stopped unexpectedly",
        "Error invoking staggered action callback",
        "Exception",
        "NullReference"
    )

    Get-Content -LiteralPath $Path -ErrorAction SilentlyContinue |
        Where-Object {
            $line = $_
            foreach ($pattern in $patterns) {
                if ($line.IndexOf($pattern, [System.StringComparison]::OrdinalIgnoreCase) -ge 0) {
                    return $true
                }
            }

            return $false
        }
}

function Copy-IfExists {
    param(
        [string]$Source,
        [string]$DestinationDirectory
    )

    if (Test-Path -LiteralPath $Source) {
        Copy-Item -LiteralPath $Source -Destination (Join-Path $DestinationDirectory (Split-Path -Leaf $Source)) -Force
    }
}

function Clear-LogFile {
    param([string]$Path)

    if (Test-Path -LiteralPath $Path) {
        Remove-Item -LiteralPath $Path -Force -ErrorAction SilentlyContinue
    }
}

function Get-MelonRunLogs {
    param(
        [string[]]$GameDirs,
        [datetime]$Since
    )

    $logs = New-Object System.Collections.Generic.List[string]
    foreach ($gameDir in ($GameDirs | Select-Object -Unique)) {
        $latestLog = Join-Path $gameDir "MelonLoader\Latest.log"
        if (Test-Path -LiteralPath $latestLog) {
            $logs.Add($latestLog)
        }

        $archiveDir = Join-Path $gameDir "MelonLoader\Logs"
        if (-not (Test-Path -LiteralPath $archiveDir)) {
            continue
        }

        Get-ChildItem -LiteralPath $archiveDir -Filter "*.log" -File -ErrorAction SilentlyContinue |
            Where-Object { $_.LastWriteTime -ge $Since.AddSeconds(-5) } |
            Sort-Object LastWriteTime |
            ForEach-Object { $logs.Add($_.FullName) }
    }

    @($logs | Select-Object -Unique)
}

function Archive-DefaultDedicatedServerSave {
    param(
        [string]$GameDir,
        [string]$RunId
    )

    $savePath = Join-Path $GameDir "UserData\DedicatedServerSave"
    if (-not (Test-Path -LiteralPath $savePath)) {
        return
    }

    $archivePath = "$savePath.smoke-backup-$RunId"
    Write-Step "Archiving default dedicated-server save"
    Move-Item -LiteralPath $savePath -Destination $archivePath
    Write-Host "Archived save: $archivePath" -ForegroundColor Cyan
}

function Invoke-BuildAndDeploy {
    $addonRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
    $frameworkRoot = [System.IO.Path]::GetFullPath((Join-Path $addonRoot "..\..\DedicatedServerMod"))

    Write-Step "Building Organisations Mono server"
    dotnet build $addonRoot -c Mono_Server -p:AutomateLocalDeployment=false
    if ($LASTEXITCODE -ne 0) {
        throw "Mono_Server build failed"
    }

    Write-Step "Building Organisations Mono client"
    dotnet build $addonRoot -c Mono_Client -p:AutomateLocalDeployment=false
    if ($LASTEXITCODE -ne 0) {
        throw "Mono_Client build failed"
    }

    $serverDll = Join-Path $addonRoot "bin\Mono_Server\netstandard2.1\Organisations_Mono_Server.dll"
    $clientDll = Join-Path $addonRoot "bin\Mono_Client\netstandard2.1\Organisations_Mono_Client.dll"
    $frameworkServerDll = Join-Path $frameworkRoot "bin\Mono_Server\netstandard2.1\DedicatedServerMod_Mono_Server.dll"
    $frameworkClientDll = Join-Path $frameworkRoot "bin\Mono_Client\netstandard2.1\DedicatedServerMod_Mono_Client.dll"
    Assert-Path $serverDll "Built server DLL"
    Assert-Path $clientDll "Built client DLL"
    Assert-Path $frameworkServerDll "Built framework server DLL"
    Assert-Path $frameworkClientDll "Built framework client DLL"

    Copy-Item -LiteralPath $frameworkServerDll -Destination (Join-Path $ServerGameDir "Mods\DedicatedServerMod_Mono_Server.dll") -Force
    Copy-Item -LiteralPath $serverDll -Destination (Join-Path $ServerGameDir "Mods\Organisations_Mono_Server.dll") -Force

    $clientDeployDirs = if ($resolvedClientGameDirs.Count -gt 0) {
        $resolvedClientGameDirs | Select-Object -Unique
    }
    else {
        @($ClientGameDir)
    }

    foreach ($clientDeployDir in $clientDeployDirs) {
        Copy-Item -LiteralPath $frameworkClientDll -Destination (Join-Path $clientDeployDir "Mods\DedicatedServerMod_Mono_Client.dll") -Force
        Copy-Item -LiteralPath $clientDll -Destination (Join-Path $clientDeployDir "Mods\Organisations_Mono_Client.dll") -Force
    }
}

if ($ClientCount -lt 1) {
    throw "ClientCount must be at least 1."
}

if ($ClientSteamIds.Count -lt $ClientCount -or $ClientNames.Count -lt $ClientCount) {
    throw "Provide at least ClientCount Steam IDs and names."
}

$resolvedClientGameDirs = @()
if ($ClientGameDirs.Count -gt 0) {
    if ($ClientGameDirs.Count -lt $ClientCount) {
        throw "Provide at least ClientCount entries in ClientGameDirs, or omit ClientGameDirs to reuse ClientGameDir."
    }

    for ($i = 0; $i -lt $ClientCount; $i++) {
        $resolvedClientGameDirs += $ClientGameDirs[$i]
    }
}
else {
    for ($i = 0; $i -lt $ClientCount; $i++) {
        $resolvedClientGameDirs += $ClientGameDir
    }
}

if ($RunOrganisationWorkflowSmoke -and $ClientCount -lt 2) {
    throw "RunOrganisationWorkflowSmoke requires ClientCount of at least 2."
}

$ExtraServerArgs = Normalize-ArgumentList -Arguments $ExtraServerArgs
$ExtraClientArgs = Normalize-ArgumentList -Arguments $ExtraClientArgs

Assert-Path $ServerGameDir "Server game directory"
Assert-Path (Join-Path $ServerGameDir "Schedule I.exe") "Server executable"
Assert-GoldbergSteamApi -GameDir $ServerGameDir -Description "Server game directory"
foreach ($resolvedClientGameDir in $resolvedClientGameDirs) {
    Assert-Path $resolvedClientGameDir "Client game directory"
    Assert-Path (Join-Path $resolvedClientGameDir "Schedule I.exe") "Client executable"
    Assert-GoldbergSteamApi -GameDir $resolvedClientGameDir -Description "Client game directory"
}

if ([string]::IsNullOrWhiteSpace($ResultsRoot)) {
    $ResultsRoot = Join-Path $PSScriptRoot "artifacts"
}

$runStartedAt = Get-Date
$runId = $runStartedAt.ToString("yyyyMMdd-HHmmss")
$resultsDir = Join-Path $ResultsRoot $runId
New-Item -ItemType Directory -Path $resultsDir -Force | Out-Null

$serverLog = Join-Path $ServerGameDir "MelonLoader\Latest.log"
$clientLogs = $resolvedClientGameDirs | ForEach-Object { Join-Path $_ "MelonLoader\Latest.log" }
$uniqueClientLogs = @($clientLogs | Select-Object -Unique)
$clientLog = $clientLogs[0]
$playerLog = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)) "Low\TVGS\Schedule I\Player.log"

$serverProcess = $null
$clientProcesses = @()
$failed = $false
$failureReasons = New-Object System.Collections.Generic.List[string]

try {
    if ($StopExistingProcesses -and -not $KeepExistingProcesses) {
        Stop-ScheduleOneProcesses -Roots (@($ServerGameDir) + ($resolvedClientGameDirs | Select-Object -Unique))
        Start-Sleep -Seconds 2
    }
    else {
        Write-Host "Skipping pre-run process cleanup. Existing Schedule I processes will not be stopped." -ForegroundColor DarkGray
    }

    if ($BuildAndDeploy) {
        Invoke-BuildAndDeploy
    }

    $serverExpectedMods = @("DedicatedServerMod_Mono_Server.dll", "Organisations_Mono_Server.dll")
    $serverAllowedMods = @("DedicatedServerMod_Mono_Server.dll", "Organisations_Mono_Server.dll", "S1API.Mono.MelonLoader.dll")
    $clientExpectedMods = @("DedicatedServerMod_Mono_Client.dll", "Organisations_Mono_Client.dll")
    $clientAllowedMods = @("DedicatedServerMod_Mono_Client.dll", "Organisations_Mono_Client.dll", "S1API.Mono.MelonLoader.dll")
    $allowedUserLibs = @("SteamNetworkLib-Mono.dll", "SteamNetworkLib.dll")
    Assert-InstallContents -GameDir $ServerGameDir -Description "Server game directory" -ExpectedMods $serverExpectedMods -AllowedMods $serverAllowedMods -AllowedUserLibs $allowedUserLibs -AllowExtra:$AllowExtraMods
    foreach ($clientDeployDir in ($resolvedClientGameDirs | Select-Object -Unique)) {
        Assert-InstallContents -GameDir $clientDeployDir -Description "Client game directory" -ExpectedMods $clientExpectedMods -AllowedMods $clientAllowedMods -AllowedUserLibs $allowedUserLibs -AllowExtra:$AllowExtraMods
    }

    if ($ArchiveDedicatedServerSave) {
        Archive-DefaultDedicatedServerSave -GameDir $ServerGameDir -RunId $runId
    }

    Clear-LogFile -Path $serverLog
    foreach ($log in $clientLogs) {
        Clear-LogFile -Path $log
    }

    Write-Step "Launching dedicated server"
    $serverExe = Join-Path $ServerGameDir "Schedule I.exe"
    $serverArgs = @(
        "--batchmode",
        "--nographics",
        "--dedicated-server",
        "--stdio-console",
        "--require-auth",
        "--auth-provider",
        "steam_game_server",
        "--steam-gs-anonymous",
        "--server-port",
        $Port.ToString()
    )
    if ($ExtraServerArgs.Count -gt 0) {
        $serverArgs += $ExtraServerArgs
    }
    $serverProcess = Start-Process -FilePath $serverExe -ArgumentList $serverArgs -WorkingDirectory $ServerGameDir -PassThru -WindowStyle Hidden
    Write-Host "Server PID: $($serverProcess.Id)" -ForegroundColor Green

    $null = Wait-ForLogPattern -Path $serverLog -Pattern "DEDICATED SERVER READY" -TimeoutSeconds $ServerReadyTimeoutSeconds -Description "server ready"

    for ($i = 0; $i -lt $ClientCount; $i++) {
        $steamId = $ClientSteamIds[$i]
        $name = $ClientNames[$i]
        $currentClientGameDir = $resolvedClientGameDirs[$i]

        Write-Step "Launching client $($i + 1): $name ($steamId)"
        Set-GoldbergConfig -GameDir $currentClientGameDir -SteamId $steamId -Name $name
        Start-Sleep -Milliseconds 300

        $clientExe = Join-Path $currentClientGameDir "Schedule I.exe"
        $clientArgs = @(
            "--server-ip",
            $ServerIp,
            "--server-port",
            $Port.ToString(),
            "--disable-friends-check"
        )
        if ($RunOrganisationWorkflowSmoke) {
            if ($i -eq 0) {
                $clientArgs += @(
                    "--org-smoke-create-name",
                    $SmokeOrganisationName,
                    "--org-smoke-invite-target",
                    $ClientSteamIds[1]
                )

                if ($WorkflowInviteDelaySeconds -gt 0) {
                    $clientArgs += @(
                        "--org-smoke-invite-delay-seconds",
                        $WorkflowInviteDelaySeconds.ToString()
                    )
                }
            }
            else {
                $clientArgs += "--org-smoke-auto-accept-invites"
            }
        }
        if ($ExtraClientArgs.Count -gt 0) {
            $clientArgs += $ExtraClientArgs
        }
        $clientWindowStyle = if ($HideClients) { "Hidden" } else { "Normal" }
        $clientProcess = Start-Process -FilePath $clientExe -ArgumentList $clientArgs -WorkingDirectory $currentClientGameDir -PassThru -WindowStyle $clientWindowStyle
        $clientProcesses += $clientProcess
        Write-Host "Client PID: $($clientProcess.Id)" -ForegroundColor Green

        if ($i -lt ($ClientCount - 1)) {
            $nextClientGameDir = $resolvedClientGameDirs[$i + 1]
            $mustWaitForStableIdentity = $WaitForClientJoinBeforeNextLaunch `
                -or ($RunOrganisationWorkflowSmoke -and [string]::Equals($currentClientGameDir, $nextClientGameDir, [System.StringComparison]::OrdinalIgnoreCase))

            if ($mustWaitForStableIdentity) {
                $null = Wait-ForLogPattern -Path $serverLog -Pattern "Player joined: $name" -TimeoutSeconds $ClientJoinBeforeNextLaunchTimeoutSeconds -Description "client $($i + 1) join before launching the next client"
            }
            else {
                Start-Sleep -Seconds $ClientLaunchGapSeconds
            }
        }
    }

    Write-Step "Watching logs for $ClientStableSeconds seconds"
    $deadline = (Get-Date).AddSeconds($ClientStableSeconds)
    while ((Get-Date) -lt $deadline) {
        if (Test-LogHasPattern -Path $serverLog -Pattern "PacketId of 65535") {
            $failed = $true
            $failureReasons.Add("Server reported unhandled PacketId 65535 kick.")
            break
        }

        $clientRunLogs = Get-MelonRunLogs -GameDirs $resolvedClientGameDirs -Since $runStartedAt
        if ($clientRunLogs | Where-Object { Test-LogHasPattern -Path $_ -Pattern "Dedicated server connection stopped unexpectedly" }) {
            $failed = $true
            $failureReasons.Add("Client reported unexpected dedicated-server disconnect.")
            break
        }

        Start-Sleep -Milliseconds 500
    }

    $clientRunLogs = Get-MelonRunLogs -GameDirs $resolvedClientGameDirs -Since $runStartedAt
    if ($clientRunLogs.Count -eq 0) {
        $clientRunLogs = $uniqueClientLogs
    }

    $snapshotSeen = -not ($clientRunLogs | Where-Object { -not (Test-LogHasPattern -Path $_ -Pattern "Snapshot updated.") })
    if (-not $snapshotSeen) {
        $failed = $true
        $failureReasons.Add("At least one client did not report an organisation snapshot update.")
    }

    $questReadySeen = -not ($clientRunLogs | Where-Object { -not (Test-LogHasPattern -Path $_ -Pattern "Quest system initialization completed") })
    if (-not $questReadySeen) {
        $failureReasons.Add("At least one client did not report quest system initialization completed during the watch window.")
    }

    if ($RunOrganisationWorkflowSmoke) {
        $requiredServerPatterns = @(
            "Received organisation command 'org_create_request'",
            "Received organisation command 'org_invite_request'",
            "Received organisation command 'org_invite_accept_request'"
        )

        foreach ($pattern in $requiredServerPatterns) {
            if (-not (Test-LogHasPattern -Path $serverLog -Pattern $pattern)) {
                $failed = $true
                $failureReasons.Add("Server log did not contain workflow marker: $pattern")
            }
        }

        $serverFinalSnapshotsSeen = $true
        foreach ($steamId in @($ClientSteamIds[0], $ClientSteamIds[1])) {
            $snapshotPattern = "Sending organisation snapshot. HasOrganisation=True, PendingInvites=0, PlayerSteamId=$steamId"
            if (-not (Test-LogHasPattern -Path $serverLog -Pattern $snapshotPattern)) {
                $serverFinalSnapshotsSeen = $false
                $failed = $true
                $failureReasons.Add("Server log did not contain final organisation snapshot marker for $steamId.")
            }
        }

        $workflowCompleteCount = 0
        foreach ($log in $clientRunLogs) {
            if (Test-Path -LiteralPath $log) {
                $workflowCompleteCount += @(Select-String -LiteralPath $log -Pattern "[OrgSmoke] Workflow complete" -SimpleMatch).Count
            }
        }

        $expectedWorkflowCompleteCount = [Math]::Min($ClientCount, $clientRunLogs.Count)
        if ($workflowCompleteCount -lt $expectedWorkflowCompleteCount -and -not $serverFinalSnapshotsSeen) {
            $failed = $true
            $failureReasons.Add("Expected at least $expectedWorkflowCompleteCount client workflow completion marker(s), found $workflowCompleteCount.")
        }
        elseif ($workflowCompleteCount -lt $expectedWorkflowCompleteCount) {
            Write-Host "Client workflow completion markers were incomplete ($workflowCompleteCount/$expectedWorkflowCompleteCount), but server final snapshots confirmed the workflow." -ForegroundColor Yellow
        }
    }

    if (Test-Path -LiteralPath $serverLog) {
        Copy-Item -LiteralPath $serverLog -Destination (Join-Path $resultsDir "Server.Latest.log") -Force
    }

    for ($i = 0; $i -lt $clientRunLogs.Count; $i++) {
        $log = $clientRunLogs[$i]
        if (Test-Path -LiteralPath $log) {
            $clientLogName = if ($i -eq 0) { "Client.Latest.log" } else { "Client$($i + 1).Latest.log" }
            Copy-Item -LiteralPath $log -Destination (Join-Path $resultsDir $clientLogName) -Force
        }
    }
    Copy-IfExists -Source $playerLog -DestinationDirectory $resultsDir

    $timeline = @()
    $timeline += "=== SERVER TIMELINE ==="
    $timeline += Get-RelevantTimeline -Path $serverLog
    $timeline += ""
    for ($i = 0; $i -lt $clientRunLogs.Count; $i++) {
        $timeline += "=== CLIENT $($i + 1) MELON TIMELINE ==="
        $timeline += Get-RelevantTimeline -Path $clientRunLogs[$i]
        $timeline += ""
    }
    $timeline += "=== PLAYER LOG TIMELINE ==="
    $timeline += Get-RelevantTimeline -Path $playerLog
    $timeline | Set-Content -LiteralPath (Join-Path $resultsDir "timeline.txt") -Encoding UTF8

    $summary = @()
    $summary += "RunId=$runId"
    $summary += "ServerGameDir=$ServerGameDir"
    $summary += "ClientGameDirs=$($resolvedClientGameDirs -join ';')"
    $summary += "ClientCount=$ClientCount"
    $summary += "ClientRunLogCount=$($clientRunLogs.Count)"
    $summary += "RunOrganisationWorkflowSmoke=$RunOrganisationWorkflowSmoke"
    $summary += "SmokeOrganisationName=$SmokeOrganisationName"
    $summary += "WorkflowInviteDelaySeconds=$WorkflowInviteDelaySeconds"
    $summary += "WaitForClientJoinBeforeNextLaunch=$WaitForClientJoinBeforeNextLaunch"
    $summary += "AllowExtraMods=$AllowExtraMods"
    $summary += "StopExistingProcesses=$StopExistingProcesses"
    $summary += "ExtraServerArgs=$($ExtraServerArgs -join ' ')"
    $summary += "ExtraClientArgs=$($ExtraClientArgs -join ' ')"
    $summary += "SnapshotSeen=$snapshotSeen"
    $summary += "QuestReadySeen=$questReadySeen"
    if ($RunOrganisationWorkflowSmoke) {
        $summary += "ServerFinalSnapshotsSeen=$serverFinalSnapshotsSeen"
        $summary += "WorkflowCompleteCount=$workflowCompleteCount"
        $summary += "ExpectedWorkflowCompleteCount=$expectedWorkflowCompleteCount"
    }
    $summary += "Failed=$failed"
    foreach ($reason in $failureReasons) {
        $summary += "Reason=$reason"
    }

    $summary | Set-Content -LiteralPath (Join-Path $resultsDir "summary.txt") -Encoding UTF8

    Write-Host "Artifacts: $resultsDir" -ForegroundColor Cyan
    if ($failed) {
        Write-Host "Join smoke test failed:" -ForegroundColor Red
        foreach ($reason in $failureReasons) {
            Write-Host " - $reason" -ForegroundColor Red
        }
        exit 1
    }

    Write-Host "Join smoke test passed." -ForegroundColor Green
    exit 0
}
finally {
    if (-not $KeepProcesses) {
        foreach ($process in $clientProcesses) {
            if ($process -and -not $process.HasExited) {
                Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
            }
        }

        if ($serverProcess -and -not $serverProcess.HasExited) {
            Stop-Process -Id $serverProcess.Id -Force -ErrorAction SilentlyContinue
        }
    }
}
