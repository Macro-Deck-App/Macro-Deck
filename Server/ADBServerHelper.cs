using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using SharpAdbClient;
using SuchByte.MacroDeck.Enums;
using SuchByte.MacroDeck.Logging;

namespace SuchByte.MacroDeck.Server
{
    public class AdbDeviceConnectionStateChangedEventArgs : EventArgs
    {
        public DeviceData Device { get; set; }

        public AdbDeviceConnectionState ConnectionState { get; set; }
    }


    public class ADBServerHelper
    {

        private static AdbServer _adbServer;

        private static AdbClient _adbClient;

        private static string _adbFolderName = "Android Debug Bridge";

        private static string _adbPath = Path.Combine(MacroDeck.ApplicationPaths.MainDirectoryPath, _adbFolderName, "adb.exe");

        public static EventHandler<AdbDeviceConnectionStateChangedEventArgs> OnDeviceConnectionStateChanged;

        public static bool ServerRunning => _adbServer != null && _adbClient != null && _adbServer.GetStatus().IsRunning;

        public static List<DeviceData> Devices => _adbClient.GetDevices();

        public static void Initialize()
        {
            if (!File.Exists(_adbPath))
            {
                MacroDeckLogger.Warning(typeof(ADBServerHelper), $"Cannot start adb server at {_adbPath}: File not found");
                return;
            }
            MacroDeckLogger.Info(typeof(ADBServerHelper), $"Starting ADB server using {_adbPath}");
            _adbServer = new AdbServer();
            var result = _adbServer.StartServer(_adbPath, true);
            if (result != StartServerResult.Started)
            {
                MacroDeckLogger.Info(typeof(ADBServerHelper), "Unable to start ADB server");
            }

            _adbClient = new AdbClient();

            Task.Run(() =>
            {
                var monitor = new DeviceMonitor(new AdbSocket(new IPEndPoint(IPAddress.Loopback, AdbClient.AdbServerPort)));
                monitor.DeviceConnected += Monitor_DeviceConnected;
                monitor.DeviceDisconnected += Monitor_DeviceDisconnected;
                monitor.Start();
            });

            
            foreach (var device in _adbClient.GetDevices())
            {
                MacroDeckLogger.Info(typeof(ADBServerHelper), $"Found {device.Name}");
                StartReverseForward(device);
            }
        }

        private static void Monitor_DeviceDisconnected(object sender, DeviceDataEventArgs e)
        {
            MacroDeckLogger.Info(typeof(ADBServerHelper), $"{e.Device.Name} disconnected");
            OnDeviceConnectionStateChanged?.Invoke(null, new AdbDeviceConnectionStateChangedEventArgs
            {
                Device = e.Device,
                ConnectionState = AdbDeviceConnectionState.DISCONNECTED
            });
        }

        private static void Monitor_DeviceConnected(object sender, DeviceDataEventArgs e)
        {
            MacroDeckLogger.Info(typeof(ADBServerHelper), $"{e.Device.Name} connected");
            OnDeviceConnectionStateChanged?.Invoke(null, new AdbDeviceConnectionStateChangedEventArgs
            {
                Device = e.Device,
                ConnectionState = AdbDeviceConnectionState.CONNECTED
            });
            StartReverseForward(e.Device);
        }

        private static void StartReverseForward(DeviceData device)
        {
            try
            {
                _adbClient.CreateReverseForward(device, $"tcp:{MacroDeck.Configuration.Host_Port}", $"tcp:{MacroDeck.Configuration.Host_Port}", true);
            } catch (Exception ex)
            {
                MacroDeckLogger.Warning(typeof(ADBServerHelper), $"Unable to start reverse forward on {device?.Name}: {ex.Message}");
            }
            MacroDeckLogger.Info(typeof(ADBServerHelper), $"Started reverse forward on {device?.Name}");
        }

    }
}
