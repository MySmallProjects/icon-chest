using System.Diagnostics;
using System.Runtime.InteropServices;

namespace IconChest.Core.Native;

public interface INativeLauncher
{
    void OpenFile(string path);
}

public sealed class ProcessNativeLauncher : INativeLauncher
{
    public void OpenFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("File path cannot be null or empty.", nameof(path));

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var psi = new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            };
            Process.Start(psi);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            var psi = new ProcessStartInfo
            {
                FileName = "open",
                ArgumentList = { path },
                UseShellExecute = false
            };
            Process.Start(psi);
        }
        else
        {
            // Linux and Unix
            var psi = new ProcessStartInfo
            {
                FileName = "xdg-open",
                ArgumentList = { path },
                UseShellExecute = false
            };
            Process.Start(psi);
        }
    }
}

