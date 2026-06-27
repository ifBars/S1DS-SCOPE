#if CLIENT
using System;
using DedicatedServerMod.Organisations.Contracts;
using DedicatedServerMod.Organisations.Utils;
#if IL2CPP
using Il2CppScheduleOne.AvatarFramework.Customization;
using Il2CppScheduleOne.PlayerScripts;
#else
using ScheduleOne.AvatarFramework.Customization;
using ScheduleOne.PlayerScripts;
#endif
using UnityEngine;

namespace DedicatedServerMod.Organisations.Client.Services;

internal sealed class OrganisationTeamAppearanceService
{
    private const string DefaultTeamTopPath = "Avatar/Layers/Top/T-Shirt";

    private readonly OrganisationLogger _logger;
    private string _lastAppliedSignature = string.Empty;
    private DateTime _nextAttemptAtUtc = DateTime.MinValue;

    public OrganisationTeamAppearanceService(OrganisationLogger logger)
    {
        _logger = logger;
    }

    public void Clear()
    {
        _lastAppliedSignature = string.Empty;
        _nextAttemptAtUtc = DateTime.MinValue;
    }

    public void Tick(OrganisationSnapshotDto snapshot)
    {
        if (snapshot == null
            || !snapshot.HasOrganisation
            || !snapshot.ShouldApplyTeamColorToOutfit
            || string.IsNullOrWhiteSpace(snapshot.TeamColorHex))
        {
            return;
        }

        DateTime utcNow = DateTime.UtcNow;
        if (utcNow < _nextAttemptAtUtc)
        {
            return;
        }

        _nextAttemptAtUtc = utcNow.AddSeconds(2);
        TryApplyTeamColor(snapshot);
    }

    private void TryApplyTeamColor(OrganisationSnapshotDto snapshot)
    {
        if (!ColorUtility.TryParseHtmlString(snapshot.TeamColorHex, out Color teamColor))
        {
            _logger.Warning($"Configured team color '{snapshot.TeamColorHex}' could not be parsed for outfit application.");
            return;
        }

        Player? player = Player.Local;
        if (player == null || !player.HasCompletedIntro)
        {
            return;
        }

        BasicAvatarSettings? settings = player.CurrentAvatarSettings;
        if (settings == null)
        {
            return;
        }

        bool addedDefaultTop = false;
        if (string.IsNullOrWhiteSpace(settings.Top))
        {
            settings.Top = DefaultTeamTopPath;
            addedDefaultTop = true;
        }

        if (!addedDefaultTop && ColorsNearlyEqual(settings.TopColor, teamColor))
        {
            return;
        }

        string signature = $"{snapshot.OrganisationId}|{snapshot.TeamColorHex.ToUpperInvariant()}";
        settings.TopColor = teamColor;
        player.SendAppearance(settings);

        if (!string.Equals(_lastAppliedSignature, signature, StringComparison.Ordinal))
        {
            string topMessage = addedDefaultTop ? $" using default top '{DefaultTeamTopPath}'" : string.Empty;
            _logger.Info($"Applied configured team outfit color {snapshot.TeamColorHex.ToUpperInvariant()}{topMessage} for organisation '{snapshot.OrganisationName}'.");
            _lastAppliedSignature = signature;
        }
    }

    private static bool ColorsNearlyEqual(Color left, Color right)
    {
        return Mathf.Abs(left.r - right.r) < 0.002f
            && Mathf.Abs(left.g - right.g) < 0.002f
            && Mathf.Abs(left.b - right.b) < 0.002f
            && Mathf.Abs(left.a - right.a) < 0.002f;
    }
}
#endif
