#if CLIENT && ORG_UI_SMOKE
using System;

namespace DedicatedServerMod.Organisations.Client.Testing;

internal sealed class OrganisationPhoneAppUiSmokeOptions
{
    public bool Enabled { get; private set; }

    public string OutputDirectory { get; private set; } = string.Empty;

    public int CaptureDelaySeconds { get; private set; } = 3;

    public static OrganisationPhoneAppUiSmokeOptions Parse(string[] args)
    {
        var options = new OrganisationPhoneAppUiSmokeOptions();
        args ??= Array.Empty<string>();

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i] ?? string.Empty;
            if (string.Equals(arg, "--org-ui-smoke", StringComparison.OrdinalIgnoreCase))
            {
                options.Enabled = true;
                continue;
            }

            if (string.Equals(arg, "--org-ui-smoke-output-dir", StringComparison.OrdinalIgnoreCase)
                && TryReadNext(args, i, out string outputDirectory))
            {
                options.OutputDirectory = outputDirectory.Trim();
                i++;
                continue;
            }

            if (string.Equals(arg, "--org-ui-smoke-capture-delay-seconds", StringComparison.OrdinalIgnoreCase)
                && TryReadNext(args, i, out string delay)
                && int.TryParse(delay.Trim(), out int parsedDelay)
                && parsedDelay >= 0)
            {
                options.CaptureDelaySeconds = parsedDelay;
                i++;
            }
        }

        return options;
    }

    private static bool TryReadNext(string[] args, int index, out string value)
    {
        value = string.Empty;
        int nextIndex = index + 1;
        if (nextIndex >= args.Length)
        {
            return false;
        }

        string candidate = args[nextIndex] ?? string.Empty;
        if (candidate.StartsWith("--", StringComparison.Ordinal))
        {
            return false;
        }

        value = candidate;
        return !string.IsNullOrWhiteSpace(value);
    }
}
#endif
