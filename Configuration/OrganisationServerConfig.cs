#if SERVER
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DedicatedServerMod.API.Configuration;
using DedicatedServerMod.API.Toml;
using Newtonsoft.Json;

namespace DedicatedServerMod.Organisations.Configuration;

internal sealed class OrganisationServerConfig
{
    private const string Team10V10FirstTo100KPreset = "Team10v10FirstTo100k";
    private const string LegacyTeamTablePrefix = "teams.";

    private static readonly TomlConfigSchema<OrganisationServerConfig> Schema = BuildSchema();

    public string EventPreset { get; set; } = string.Empty;

    public float StartingOnlineBalance { get; set; } = 0f;

    public int MaxOrganisationMembers { get; set; } = 0;

    public float WeeklyAtmDepositLimit { get; set; } = Constants.WeeklyAtmDepositLimit;

    public bool EnableDealerRetentionFees { get; set; } = false;

    public float WeeklyDealerRetentionFee { get; set; } = Constants.WeeklyDealerRetentionFee;

    public float VictoryOnlineBalanceTarget { get; set; } = 0f;

    public bool AnnounceVictoryToAllPlayers { get; set; } = true;

    public bool EnableTeamTestingNotes { get; set; } = false;

    public bool EnableConfiguredTeams { get; set; } = false;

    public bool AllowConfiguredTeamReassignment { get; set; } = false;

    public bool RestrictOrganisationCreationToConfiguredTeams { get; set; } = false;

    public bool RestrictConfiguredTeamInvitesToRoster { get; set; } = false;

    public bool LockConfiguredTeamRosterMembership { get; set; } = false;

    public bool ApplyConfiguredTeamColorToOutfit { get; set; } = false;

    public List<ConfiguredTeamDefinition> ConfiguredTeams { get; set; } = new List<ConfiguredTeamDefinition>
    {
        new ConfiguredTeamDefinition
        {
            Name = "Red",
            TeamColorHex = "#d94141",
        },
        new ConfiguredTeamDefinition
        {
            Name = "Blue",
            TeamColorHex = "#3b6df2",
        },
    };

    public void Normalize()
    {
        EventPreset = (EventPreset ?? string.Empty).Trim();

        if (float.IsNaN(StartingOnlineBalance) || float.IsInfinity(StartingOnlineBalance) || StartingOnlineBalance < 0f)
        {
            StartingOnlineBalance = 0f;
        }

        if (MaxOrganisationMembers < 0)
        {
            MaxOrganisationMembers = 0;
        }

        if (float.IsNaN(WeeklyAtmDepositLimit) || float.IsInfinity(WeeklyAtmDepositLimit) || WeeklyAtmDepositLimit < 0f)
        {
            WeeklyAtmDepositLimit = Constants.WeeklyAtmDepositLimit;
        }

        if (float.IsNaN(WeeklyDealerRetentionFee) || float.IsInfinity(WeeklyDealerRetentionFee) || WeeklyDealerRetentionFee < 0f)
        {
            WeeklyDealerRetentionFee = Constants.WeeklyDealerRetentionFee;
        }

        if (float.IsNaN(VictoryOnlineBalanceTarget) || float.IsInfinity(VictoryOnlineBalanceTarget) || VictoryOnlineBalanceTarget < 0f)
        {
            VictoryOnlineBalanceTarget = 0f;
        }

        ConfiguredTeams ??= new List<ConfiguredTeamDefinition>();
        foreach (ConfiguredTeamDefinition team in ConfiguredTeams)
        {
            team.Normalize();
        }

        ConfiguredTeams = ConfiguredTeams
            .Where(team => !string.IsNullOrWhiteSpace(team.Name))
            .GroupBy(team => team.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();

        ApplyEventPreset();
        RemoveDuplicateConfiguredTeamMembers();
    }

    public static OrganisationServerConfig Load(Organisations.Utils.OrganisationLogger logger)
    {
        string path = GetConfigPath();
        try
        {
            string legacyPath = GetLegacyConfigPath();
            if (!File.Exists(path) && File.Exists(legacyPath))
            {
                OrganisationServerConfig migrated = JsonConvert.DeserializeObject<OrganisationServerConfig>(File.ReadAllText(legacyPath))
                    ?? new OrganisationServerConfig();
                migrated.Normalize();
                CreateStore(path).Save(migrated);
                logger.Info($"Migrated legacy Organisations config from {legacyPath} to {path}.");
                return migrated;
            }

            TomlConfigStore<OrganisationServerConfig> store = CreateStore(path);
            TomlConfigLoadResult<OrganisationServerConfig> loadResult = store.LoadOrCreate();
            LogTomlDiagnostics(loadResult, logger);

            OrganisationServerConfig config = loadResult.Config ?? new OrganisationServerConfig();
            config.Normalize();
            if (loadResult.RequiresSave)
            {
                store.Save(config);
            }

            logger.Info($"Loaded Organisations config from {path}.");
            return config;
        }
        catch (Exception ex)
        {
            logger.Error("Failed to load Organisations config. Falling back to defaults.", ex);
            OrganisationServerConfig fallback = new OrganisationServerConfig();
            fallback.Normalize();
            return fallback;
        }
    }

    private static string GetConfigPath()
    {
        return ModConfigPaths.GetPath(Constants.ModName, Constants.ConfigFileName);
    }

    private static string GetLegacyConfigPath()
    {
        return Path.Combine(MelonLoader.Utils.MelonEnvironment.UserDataDirectory, Constants.LegacyConfigFileName);
    }

    private static TomlConfigStore<OrganisationServerConfig> CreateStore(string path)
    {
        return new TomlConfigStore<OrganisationServerConfig>(
            Schema,
            new TomlConfigStoreOptions<OrganisationServerConfig>
            {
                Path = path,
                CreateInstance = () => new OrganisationServerConfig(),
                NormalizeDocument = RemoveLegacyTeamTables,
            });
    }

    private static TomlConfigSchema<OrganisationServerConfig> BuildSchema()
    {
        return TomlConfigSchemaBuilder
            .For<OrganisationServerConfig>()
            .FileHeader(
                "Organisations addon configuration.",
                "Configured team rosters are written as TOML inline table arrays.")
            .Normalize(config => config.Normalize())
            .Section("event", section => section
                .Comment("Event preset and win condition settings.")
                .Option(config => config.EventPreset, option => option
                    .Key("eventPreset")
                    .Comment("Optional preset. Use 'Team10v10FirstTo100k' for the configured 10v10 race mode."))
                .Option(config => config.VictoryOnlineBalanceTarget, option => option
                    .Key("victoryOnlineBalanceTarget")
                    .Comment("Online balance required to trigger victory announcements. Use 0 to disable."))
                .Option(config => config.AnnounceVictoryToAllPlayers, option => option
                    .Key("announceVictoryToAllPlayers")
                    .Comment("Broadcast victory announcements to every connected player."))
                .Option(config => config.EnableTeamTestingNotes, option => option
                    .Key("enableTeamTestingNotes")
                    .Comment("Enable extra team-assignment test notes in logs and player feedback.")))
            .Section("banking", section => section
                .Comment("Organisation wallet defaults and ATM constraints.")
                .Option(config => config.StartingOnlineBalance, option => option
                    .Key("startingOnlineBalance")
                    .Comment("Online balance assigned when an organisation is created."))
                .Option(config => config.WeeklyAtmDepositLimit, option => option
                    .Key("weeklyAtmDepositLimit")
                    .Comment("Maximum cash a player can deposit to an organisation wallet each week.")))
            .Section("dealers", section => section
                .Comment("Scoped dealer retention settings.")
                .Option(config => config.EnableDealerRetentionFees, option => option
                    .Key("enableDealerRetentionFees")
                    .Comment("When true, recruited scoped dealers must pay a weekly retention fee from dealer cash or they stop working for that player/organisation."))
                .Option(config => config.WeeklyDealerRetentionFee, option => option
                    .Key("weeklyDealerRetentionFee")
                    .Comment("Cash charged from each recruited scoped dealer during the weekly ATM reset when dealer retention fees are enabled.")))
            .Section("membership", section => section
                .Comment("Organisation membership limits.")
                .Option(config => config.MaxOrganisationMembers, option => option
                    .Key("maxOrganisationMembers")
                    .Comment("Maximum members per organisation. Use 0 for unlimited.")))
            .Section("teams", section => section
                .Comment("Configured-team behavior and inline roster definitions.")
                .Option(config => config.EnableConfiguredTeams, option => option
                    .Key("enableConfiguredTeams")
                    .Comment("Automatically map configured roster members to matching organisations."))
                .Option(config => config.AllowConfiguredTeamReassignment, option => option
                    .Key("allowConfiguredTeamReassignment")
                    .Comment("Allow players to be reassigned when their configured team changes."))
                .Option(config => config.RestrictOrganisationCreationToConfiguredTeams, option => option
                    .Key("restrictOrganisationCreationToConfiguredTeams")
                    .Comment("Only allow configured team owners/members to create configured organisations."))
                .Option(config => config.RestrictConfiguredTeamInvitesToRoster, option => option
                    .Key("restrictConfiguredTeamInvitesToRoster")
                    .Comment("Only allow configured team organisations to invite listed roster members."))
                .Option(config => config.LockConfiguredTeamRosterMembership, option => option
                    .Key("lockConfiguredTeamRosterMembership")
                    .Comment("Prevent configured-team members from joining or remaining in another organisation."))
                .Option(config => config.ApplyConfiguredTeamColorToOutfit, option => option
                    .Key("applyConfiguredTeamColorToOutfit")
                    .Comment("Apply configured team color to organisation outfit color when available."))
                .Option(config => config.ConfiguredTeams, option => option
                    .Key("configuredTeams")
                    .Comments(
                        "Configured teams as inline table objects.",
                        "Each item supports name, teamColorHex, ownerSteamId, and memberSteamIds.")))
            .Build();
    }

    private static bool RemoveLegacyTeamTables(TomlDocument document)
    {
        bool removed = false;
        foreach (TomlTable table in document.Tables.Where(table => table.Name.StartsWith(LegacyTeamTablePrefix, StringComparison.OrdinalIgnoreCase)).ToList())
        {
            removed |= document.RemoveTable(table.Name);
        }

        return removed;
    }

    private static void LogTomlDiagnostics(TomlConfigLoadResult<OrganisationServerConfig> loadResult, Organisations.Utils.OrganisationLogger logger)
    {
        foreach (TomlDiagnostic diagnostic in loadResult.Diagnostics)
        {
            string location = diagnostic.LineNumber > 0 ? $" line {diagnostic.LineNumber}" : string.Empty;
            string sectionLabel = string.IsNullOrWhiteSpace(diagnostic.TableName) ? "root" : diagnostic.TableName;
            string keyLabel = string.IsNullOrWhiteSpace(diagnostic.Key) ? string.Empty : $" key '{diagnostic.Key}'";
            logger.Warning($"Config warning in section '{sectionLabel}'{keyLabel}{location}: {diagnostic.Message}");
        }

        foreach (TomlConfigValidationIssue issue in loadResult.ValidationIssues)
        {
            string sectionLabel = string.IsNullOrWhiteSpace(issue.Section) ? "root" : issue.Section;
            string keyLabel = string.IsNullOrWhiteSpace(issue.Key) ? string.Empty : $" key '{issue.Key}'";
            logger.Warning($"Config binding warning in section '{sectionLabel}'{keyLabel}: {issue.Message}");
        }
    }

    private void ApplyEventPreset()
    {
        if (!string.Equals(EventPreset, Team10V10FirstTo100KPreset, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        EnableConfiguredTeams = true;
        ApplyConfiguredTeamColorToOutfit = true;
        AnnounceVictoryToAllPlayers = true;
        RestrictOrganisationCreationToConfiguredTeams = true;
        RestrictConfiguredTeamInvitesToRoster = true;
        LockConfiguredTeamRosterMembership = true;

        if (MaxOrganisationMembers <= 0)
        {
            MaxOrganisationMembers = 10;
        }

        if (VictoryOnlineBalanceTarget <= 0f)
        {
            VictoryOnlineBalanceTarget = 100000f;
        }

        EnsureConfiguredTeam("Red", "#D94141");
        EnsureConfiguredTeam("Blue", "#3B6DF2");
    }

    private void EnsureConfiguredTeam(string name, string colorHex)
    {
        ConfiguredTeamDefinition? existing = ConfiguredTeams.FirstOrDefault(team =>
            string.Equals(team.Name, name, StringComparison.OrdinalIgnoreCase));
        if (existing == null)
        {
            ConfiguredTeams.Add(new ConfiguredTeamDefinition
            {
                Name = name,
                TeamColorHex = colorHex,
            });
            return;
        }

        if (string.IsNullOrWhiteSpace(existing.TeamColorHex))
        {
            existing.TeamColorHex = colorHex;
        }
    }

    private void RemoveDuplicateConfiguredTeamMembers()
    {
        HashSet<string> assignedSteamIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (ConfiguredTeamDefinition team in ConfiguredTeams)
        {
            List<string> uniqueMembers = new List<string>();
            foreach (string memberSteamId in team.MemberSteamIds)
            {
                if (assignedSteamIds.Add(memberSteamId))
                {
                    uniqueMembers.Add(memberSteamId);
                }
            }

            team.MemberSteamIds = uniqueMembers;
            if (!string.IsNullOrWhiteSpace(team.OwnerSteamId) && !team.ContainsMember(team.OwnerSteamId))
            {
                team.OwnerSteamId = string.Empty;
            }
        }
    }
}

internal sealed class ConfiguredTeamDefinition
{
    [JsonProperty("name")]
    public string Name { get; set; } = string.Empty;

    [JsonProperty("teamColorHex")]
    public string TeamColorHex { get; set; } = string.Empty;

    [JsonProperty("ownerSteamId")]
    public string OwnerSteamId { get; set; } = string.Empty;

    [JsonProperty("memberSteamIds")]
    public List<string> MemberSteamIds { get; set; } = new List<string>();

    public void Normalize()
    {
        Name = (Name ?? string.Empty).Trim();
        TeamColorHex = NormalizeHexColor(TeamColorHex);
        OwnerSteamId = (OwnerSteamId ?? string.Empty).Trim();
        MemberSteamIds ??= new List<string>();
        MemberSteamIds = MemberSteamIds
            .Where(member => !string.IsNullOrWhiteSpace(member))
            .Select(member => member.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (!string.IsNullOrWhiteSpace(OwnerSteamId) && !MemberSteamIds.Contains(OwnerSteamId, StringComparer.OrdinalIgnoreCase))
        {
            MemberSteamIds.Insert(0, OwnerSteamId);
        }
    }

    public bool ContainsMember(string steamId)
    {
        return !string.IsNullOrWhiteSpace(steamId)
            && MemberSteamIds.Contains(steamId, StringComparer.OrdinalIgnoreCase);
    }

    private static string NormalizeHexColor(string value)
    {
        string color = (value ?? string.Empty).Trim();
        if (color.Length == 6)
        {
            color = "#" + color;
        }

        if (color.Length != 7 || color[0] != '#')
        {
            return string.Empty;
        }

        for (int i = 1; i < color.Length; i++)
        {
            if (!Uri.IsHexDigit(color[i]))
            {
                return string.Empty;
            }
        }

        return color.ToUpperInvariant();
    }
}
#endif
