#if SERVER
using System.IO;
using DedicatedServerMod.Organisations.Utils;
using Newtonsoft.Json;
#if !IL2CPP
using ScheduleOne.DevUtilities;
using ScheduleOne.Persistence;
using ScheduleOne.Persistence.Loaders;
#endif

namespace DedicatedServerMod.Organisations.Persistence;

internal sealed class FileOrganisationRepository :
#if IL2CPP
    IOrganisationRepository
#else
    IBaseSaveable, ISaveable, IOrganisationRepository
#endif
{
    private readonly OrganisationLogger _logger;
#if !IL2CPP
    private readonly Loader _loader;
#endif
    private readonly OrganisationSaveDataMigrator _migrator;
    private OrganisationSaveData _current = new OrganisationSaveData();

    public FileOrganisationRepository(OrganisationLogger logger)
    {
        _logger = logger;
        _migrator = new OrganisationSaveDataMigrator(logger);
#if !IL2CPP
        _loader = new OrganisationSaveDataLoader(logger, Replace);
#endif
    }

    public OrganisationSaveData Current => _current;

#if !IL2CPP
    public string SaveFolderName => Constants.SaveFileName;

    public string SaveFileName => Constants.SaveFileName;

    public Loader Loader => _loader;

    public bool ShouldSaveUnderFolder => false;

    public System.Collections.Generic.List<string> LocalExtraFiles { get; set; } = new System.Collections.Generic.List<string>();

    public System.Collections.Generic.List<string> LocalExtraFolders { get; set; } = new System.Collections.Generic.List<string>();

    public bool HasChanged { get; set; } = true;

    public int LoadOrder => 10;

    public void InitializeSaveable()
    {
        try
        {
            Singleton<SaveManager>.Instance?.RegisterSaveable(this);
            _logger.Info("Registered organisation repository with SaveManager.");
        }
        catch (System.Exception ex)
        {
            _logger.Error("Failed to register organisation repository", ex);
        }
    }
#else
    public bool HasChanged { get; private set; } = true;
#endif

    public string GetSaveString()
    {
        HasChanged = false;
        return JsonConvert.SerializeObject(_current, Formatting.Indented);
    }

    public void Replace(OrganisationSaveData data)
    {
        _current = _migrator.Migrate(data, out bool changed);
        HasChanged = changed;
    }

    public void MarkDirty()
    {
        HasChanged = true;
    }

#if IL2CPP
    public void LoadFromSaveFolderPath(string saveFolderPath)
    {
        string savePath = GetSavePath(saveFolderPath);
        if (!File.Exists(savePath))
        {
            Replace(new OrganisationSaveData());
            _logger.Info("No organisation save data found; starting fresh.");
            return;
        }

        string contents = File.ReadAllText(savePath);
        try
        {
            OrganisationSaveData? data = JsonConvert.DeserializeObject<OrganisationSaveData>(contents);
            Replace(data ?? new OrganisationSaveData());
            _logger.Info("Loaded organisation save data.");
        }
        catch (System.Exception ex)
        {
            TryBackupUnreadableFile(savePath, contents);
            Replace(new OrganisationSaveData());
            _logger.Error("Failed to deserialize organisation save data", ex);
        }
    }

    public void SaveToSaveFolderPath(string saveFolderPath)
    {
        if (string.IsNullOrWhiteSpace(saveFolderPath))
        {
            _logger.Warning("Skipped organisation save because the save folder path was unavailable.");
            return;
        }

        Directory.CreateDirectory(saveFolderPath);
        string savePath = GetSavePath(saveFolderPath);
        File.WriteAllText(savePath, GetSaveString());
        _logger.Info("Saved organisation data.");
    }

    private static string GetSavePath(string saveFolderPath)
    {
        return Path.Combine(saveFolderPath, Constants.SaveFileName);
    }

    private void TryBackupUnreadableFile(string savePath, string contents)
    {
        try
        {
            string directory = Path.GetDirectoryName(savePath) ?? string.Empty;
            string fileName = Path.GetFileName(savePath);
            string timestamp = System.DateTime.UtcNow.ToString("yyyyMMddHHmmss");
            string backupPath = Path.Combine(directory, $"{fileName}.corrupt-{timestamp}.bak");
            File.WriteAllText(backupPath, contents ?? string.Empty);
            _logger.Warning($"Backed up unreadable organisation save data to {backupPath}.");
        }
        catch (System.Exception backupEx)
        {
            _logger.Warning($"Failed to back up unreadable organisation save data: {backupEx.Message}");
        }
    }
#endif
}
#endif
