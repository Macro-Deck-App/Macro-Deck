using System.IO;
using AdvancedSharpAdbClient;
using AdvancedSharpAdbClient.DeviceCommands;
using AdvancedSharpAdbClient.Models;
using AdvancedSharpAdbClient.Receivers;
using Serilog;
using SuchByte.MacroDeck.StartupConfig;

namespace SuchByte.MacroDeck.Server;

public class AdbServerHelper
{
    private static readonly ILogger Logger = Log.ForContext(typeof(AdbServerHelper));

    private static AdbServer? _adbServer;

    private const string AdbFolderName = "Android Debug Bridge";

    private static readonly string AdbPath = Path.Combine(ApplicationPaths.MainDirectoryPath, AdbFolderName, "adb.exe");

    public static async Task Stop()
    {
        if (_adbServer is null)
        {
            return;
        }

        await _adbServer.StopServerAsync();
    }

    public static async Task Initialize()
    {
        if (!MacroDeck.Configuration.EnableAdbServer)
        {
            return;
        }

        if (!File.Exists(AdbPath))
        {
            Logger.Warning("Cannot start adb server at {AdbPath}: File not found", AdbPath);
            return;
        }

        Logger.Information("Starting ADB server using {AdbPath}", AdbPath);

        _adbServer = new AdbServer();
        var result = await _adbServer.StartServerAsync(AdbPath, false);
        if (result != StartServerResult.Started && result != StartServerResult.AlreadyRunning)
        {
            Logger.Information("Unable to start ADB server");
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
        if (device is null)
        {
            return;
        }

        await action(adbClient, device);
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

        Logger.Information("Endpoint was null");
        return null;
    }

    private static void Monitor_DeviceDisconnected(object sender, DeviceDataEventArgs e)
    {
        Logger.Information("{DeviceName} disconnected", e.Device.Name);
    }

    private static async void Monitor_DeviceConnected(object sender, DeviceDataEventArgs e)
    {
        if (e.Device.Serial.StartsWith("127.0.0.1"))
        {
            return;
        }

        Logger.Information("{DeviceName} connected", e.Device.Name);
        await RunForDevice(e.Device.Serial,
            async (adbDeviceClient, deviceData) =>
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

        Logger.Information("Device {DeviceSerial} is still not online - {DeviceState}", device.Serial, device.State);
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

        try
        {
            await adbDeviceClient.ExecuteRemoteCommandAsync("am force-stop com.suchbyte.macrodeck",
                device,
                new ConsoleOutputReceiver());
            await adbDeviceClient.SendKeyEventAsync(device, "KEYCODE_SLEEP");
        }
        catch
        {
            // ignore
        }
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

        try
        {
            await adbDeviceClient.SendKeyEventAsync(device, "KEYCODE_WAKEUP");
            await adbDeviceClient.ExecuteRemoteCommandAsync("am start -n com.suchbyte.macrodeck/.MainActivity",
                device,
                new ConsoleOutputReceiver());
        }
        catch
        {
            // ignore
        }
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
            await adbDeviceClient.CreateReverseForwardAsync(device,
                $"tcp:{MacroDeck.Configuration.HostPort}",
                $"tcp:{MacroDeck.Configuration.HostPort}",
                true);

            Logger.Information("Started reverse forward on {DeviceName}", device.Name);
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Unable to start reverse forward on {DeviceName}", device.Name);
        }
    }
}
