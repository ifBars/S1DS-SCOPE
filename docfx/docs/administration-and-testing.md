---
title: Administration and Testing
description: Fresh-save rollout, builds, smoke tests, and validation coverage.
---

# Administration and Testing

Run Organisations on a fresh dedicated-server save. Do not install it into an existing shared co-op save unless a migration plan has been written and tested for that save.

## Build

Build each side/runtime separately:

```powershell
dotnet restore Organisations.csproj -p:Configuration=Mono_Server -p:EffectiveConfiguration=Mono_Server -p:TargetFramework=netstandard2.1 --force-evaluate
dotnet build Organisations.csproj -c Mono_Server -p:EffectiveConfiguration=Mono_Server -p:AutomateLocalDeployment=false --no-restore

dotnet restore Organisations.csproj -p:Configuration=Mono_Client -p:EffectiveConfiguration=Mono_Client -p:TargetFramework=netstandard2.1 --force-evaluate
dotnet build Organisations.csproj -c Mono_Client -p:EffectiveConfiguration=Mono_Client -p:AutomateLocalDeployment=false --no-restore

dotnet restore Organisations.csproj -p:Configuration=Il2cpp_Server -p:EffectiveConfiguration=Il2cpp_Server -p:TargetFramework=net6.0 --force-evaluate
dotnet build Organisations.csproj -c Il2cpp_Server -p:EffectiveConfiguration=Il2cpp_Server -p:AutomateLocalDeployment=false --no-restore

dotnet restore Organisations.csproj -p:Configuration=Il2cpp_Client -p:EffectiveConfiguration=Il2cpp_Client -p:TargetFramework=net6.0 --force-evaluate
dotnet build Organisations.csproj -c Il2cpp_Client -p:EffectiveConfiguration=Il2cpp_Client -p:AutomateLocalDeployment=false --no-restore
```

The restore/build pairs are intentionally sequential. The Mono and IL2CPP configurations target different frameworks and should not race over the same intermediate asset files.

## Fast Invariant Tests

Run the lightweight workflow and scope-rule tests:

```powershell
dotnet run --project tests/Organisations.WorkflowSmokeTests/Organisations.WorkflowSmokeTests.csproj
```

These tests cover:

- Workflow smoke option parsing.
- Solo active-customer-contract ownership.
- Two organisation members sharing the same owner key.
- Separate owner-key rejection for active customer contracts.
- Dealer recruitment exclusivity across owner keys.
- Dealer contract owner resolution for assigned and unlocked customers.
- Dealer contract rejection when another owner key owns the active reservation.
- Optional dealer retention fee payment, loss, and disabled-toggle behavior.
- Dealer retention warning candidate selection and once-per-day deduping.

## Dealer Retention Configuration

Dealer retention is off by default:

```toml
[dealers]
enableDealerRetentionFees = false
weeklyDealerRetentionFee = 1000
```

Enable it when the server should punish unattended dealer networks. The weekly fee is charged from each recruited dealer's scoped dealer cash. If the dealer cannot pay, the dealer stops working for that player or organisation and the exclusive recruitment lock is released.

The warning pass runs on Sunday. Any recruited dealer below the fee sends an owner-scoped text warning before Monday's weekly charge. The warning is recorded once per in-game day per dealer, so reconnects and repeated sync checks should not spam the same warning.

## Live Join Smoke

For real client/server sync, run:

```powershell
.\tests\Run-JoinSmokeTest.ps1 -BuildAndDeploy -RunOrganisationWorkflowSmoke -ClientCount 2 -HideClients -ClientStableSeconds 120 -WorkflowInviteDelaySeconds 20 -ExtraServerArgs @('--org-diag-log-quest-variables','--org-diag-log-deaddrops')
```

The smoke runner creates an organisation, invites a second client, accepts the invite, waits for snapshots, and checks final server snapshots. Use this after changes to messaging, snapshots, lifecycle hooks, or persistence.
