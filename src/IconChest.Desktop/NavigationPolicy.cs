using System.Diagnostics;
using System.Runtime.InteropServices;

namespace IconChest.Desktop;

public sealed class NavigationPolicy
{
    private readonly Uri _apiOrigin;

    public NavigationPolicy(Uri apiOrigin)
    {
        _apiOrigin = apiOrigin ?? throw new ArgumentNullException(nameof(apiOrigin));
    }

    public bool IsAllowedNavigation(Uri uri)
    {
        if (uri == null) return false;

        // Same-origin navigation is allowed
        return string.Equals(uri.Scheme, _apiOrigin.Scheme, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(uri.Host, _apiOrigin.Host, StringComparison.OrdinalIgnoreCase) &&
               uri.Port == _apiOrigin.Port;
    }

    public bool ShouldOpenInExternalBrowser(Uri uri)
    {
        if (uri == null) return false;
        if (IsAllowedNavigation(uri)) return false;

        return uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps;
    }

    public bool IsAllowedBridgeOrigin(string? origin)
    {
        if (string.IsNullOrWhiteSpace(origin)) return false;

        if (Uri.TryCreate(origin, UriKind.Absolute, out var uri))
        {
            return IsAllowedNavigation(uri);
        }

        return false;
    }

    public void OpenExternalBrowser(Uri uri)
    {
        LaunchSystemBrowser(uri);
    }

    public static void LaunchSystemBrowser(Uri uri)
    {
        if (uri == null) return;

        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Process.Start(new ProcessStartInfo(uri.ToString()) { UseShellExecute = true });
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "xdg-open",
                    Arguments = $"\"{uri}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Process.Start("open", uri.ToString());
            }
        }
        catch (Exception ex)
        {
            Trace.TraceWarning("Failed to open external browser for URI {0}: {1}", uri, ex.Message);
        }
    }
}

