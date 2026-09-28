using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace OptimaBrowser;

public static class DefaultBrowser
{
    public static void RegisterAsDefault()
    {
        try
        {
            string exePath = Process.GetCurrentProcess().MainModule?.FileName ?? "";
            if (string.IsNullOrEmpty(exePath)) return;
            
            string progId = "OptimaBrowser.Url";
            string appName = "Optima Browser";

            // 1. Register ProgID for HTML and HTTP/HTTPS
            using (var key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{progId}"))
            {
                key.SetValue("", "Optima HTML Document");
                key.SetValue("AppUserModelId", "OptimaBrowser");
                using (var icon = key.CreateSubKey("DefaultIcon"))
                    icon.SetValue("", $"{exePath},0");
                using (var cmd = key.CreateSubKey(@"shell\open\command"))
                    cmd.SetValue("", $"\"{exePath}\" \"%1\"");
            }

            // 2. Register Application Capabilities
            using (var appKey = Registry.CurrentUser.CreateSubKey($@"Software\Clients\StartMenuInternet\{appName}"))
            {
                appKey.SetValue("", appName);
                using (var icon = appKey.CreateSubKey("DefaultIcon"))
                    icon.SetValue("", $"{exePath},0");
                using (var cmd = appKey.CreateSubKey(@"shell\open\command"))
                    cmd.SetValue("", $"\"{exePath}\"");

                using (var caps = appKey.CreateSubKey("Capabilities"))
                {
                    caps.SetValue("ApplicationName", appName);
                    caps.SetValue("ApplicationIcon", $"{exePath},0");
                    caps.SetValue("ApplicationDescription", "Optima Browser - liquid glass design.");
                    
                    using (var url = caps.CreateSubKey("URLAssociations"))
                    {
                        url.SetValue("http", progId);
                        url.SetValue("https", progId);
                    }
                    using (var ext = caps.CreateSubKey("FileAssociations"))
                    {
                        ext.SetValue(".htm", progId);
                        ext.SetValue(".html", progId);
                    }
                }
            }

            // 3. Register in RegisteredApplications
            using (var regApps = Registry.CurrentUser.CreateSubKey(@"Software\RegisteredApplications"))
            {
                regApps.SetValue(appName, $@"Software\Clients\StartMenuInternet\{appName}\Capabilities");
            }

            // Open Settings to let user confirm
            Process.Start(new ProcessStartInfo
            {
                FileName = "ms-settings:defaultapps",
                UseShellExecute = true
            });
        }
        catch { }
    }
}
