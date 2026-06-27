#if CLIENT
using DedicatedServerMod.Organisations.Contracts;

namespace DedicatedServerMod.Organisations.Client;

internal sealed class OrganisationClientState
{
    public OrganisationSnapshotDto Snapshot { get; private set; } = new OrganisationSnapshotDto();

    public void Replace(OrganisationSnapshotDto snapshot)
    {
        Snapshot = snapshot ?? new OrganisationSnapshotDto();
    }
}
#endif
