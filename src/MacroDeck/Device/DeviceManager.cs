using System.IO;
using System.Media;
using Newtonsoft.Json;
using Serilog;
using SuchByte.MacroDeck.GUI.Dialogs;
using SuchByte.MacroDeck.Profiles;
using SuchByte.MacroDeck.Server;
using SuchByte.MacroDeck.StartupConfig;

namespace SuchByte.MacroDeck.Device;

public class DeviceManager
{
    private static readonly ILogger logger = Log.ForContext(typeof(DeviceManager));

    private static List<MacroDeckDevice> _macroDeckDevices = new();
    private static readonly object _saveLock = new();

    public static event EventHandler OnDevicesChange;

    public static void LoadKnownDevices()
    {
        if (!File.Exists(ApplicationPaths.DevicesFilePath))
        {
            return;
        }

        try
        {
            _macroDeckDevices = JsonConvert.DeserializeObject<List<MacroDeckDevice>>(
                File.ReadAllText(ApplicationPaths.DevicesFilePath),
                new JsonSerializerSettings
                {
                    TypeNameHandling = TypeNameHandling.Auto,
                    NullValueHandling = NullValueHandling.Ignore
                })!;
        }
        catch (Exception ex)
        {
            logger.Error(ex, "devices.json is corrupted and will be reset");
            try
            {
                File.Delete(ApplicationPaths.DevicesFilePath);
            }
            catch
            {
            }

            _macroDeckDevices = new List<MacroDeckDevice>();
        }
    }

    public static void SaveKnownDevices()
    {
        var serializer = new JsonSerializer
        {
            TypeNameHandling = TypeNameHandling.Auto,
            NullValueHandling = NullValueHandling.Ignore
        };

        try
        {
            var tempPath = ApplicationPaths.DevicesFilePath + ".tmp";
            lock (_saveLock)
            {
                using (var sw = new StreamWriter(tempPath))
                using (JsonWriter writer = new JsonTextWriter(sw))
                {
                    serializer.Serialize(writer, _macroDeckDevices);
                }

                File.Move(tempPath, ApplicationPaths.DevicesFilePath, true);
            }

            OnDevicesChange?.Invoke(null, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Error while saving known devices");
        }
    }

    public static void AddKnownDevice(MacroDeckDevice macroDeckDevice)
    {
        if (!_macroDeckDevices.Contains(macroDeckDevice) &&
            _macroDeckDevices.Find(x => x.ClientId.Equals(macroDeckDevice.ClientId)) == null)
        {
            _macroDeckDevices.Add(macroDeckDevice);
        }

        SaveKnownDevices();
    }

    public static bool IsKnownDevice(string clientId)
    {
        foreach (var macroDeckDevice in _macroDeckDevices)
        {
            if (macroDeckDevice.ClientId.Equals(clientId))
            {
                return true;
            }
        }

        return false;
    }

    public static MacroDeckDevice? GetMacroDeckDevice(string clientId)
    {
        return _macroDeckDevices.FirstOrDefault(macroDeckDevice => macroDeckDevice.ClientId.Equals(clientId));
    }

    public static MacroDeckDevice? GetMacroDeckDeviceByDisplayName(string displayName)
    {
        return _macroDeckDevices.FirstOrDefault(macroDeckDevice => macroDeckDevice.DisplayName.Equals(displayName));
    }

    public static void SetProfile(MacroDeckDevice macroDeckDevice, MacroDeckProfile macroDeckProfile)
    {
        if (_macroDeckDevices.Contains(macroDeckDevice))
        {
            macroDeckDevice.ProfileId = macroDeckProfile.ProfileId;
            SaveKnownDevices();
        }

        if (macroDeckDevice.Available)
        {
            MacroDeckServer.SetProfile(MacroDeckServer.GetMacroDeckClient(macroDeckDevice.ClientId), macroDeckProfile);
        }
    }

    public static void SetBlocked(MacroDeckDevice macroDeckDevice, bool blocked)
    {
        if (_macroDeckDevices.Contains(macroDeckDevice))
        {
            macroDeckDevice.Blocked = blocked;
            SaveKnownDevices();
        }

        if (!blocked || !macroDeckDevice.Available)
        {
            return;
        }

        var macroDeckClient = MacroDeckServer.GetMacroDeckClient(macroDeckDevice.ClientId);
        if (macroDeckClient is not null)
        {
            Task.Run(async () => await WebSocketHandler.Close(macroDeckClient.SessionId));
        }
    }

    public static void RenameMacroDeckDevice(MacroDeckDevice macroDeckDevice, string displayName)
    {
        if (!_macroDeckDevices.Contains(macroDeckDevice))
        {
            return;
        }

        macroDeckDevice.DisplayName = displayName;
        SaveKnownDevices();
    }

    public static void RemoveKnownDevice(MacroDeckDevice macroDeckDevice)
    {
        if (!_macroDeckDevices.Contains(macroDeckDevice))
        {
            return;
        }

        _macroDeckDevices.Remove(macroDeckDevice);
        SaveKnownDevices();
    }

    public static bool IsDisplayNameAvailable(string displayName)
    {
        return !(_macroDeckDevices.FindAll(macroDeckDevice => macroDeckDevice.DisplayName.Equals(displayName)).Count >
            0);
    }

    public static List<MacroDeckDevice> GetKnownDevices()
    {
        return _macroDeckDevices;
        // TODO: Array
    }

    public static bool RequestConnection(MacroDeckClient macroDeckClient)
    {
        if (MacroDeck.Configuration.AskOnNewConnections)
        {
            if (IsKnownDevice(macroDeckClient.ClientId))
            {
                var macroDeckDevice = GetMacroDeckDevice(macroDeckClient.ClientId);
                if (macroDeckDevice is { Blocked: true })
                {
                    return false;
                }

                macroDeckDevice!.ClientId = macroDeckClient.ClientId;
                macroDeckDevice.DeviceType = macroDeckClient.DeviceType;
                return true;
            }

            Form? mainForm = null;
            var dialogResult = false;
            foreach (Form form in Application.OpenForms)
            {
                if (form.Name.Equals("MainWindow"))
                {
                    mainForm = form;
                }
            }

            if (mainForm is { IsHandleCreated: true, IsDisposed: false })
            {
                mainForm.Invoke(() => { dialogResult = ShowConnectionDialog(macroDeckClient); });
            }
            else
            {
                dialogResult = ShowConnectionDialog(macroDeckClient);
            }

            if (!dialogResult)
            {
                return dialogResult;
            }

            {
                AddKnownDevice(macroDeckClient);
            }
            return dialogResult;
        }

        if (!IsKnownDevice(macroDeckClient.ClientId))
        {
            AddKnownDevice(macroDeckClient);
        }

        return true;
    }

    public static void AddKnownDevice(MacroDeckClient macroDeckClient)
    {
        var macroDeckDevice = new MacroDeckDevice
        {
            ClientId = macroDeckClient.ClientId,
            DisplayName = "Client " + macroDeckClient.ClientId,
            ProfileId = ProfileManager.Profiles.FirstOrDefault()?.ProfileId ?? "0"
        };
        AddKnownDevice(macroDeckDevice);
        macroDeckDevice.ClientId = macroDeckClient.ClientId;
        macroDeckDevice.DeviceType = macroDeckClient.DeviceType;
    }

    private static bool ShowConnectionDialog(MacroDeckClient macroDeckClient)
    {
        SystemSounds.Exclamation.Play();
        using var newConnectionDialog = new NewConnectionDialog(macroDeckClient);
        if (newConnectionDialog.ShowDialog() == DialogResult.Yes)
        {
            return true;
        }

        Task.Run(async () => await WebSocketHandler.Close(macroDeckClient.SessionId));

        if (newConnectionDialog.Blocked)
        {
            var macroDeckDevice = new MacroDeckDevice
            {
                ClientId = macroDeckClient?.ClientId,
                DisplayName = "Client " + macroDeckClient.ClientId,
                Blocked = true
            };
            AddKnownDevice(macroDeckDevice);
        }

        return false;
    }
}
