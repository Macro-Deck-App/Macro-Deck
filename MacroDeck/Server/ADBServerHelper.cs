using System.Diagnostics;
using System.IO;
using System.Net;
using AdvancedSharpAdbClient;
using AdvancedSharpAdbClient.Models;
using SuchByte.MacroDeck.Enums;
using SuchByte.MacroDeck.Logging;
using SuchByte.MacroDeck.Startup;

namespace SuchByte.MacroDeck.Server;

public class AdbDeviceConnectionStateChangedEventArgs : EventArgs
{
    public DeviceData Device { get; set; }

    public AdbDeviceConnectionState ConnectionState { get; set; }
}

public class AdbServerHelper
{
    private static AdbServer? _adbServer;

    private static AdbClient? _adbClient;

    private const string AdbFolderName = "Android Debug Bridge";

    private static readonly string AdbPath = Path.Combine(ApplicationPaths.MainDirectoryPath, AdbFolderName, "adb.exe");
    
    public static void Kill()
    {
        try
        {
            _adbClient?.KillAdb();
            var processes = Process.GetProcessesByName("adb").ToArray();
            foreach (var process in processes)
            {
                process.Kill();
            }
        }
        catch (Exception ex)
        {
            MacroDeckLogger.Warning($"Failed to kill adb\n{ex}");
        }
    }

    public static void Initialize()
    {
        if (!File.Exists(AdbPath))
        {
            MacroDeckLogger.Warning(typeof(AdbServerHelper), $"Cannot start adb server at {AdbPath}: File not found");
            return;
        }
        
        MacroDeckLogger.Info(typeof(AdbServerHelper), $"Starting ADB server using {AdbPath}");

        _adbServer = new AdbServer();
        var result = _adbServer.StartServer(AdbPath, true);
        if (result != StartServerResult.Started)
        {
            MacroDeckLogger.Info(typeof(AdbServerHelper), "Unable to start ADB server");
        }

        var adbServerEndpoint = _adbServer.EndPoint.ToString();
        if (adbServerEndpoint is null)
        {
            MacroDeckLogger.Info(typeof(AdbServerHelper), "Endpoint was null");
            return;
        }
        
        _adbClient = new AdbClient();
        _adbClient.Connect(adbServerEndpoint);
        
        foreach (var device in _adbClient.GetDevices())
        {
            MacroDeckLogger.Info(typeof(AdbServerHelper), $"Found {device.Name}");
            StartReverseForward(device);
        }
        
        var monitor = new DeviceMonitor(new AdbSocket(AdbClient.AdbServerEndPoint));
        monitor.DeviceConnected += Monitor_DeviceConnected;
        monitor.DeviceDisconnected += Monitor_DeviceDisconnected;
        Task.Run(async () => await monitor.StartAsync());
    }

    private static void Monitor_DeviceDisconnected(object sender, DeviceDataEventArgs e)
    {
        MacroDeckLogger.Info(typeof(AdbServerHelper), $"{e.Device.Name} disconnected");
    }

    private static void Monitor_DeviceConnected(object sender, DeviceDataEventArgs e)
    {
        MacroDeckLogger.Info(typeof(AdbServerHelper), $"{e.Device.Name} connected");
        StartReverseForward(e.Device);
    }

    private static void StartReverseForward(DeviceData device)
    {
        try
        {
            _adbClient?.CreateReverseForward(
                device,
                $"tcp:{MacroDeck.Configuration.HostPort}",
                $"tcp:{MacroDeck.Configuration.HostPort}",
                true);
            
            MacroDeckLogger.Info(typeof(AdbServerHelper), $"Started reverse forward on {device.Name}");
        } catch (Exception ex)
        {
            MacroDeckLogger.Warning(typeof(AdbServerHelper),
                $"Unable to start reverse forward on {device.Name}: {ex.Message}");
        }
    }

}