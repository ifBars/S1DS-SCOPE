using MelonLoader;

namespace DedicatedServerMod.Organisations.Utils;

internal sealed class OrganisationLogger
{
    private readonly MelonLogger.Instance _logger;

    public OrganisationLogger(MelonLogger.Instance logger)
    {
        _logger = logger;
    }

    public void Info(string message)
    {
        _logger.Msg($"[{Constants.ModName}] {message}");
    }

    public void Warning(string message)
    {
        _logger.Warning($"[{Constants.ModName}] {message}");
    }

    public void Error(string message)
    {
        _logger.Error($"[{Constants.ModName}] {message}");
    }

    public void Error(string message, System.Exception exception)
    {
        _logger.Error($"[{Constants.ModName}] {message}: {exception}");
    }
}
