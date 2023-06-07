using CefSharp.WinForms;
using CefSharp;
using SuchByte.MacroDeck.Logging;
using System.IO;

namespace SuchByte.MacroDeck.Startup;

internal class CefSetup
{
    public static void Initialize()
    {
        try
        {
            var settings = new CefSettings();
            settings.CefCommandLineArgs.Add("force-device-scale-factor", "1");
            settings.CefCommandLineArgs.Add("disable-gpu-vsync", "1");
            settings.CefCommandLineArgs.Add("disable-gpu", "1");
            settings.CachePath = Path.Combine(ApplicationPaths.UserDirectoryPath, "CefSharp", "Cache");
            Cef.Initialize(settings);
        }
        catch (Exception ex)
        {
            MacroDeckLogger.Error(typeof(MacroDeck), $"Could not initialize Chromium Embedded Framework: {ex}" + Environment.NewLine + "Make sure, Microsoft Visual C++ Redistributable is installed on your computer. You can download it here: https://aka.ms/vs/17/release/vc_redist.x64.exe");
        }
    }
}