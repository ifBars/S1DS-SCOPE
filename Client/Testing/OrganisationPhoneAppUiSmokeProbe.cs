#if CLIENT && ORG_UI_SMOKE
using System;
using System.IO;
using DedicatedServerMod.Organisations.Client.UI;
using DedicatedServerMod.Organisations.Utils;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DedicatedServerMod.Organisations.Client.Testing;

internal sealed class OrganisationPhoneAppUiSmokeProbe
{
    private readonly OrganisationPhoneAppUiSmokeOptions _options;
    private readonly OrganisationLogger _logger;
    private readonly Action<bool> _openOrganisationHub;
    private bool _openRequested;
    private bool _captured;
    private DateTime _captureDueAtUtc;

    public OrganisationPhoneAppUiSmokeProbe(
        OrganisationPhoneAppUiSmokeOptions options,
        OrganisationLogger logger,
        Action<bool> openOrganisationHub)
    {
        _options = options ?? new OrganisationPhoneAppUiSmokeOptions();
        _logger = logger;
        _openOrganisationHub = openOrganisationHub;

        if (_options.Enabled)
        {
            _logger.Info($"[OrgUiSmoke] Enabled. CaptureDelaySeconds={_options.CaptureDelaySeconds}, OutputDirectory='{_options.OutputDirectory}'.");
        }
    }

    public void Tick(bool hasSnapshot)
    {
        if (!_options.Enabled || _captured || !hasSnapshot)
        {
            return;
        }

        OrganisationsPhoneApp? app = OrganisationsPhoneApp.Instance;
        if (app == null)
        {
            return;
        }

        if (!_openRequested)
        {
            _openRequested = true;
            _captureDueAtUtc = DateTime.UtcNow + TimeSpan.FromSeconds(_options.CaptureDelaySeconds);
            _logger.Info("[OrgUiSmoke] Opening organisation phone app.");
            _openOrganisationHub(false);
            return;
        }

        if (DateTime.UtcNow < _captureDueAtUtc)
        {
            return;
        }

        _captured = true;
        app.RefreshUI();
        string layout = app.DescribeUiForSmoke();
        _logger.Info($"[OrgUiSmoke] Layout {layout}");

        string screenshotPath = CaptureScreenshot();
        if (!string.IsNullOrWhiteSpace(screenshotPath))
        {
            _logger.Info($"[OrgUiSmoke] Screenshot={screenshotPath}");
        }

        _logger.Info("[OrgUiSmoke] PASS phone app opened and layout snapshot captured.");
    }

    private string CaptureScreenshot()
    {
        if (string.IsNullOrWhiteSpace(_options.OutputDirectory))
        {
            return string.Empty;
        }

        try
        {
            Directory.CreateDirectory(_options.OutputDirectory);
            string path = Path.Combine(_options.OutputDirectory, $"organisations-phone-{DateTime.UtcNow:yyyyMMdd-HHmmss}.png");
            Texture2D texture = ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes(path, ImageConversion.EncodeToPNG(texture));
            Object.Destroy(texture);
            return path;
        }
        catch (Exception ex)
        {
            _logger.Warning($"[OrgUiSmoke] Screenshot capture failed: {ex.Message}");
            return string.Empty;
        }
    }
}
#endif
