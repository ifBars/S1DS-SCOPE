#if SERVER
using System;
using System.Collections.Generic;
using System.Linq;
using DedicatedServerMod.Organisations.Domain;
using DedicatedServerMod.Organisations.Persistence;
using DedicatedServerMod.Organisations.Utils;
#if IL2CPP
using Il2CppScheduleOne.Vehicles;
#else
using ScheduleOne.Vehicles;
#endif

namespace DedicatedServerMod.Organisations.Services;

internal sealed class OrganisationVehicleAccessService
{
    private readonly IOrganisationRepository _repository;
    private readonly OrganisationLogger _logger;

    public OrganisationVehicleAccessService(IOrganisationRepository repository, OrganisationLogger logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public void RegisterPurchasedVehicle(string buyerSteamId, LandVehicle vehicle)
    {
        if (string.IsNullOrWhiteSpace(buyerSteamId) || vehicle == null)
        {
            return;
        }

        string vehicleGuid = vehicle.GUID.ToString();
        if (string.IsNullOrWhiteSpace(vehicleGuid))
        {
            return;
        }

        string organisationId = ResolveCurrentOrganisationId(buyerSteamId);
        _repository.Current.VehicleOwnerships[vehicleGuid] = new VehicleOwnershipRecord
        {
            VehicleGuid = vehicleGuid,
            VehicleCode = vehicle.VehicleCode ?? string.Empty,
            OwnerSteamId = buyerSteamId,
            OwnerOrganisationId = organisationId,
            PurchasedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        };

        _repository.MarkDirty();
        _logger.Info($"Registered vehicle {vehicleGuid} ({vehicle.VehicleCode}) for {buyerSteamId}.");
    }

    public void AttachOwnedVehiclesToOrganisation(string steamId, string organisationId)
    {
        if (string.IsNullOrWhiteSpace(steamId) || string.IsNullOrWhiteSpace(organisationId))
        {
            return;
        }

        bool changed = false;
        foreach (VehicleOwnershipRecord ownership in _repository.Current.VehicleOwnerships.Values)
        {
            if (!string.Equals(ownership.OwnerSteamId, steamId, StringComparison.OrdinalIgnoreCase)
                || string.Equals(ownership.OwnerOrganisationId, organisationId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            ownership.OwnerOrganisationId = organisationId;
            ownership.UpdatedAtUtc = DateTime.UtcNow;
            changed = true;
        }

        if (!changed)
        {
            return;
        }

        _repository.MarkDirty();
        _logger.Info($"Linked owned vehicles for {steamId} to organisation {organisationId}.");
    }

    public bool CanPlayerAccessVehicle(string viewerSteamId, string vehicleGuid)
    {
        if (string.IsNullOrWhiteSpace(viewerSteamId) || string.IsNullOrWhiteSpace(vehicleGuid))
        {
            return false;
        }

        if (!_repository.Current.VehicleOwnerships.TryGetValue(vehicleGuid, out VehicleOwnershipRecord? ownership))
        {
            return false;
        }

        return CanPlayerAccessOwnership(viewerSteamId, ownership);
    }

    public List<string> GetOwnedVehicleGuidsForPlayer(string steamId)
    {
        if (string.IsNullOrWhiteSpace(steamId))
        {
            return new List<string>();
        }

        return _repository.Current.VehicleOwnerships.Values
            .Where(ownership => string.Equals(ownership.OwnerSteamId, steamId, StringComparison.OrdinalIgnoreCase))
            .Select(ownership => ownership.VehicleGuid)
            .Where(guid => !string.IsNullOrWhiteSpace(guid))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(guid => guid, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public List<string> GetAccessibleVehicleGuidsForPlayer(string steamId)
    {
        if (string.IsNullOrWhiteSpace(steamId))
        {
            return new List<string>();
        }

        return _repository.Current.VehicleOwnerships.Values
            .Where(ownership => CanPlayerAccessOwnership(steamId, ownership))
            .Select(ownership => ownership.VehicleGuid)
            .Where(guid => !string.IsNullOrWhiteSpace(guid))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(guid => guid, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private bool CanPlayerAccessOwnership(string viewerSteamId, VehicleOwnershipRecord ownership)
    {
        if (string.Equals(ownership.OwnerSteamId, viewerSteamId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(ownership.OwnerOrganisationId)
            || !_repository.Current.Organisations.TryGetValue(ownership.OwnerOrganisationId, out OrganisationRecord? organisation)
            || !string.Equals(organisation.OwnerSteamId, viewerSteamId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return _repository.Current.PlayerToOrganisation.TryGetValue(ownership.OwnerSteamId, out string? currentOrganisationId)
            && string.Equals(currentOrganisationId, ownership.OwnerOrganisationId, StringComparison.OrdinalIgnoreCase);
    }

    private string ResolveCurrentOrganisationId(string steamId)
    {
        if (string.IsNullOrWhiteSpace(steamId))
        {
            return string.Empty;
        }

        return _repository.Current.PlayerToOrganisation.TryGetValue(steamId, out string? organisationId)
            ? organisationId ?? string.Empty
            : string.Empty;
    }
}
#endif
