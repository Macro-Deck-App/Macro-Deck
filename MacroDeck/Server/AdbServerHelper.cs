using System.IO;
using AdvancedSharpAdbClient;
using AdvancedSharpAdbClient.DeviceCommands;
using AdvancedSharpAdbClient.Models;
using AdvancedSharpAdbClient.Receivers;
using SuchByte.MacroDeck.Logging;
using SuchByte.MacroDeck.Startup;

namespace SuchByte.MacroDeck.Server;

public class AdbServerHelper
{
    private static AdbServer? _adbServer;

    private const string AdbFolderName = "Android Debug Bridge";

    private static readonly string AdbPath = Path.Combine(ApplicationPaths.MainDirectoryPath, AdbFolderName, "adb.exe");
    
    public static async Task Initialize()
    {
        if (!MacroDeck.Configuration.EnableAdbServer)
        {
            return;
        }
        
        if (!File.Exists(AdbPath))
        {
            MacroDeckLogger.Warning(typeof(AdbServerHelper), $"Cannot start adb server at {AdbPath}: File not found");
            return;
        }
        
        MacroDeckLogger.Info(typeof(AdbServerHelper), $"Starting ADB server using {AdbPath}");

        _adbServer = new AdbServer();
        var result = await _adbServer.StartServerAsync(AdbPath, true);
        if (result != StartServerResult.Started)
        {
            MacroDeckLogger.Info(typeof(AdbServerHelper), "Unable to start ADB server");
        }
        
        var monitor = new DeviceMonitor(new AdbSocket(AdbClient.AdbServerEndPoint));
        monitor.DeviceConnected += Monitor_DeviceConnected;
        monitor.DeviceDisconnected += Monitor_DeviceDisconnected;
        await monitor.StartAsync();
    }

    private static async Task RunForDevice(string serial, Func<AdbClient, DeviceData, Task> action)
    {
        var adbClient = await GetAdbClient();
        if (adbClient is null)
        {
            return;
        }
        var device = await GetDevice(adbClient, serial);
        if (!device.HasValue)
        {
            return;
        }

        await action(adbClient, device.Value);
    }

    private static async Task<DeviceData?> GetDevice(AdbClient adbDeviceClient, string serial)
    {
        var devices = await adbDeviceClient.GetDevicesAsync();
        return devices.FirstOrDefault(x => x.Serial.Equals(serial));
    }

    private static async Task<AdbClient?> GetAdbClient()
    {
        var serverEndpoint = GetAdbServerEndpoint();
        if (serverEndpoint is null)
        {
            return null;
        }
        
        var adbClient = new AdbClient();
        await adbClient.ConnectAsync(serverEndpoint);
        return adbClient;
    }

    private static string? GetAdbServerEndpoint()
    {
        var adbServerEndpoint = _adbServer?.EndPoint.ToString();
        if (adbServerEndpoint is not null)
        {
            return adbServerEndpoint;
        }
        
        MacroDeckLogger.Info(typeof(AdbServerHelper), "Endpoint was null");
        return null;

    }

    private static void Monitor_DeviceDisconnected(object sender, DeviceDataEventArgs e)
    {
        MacroDeckLogger.Info(typeof(AdbServerHelper), $"{e.Device.Name} disconnected");
    }

    private static async void Monitor_DeviceConnected(object sender, DeviceDataEventArgs e)
    {
        if (e.Device.Serial.StartsWith("127.0.0.1"))
        {
            return;
        }
        
        MacroDeckLogger.Info(typeof(AdbServerHelper), $"{e.Device.Name} connected");
        await RunForDevice(e.Device.Serial, async (adbDeviceClient, deviceData) =>
        {
            await StartMacroDeckClient(adbDeviceClient, deviceData);
            await StartReverseForward(adbDeviceClient, deviceData);
        });
    }

    private static async Task<bool> IsDeviceOnline(DeviceData device)
    {
        var timeoutTask = Task.Delay(TimeSpan.FromSeconds(20));

        await Task.WhenAny([timeoutTask, WaitForDeviceOnline()]);

        if (device.State == DeviceState.Online)
        {
            return true;
        }
        
        MacroDeckLogger.Info(typeof(AdbServerHelper), $"Device {device.Serial} is still not online - {device.State}");
        return false;

        async Task WaitForDeviceOnline()
        {
            while (device.State != DeviceState.Online)
            {
                await Task.Delay(100);
            }
        }
    }
    
    private static async Task ExitMacroDeckClient(AdbClient adbDeviceClient, DeviceData device)
    {
        var deviceIsOnline = await IsDeviceOnline(device);
        if (!deviceIsOnline)
        {
            return;
        }
        
        await adbDeviceClient.ExecuteRemoteCommandAsync("am force-stop com.suchbyte.macrodeck",
            device,
            new ConsoleOutputReceiver());
        await adbDeviceClient.SendKeyEventAsync(device, "KEYCODE_SLEEP");
    }

    private static async Task StartMacroDeckClient(AdbClient adbDeviceClient, DeviceData device)
    {
        if (!MacroDeck.Configuration.EnableAdbAutoStartApp)
        {
            return;
        }
        
        var deviceIsOnline = await IsDeviceOnline(device);
        if (!deviceIsOnline)
        {
            return;
        }
        
        await adbDeviceClient.SendKeyEventAsync(device, "KEYCODE_WAKEUP");
        await adbDeviceClient.ExecuteRemoteCommandAsync("am start -n com.suchbyte.macrodeck/.MainActivity",
            device,
            new ConsoleOutputReceiver());
    }

    private static async Task StartReverseForward(AdbClient adbDeviceClient, DeviceData device)
    {
        var deviceIsOnline = await IsDeviceOnline(device);
        if (!deviceIsOnline)
        {
            return;
        }
        
        try
        {
            await adbDeviceClient.CreateReverseForwardAsync(
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