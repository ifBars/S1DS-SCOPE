#if SERVER
using System;
using System.Collections.Generic;
using System.Linq;
using DedicatedServerMod.Organisations.Domain;
using DedicatedServerMod.Organisations.Persistence;
using DedicatedServerMod.Organisations.Utils;
#if IL2CPP
using Il2CppScheduleOne.Property;
#else
using ScheduleOne.Property;
#endif

namespace DedicatedServerMod.Organisations.Services;

internal sealed class OrganisationPropertyScopeService
{
    private readonly IOrganisationRepository _repository;
    private readonly IOrganisationService _organisationService;
    private readonly OrganisationLogger _logger;

    public OrganisationPropertyScopeService(IOrganisationRepository repository, IOrganisationService organisationService, OrganisationLogger logger)
    {
        _repository = repository;
        _organisationService = organisationService;
        _logger = logger;
    }

    public void EnsurePersonalScopeCaptured(string steamId)
    {
        _ = steamId;
        ReconcileReservationsWithWorldState();
    }

    public void ClonePersonalScopeToOrganisation(string steamId, string organisationId)
    {
        if (string.IsNullOrWhiteSpace(steamId) || string.IsNullOrWhiteSpace(organisationId))
        {
            return;
        }

        ReconcileReservationsWithWorldState();

        bool changed = false;
        string personalOwnerKey = BuildPlayerOwnerKey(steamId);
        string organisationOwnerKey = BuildOrganisationOwnerKey(organisationId);

        foreach (PropertyReservationRecord reservation in _repository.Current.PropertyReservations.Values)
        {
            HydrateLegacyReservationOwnership(reservation);
            if (!string.Equals(reservation.OwnerKey, personalOwnerKey, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            reservation.OwnerKey = organisationOwnerKey;
            reservation.OwnerOrganisationId = organisationId;
            reservation.UpdatedAtUtc = DateTime.UtcNow;
            changed = true;
        }

        if (!changed)
        {
            return;
        }

        SyncLegacyOrganisationOwnedPropertyCodes();
        _repository.MarkDirty();
        _logger.Info($"Linked owned properties for {steamId} to organisation {organisationId}.");
    }

    public void CaptureCurrentWorldStateForDeterministicScope()
    {
        ReconcileReservationsWithWorldState();
    }

    public void TryHydrateWorldForPlayer(string steamId)
    {
        _ = steamId;
        ReconcileReservationsWithWorldState();
    }

    public void NotifyWorldMutation(string reason)
    {
        _ = reason;
        ReconcileReservationsWithWorldState();
    }

    public bool TryReservePropertyForPlayer(string steamId, Property property, out string error)
    {
        error = string.Empty;
        if (property == null || string.IsNullOrWhiteSpace(property.PropertyCode))
        {
            error = "Invalid property.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(steamId))
        {
            error = "Player scope is not ready yet.";
            return false;
        }

        string ownerKey = _organisationService.ResolveOwnerKey(steamId);
        string organisationId = ResolveOrganisationId(ownerKey);
        ReconcileReservationsWithWorldState();

        if (_repository.Current.PropertyReservations.TryGetValue(property.PropertyCode, out PropertyReservationRecord? reservation))
        {
            HydrateLegacyReservationOwnership(reservation);
            if (string.Equals(reservation.OwnerKey, ownerKey, StringComparison.OrdinalIgnoreCase))
            {
                if (property.IsOwned)
                {
                    error = string.IsNullOrWhiteSpace(organisationId)
                        ? "Property is already owned by you."
                        : "Property is already owned by your organisation.";
                    return false;
                }

                return true;
            }

            error = string.IsNullOrWhiteSpace(reservation.OwnerOrganisationId)
                ? "Property is already owned by another player."
                : "Property is already owned by another player in a crew.";
            return false;
        }

        if (property.IsOwned)
        {
            error = "Property is already owned.";
            return false;
        }

        _repository.Current.PropertyReservations[property.PropertyCode] = new PropertyReservationRecord
        {
            PropertyCode = property.PropertyCode,
            OwnerKey = ownerKey,
            OwnerSteamId = steamId,
            OwnerOrganisationId = organisationId,
            ReservedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        };

        SyncLegacyOrganisationOwnedPropertyCodes();
        _repository.MarkDirty();
        _logger.Info($"Reserved property {property.PropertyCode} for owner={steamId} organisation={organisationId}.");
        return true;
    }

    public List<string> GetOwnedPropertyCodesForPlayer(string steamId)
    {
        ReconcileReservationsWithWorldState();

        if (string.IsNullOrWhiteSpace(steamId))
        {
            return new List<string>();
        }

        string ownerKey = _organisationService.ResolveOwnerKey(steamId);
        if (string.IsNullOrWhiteSpace(ownerKey))
        {
            return new List<string>();
        }

        return _repository.Current.PropertyReservations.Values
            .Where(reservation =>
            {
                HydrateLegacyReservationOwnership(reservation);
                return string.Equals(reservation.OwnerKey, ownerKey, StringComparison.OrdinalIgnoreCase);
            })
            .Select(reservation => reservation.PropertyCode)
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(code => code, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public List<string> GetAccessiblePropertyCodesForPlayer(string steamId)
    {
        ReconcileReservationsWithWorldState();

        if (string.IsNullOrWhiteSpace(steamId))
        {
            return new List<string>();
        }

        return _repository.Current.PropertyReservations.Values
            .Where(reservation => CanPlayerAccessReservation(steamId, reservation))
            .Select(reservation => reservation.PropertyCode)
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(code => code, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public bool CanPlayerAccessProperty(string steamId, string propertyCode)
    {
        ReconcileReservationsWithWorldState();

        if (string.IsNullOrWhiteSpace(steamId)
            || string.IsNullOrWhiteSpace(propertyCode))
        {
            return false;
        }

        if (!_repository.Current.PropertyReservations.TryGetValue(propertyCode, out PropertyReservationRecord? reservation))
        {
            return true;
        }

        return CanPlayerAccessReservation(steamId, reservation);
    }

    public bool CanOwnerAccessProperty(string ownerKey, string propertyCode)
    {
        ReconcileReservationsWithWorldState();

        if (string.IsNullOrWhiteSpace(ownerKey)
            || string.IsNullOrWhiteSpace(propertyCode))
        {
            return false;
        }

        if (!_repository.Current.PropertyReservations.TryGetValue(propertyCode, out PropertyReservationRecord? reservation))
        {
            return true;
        }

        HydrateLegacyReservationOwnership(reservation);
        return string.Equals(reservation.OwnerKey, ownerKey, StringComparison.OrdinalIgnoreCase);
    }

    public bool TryGetPropertyOwnerKey(string propertyCode, out string ownerKey)
    {
        ownerKey = string.Empty;
        ReconcileReservationsWithWorldState();

        if (string.IsNullOrWhiteSpace(propertyCode)
            || !_repository.Current.PropertyReservations.TryGetValue(propertyCode, out PropertyReservationRecord? reservation))
        {
            return false;
        }

        HydrateLegacyReservationOwnership(reservation);
        if (string.IsNullOrWhiteSpace(reservation.OwnerKey))
        {
            return false;
        }

        ownerKey = reservation.OwnerKey;
        return true;
    }

    public bool IsPropertyReserved(string propertyCode)
    {
        ReconcileReservationsWithWorldState();

        if (string.IsNullOrWhiteSpace(propertyCode))
        {
            return false;
        }

        return _repository.Current.PropertyReservations.ContainsKey(propertyCode);
    }

    public List<string> GetReservedPropertyCodes()
    {
        ReconcileReservationsWithWorldState();

        return _repository.Current.PropertyReservations.Values
            .Select(reservation => reservation.PropertyCode)
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(code => code, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private void ReconcileReservationsWithWorldState()
    {
        if (Property.Properties.Count == 0)
        {
            return;
        }

        HashSet<string> liveOwnedPropertyCodes = new HashSet<string>(
            Property.Properties.AsManagedEnumerable()
                .Where(property => property != null && property.IsOwned && !string.IsNullOrWhiteSpace(property.PropertyCode))
                .Select(property => property.PropertyCode),
            StringComparer.OrdinalIgnoreCase);

        bool changed = false;
        foreach (string propertyCode in _repository.Current.PropertyReservations.Keys.ToList())
        {
            if (liveOwnedPropertyCodes.Contains(propertyCode))
            {
                if (_repository.Current.PropertyReservations.TryGetValue(propertyCode, out PropertyReservationRecord? reservation))
                {
                    HydrateLegacyReservationOwnership(reservation);
                }

                continue;
            }

            _repository.Current.PropertyReservations.Remove(propertyCode);
            changed = true;
        }

        foreach (OrganisationRecord organisation in _repository.Current.Organisations.Values)
        {
            if (organisation.OwnedPropertyCodes == null || organisation.OwnedPropertyCodes.Count == 0)
            {
                continue;
            }

            string ownerKey = BuildOrganisationOwnerKey(organisation.OrgId);
            foreach (string propertyCode in organisation.OwnedPropertyCodes)
            {
                if (string.IsNullOrWhiteSpace(propertyCode)
                    || !liveOwnedPropertyCodes.Contains(propertyCode)
                    || _repository.Current.PropertyReservations.ContainsKey(propertyCode))
                {
                    continue;
                }

                _repository.Current.PropertyReservations[propertyCode] = new PropertyReservationRecord
                {
                    PropertyCode = propertyCode,
                    OwnerKey = ownerKey,
                    OwnerSteamId = organisation.OwnerSteamId,
                    OwnerOrganisationId = organisation.OrgId,
                    ReservedAtUtc = organisation.CreatedAtUtc == default ? DateTime.UtcNow : organisation.CreatedAtUtc,
                    UpdatedAtUtc = DateTime.UtcNow,
                };
                changed = true;
            }
        }

        if (!changed)
        {
            return;
        }

        SyncLegacyOrganisationOwnedPropertyCodes();
        _repository.MarkDirty();
        _logger.Info("Reconciled organisation property reservations against the live world state.");
    }

    private void SyncLegacyOrganisationOwnedPropertyCodes()
    {
        foreach (OrganisationRecord organisation in _repository.Current.Organisations.Values)
        {
            organisation.OwnedPropertyCodes.Clear();
        }

        foreach (PropertyReservationRecord reservation in _repository.Current.PropertyReservations.Values)
        {
            HydrateLegacyReservationOwnership(reservation);
            if (string.IsNullOrWhiteSpace(reservation.PropertyCode) || string.IsNullOrWhiteSpace(reservation.OwnerKey))
            {
                continue;
            }

            if (!reservation.OwnerKey.StartsWith("org:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string organisationId = reservation.OwnerKey.Substring(4);
            if (_repository.Current.Organisations.TryGetValue(organisationId, out OrganisationRecord? organisation))
            {
                organisation.OwnedPropertyCodes.Add(reservation.PropertyCode);
            }
        }
    }

    private static string BuildPlayerOwnerKey(string steamId)
    {
        return $"player:{steamId}";
    }

    private static string BuildOrganisationOwnerKey(string organisationId)
    {
        return $"org:{organisationId}";
    }

    private bool CanPlayerAccessReservation(string steamId, PropertyReservationRecord reservation)
    {
        HydrateLegacyReservationOwnership(reservation);
        string ownerKey = _organisationService.ResolveOwnerKey(steamId);
        if (string.IsNullOrWhiteSpace(ownerKey))
        {
            return false;
        }

        if (string.Equals(reservation.OwnerKey, ownerKey, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private void HydrateLegacyReservationOwnership(PropertyReservationRecord reservation)
    {
        if (reservation == null)
        {
            return;
        }

        bool changed = false;
        if (!string.IsNullOrWhiteSpace(reservation.OwnerOrganisationId))
        {
            string organisationOwnerKey = BuildOrganisationOwnerKey(reservation.OwnerOrganisationId);
            if (!string.Equals(reservation.OwnerKey, organisationOwnerKey, StringComparison.OrdinalIgnoreCase))
            {
                reservation.OwnerKey = organisationOwnerKey;
                changed = true;
            }
        }

        if (string.IsNullOrWhiteSpace(reservation.OwnerSteamId))
        {
            if (!string.IsNullOrWhiteSpace(reservation.OwnerKey) && reservation.OwnerKey.StartsWith("player:", StringComparison.OrdinalIgnoreCase))
            {
                reservation.OwnerSteamId = reservation.OwnerKey.Substring(7);
            }
            else if (!string.IsNullOrWhiteSpace(reservation.OwnerKey) && reservation.OwnerKey.StartsWith("org:", StringComparison.OrdinalIgnoreCase))
            {
                string organisationId = reservation.OwnerKey.Substring(4);
                if (_repository.Current.Organisations.TryGetValue(organisationId, out OrganisationRecord? organisation))
                {
                    reservation.OwnerSteamId = organisation.OwnerSteamId;
                    reservation.OwnerOrganisationId = organisation.OrgId;
                }
            }

            changed = !string.IsNullOrWhiteSpace(reservation.OwnerSteamId);
        }

        if (string.IsNullOrWhiteSpace(reservation.OwnerOrganisationId)
            && !string.IsNullOrWhiteSpace(reservation.OwnerSteamId)
            && _repository.Current.PlayerToOrganisation.TryGetValue(reservation.OwnerSteamId, out string? currentOrganisationId))
        {
            reservation.OwnerOrganisationId = currentOrganisationId ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(reservation.OwnerOrganisationId))
            {
                reservation.OwnerKey = BuildOrganisationOwnerKey(reservation.OwnerOrganisationId);
            }
            changed = true;
        }

        if (!changed)
        {
            return;
        }

        reservation.UpdatedAtUtc = DateTime.UtcNow;
        _repository.MarkDirty();
    }

    private static string ResolveOrganisationId(string ownerKey)
    {
        return ownerKey.StartsWith("org:", StringComparison.OrdinalIgnoreCase)
            ? ownerKey.Substring(4)
            : string.Empty;
    }
}
#endif
