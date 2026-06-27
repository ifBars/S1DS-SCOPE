#if SERVER && !IL2CPP
using System;
using System.IO;
using DedicatedServerMod.Organisations.Utils;
using Newtonsoft.Json;
using ScheduleOne.Persistence.Loaders;

namespace DedicatedServerMod.Organisations.Persistence;

internal sealed class OrganisationSaveDataLoader : Loader
{
    private readonly OrganisationLogger _logger;
    private readonly System.Action<OrganisationSaveData> _applyLoadedData;

    public OrganisationSaveDataLoader(OrganisationLogger logger, System.Action<OrganisationSaveData> applyLoadedData)
    {
        _logger = logger;
        _applyLoadedData = applyLoadedData;
    }

    public override void Load(string mainPath)
    {
        if (!TryLoadFile(mainPath, out string contents))
        {
            _applyLoadedData(new OrganisationSaveData());
            _logger.Info("No organisation save data found; starting fresh.");
            return;
        }

        try
        {
            OrganisationSaveData? data = JsonConvert.DeserializeObject<OrganisationSaveData>(contents);
            _applyLoadedData(data ?? new OrganisationSaveData());
            _logger.Info("Loaded organisation save data.");
        }
        catch (System.Exception ex)
        {
            TryBackupUnreadableFile(mainPath, contents);
            _applyLoadedData(new OrganisationSaveData());
            _logger.Error("Failed to deserialize organisation save data", ex);
        }
    }

    private void TryBackupUnreadableFile(string mainPath, string contents)
    {
        try
        {
            string directory = Path.GetDirectoryName(mainPath) ?? string.Empty;
            string fileName = Path.GetFileName(mainPath);
            string timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
            string backupPath = Path.Combine(directory, $"{fileName}.corrupt-{timestamp}.bak");
            File.WriteAllText(backupPath, contents ?? string.Empty);
            _logger.Warning($"Backed up unreadable organisation save data to {backupPath}.");
        }
        catch (System.Exception backupEx)
        {
            _logger.Warning($"Failed to back up unreadable organisation save data: {backupEx.Message}");
        }
    }
}
#endif
