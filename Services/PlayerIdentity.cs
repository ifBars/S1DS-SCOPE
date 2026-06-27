#if SERVER
namespace DedicatedServerMod.Organisations.Services;

internal sealed class PlayerIdentity
{
    public string SteamId { get; set; } = string.Empty;
    public string PlayerName { get; set; } = string.Empty;
}
#endif
