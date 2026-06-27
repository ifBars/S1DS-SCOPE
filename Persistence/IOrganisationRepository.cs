#if SERVER
namespace DedicatedServerMod.Organisations.Persistence;

internal interface IOrganisationRepository
{
    OrganisationSaveData Current { get; }

    void Replace(OrganisationSaveData data);

    void MarkDirty();
}
#endif
