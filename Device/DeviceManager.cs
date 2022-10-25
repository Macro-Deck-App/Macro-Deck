using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Media;
using System.Windows.Forms;
using Newtonsoft.Json;
using SuchByte.MacroDeck.GUI.Dialogs;
using SuchByte.MacroDeck.Logging;
using SuchByte.MacroDeck.Profiles;
using SuchByte.MacroDeck.Server;

namespace SuchByte.MacroDeck.Device
{
    public class DeviceManager
    {

        private static List<MacroDeckDevice> _macroDeckDevices = new();

        public static event EventHandler OnDevicesChange;

        public static void LoadKnownDevices()
        {
            if (File.Exists(MacroDeck.DevicesFilePath))
            {
                _macroDeckDevices = JsonConvert.DeserializeObject<List<MacroDeckDevice>>(File.ReadAllText(MacroDeck.DevicesFilePath), new JsonSerializerSettings
                {
                    TypeNameHandling = TypeNameHandling.Auto,
                    NullValueHandling = NullValueHandling.Ignore,
                });
            }
        }

        public static void SaveKnownDevices()
        {
            var serializer = new JsonSerializer
            {
                TypeNameHandling = TypeNameHandling.Auto,
                NullValueHandling = NullValueHandling.Ignore,
            };

            try
            {
                using (var sw = new StreamWriter(MacroDeck.DevicesFilePath))
                using (JsonWriter writer = new JsonTextWriter(sw))
                {
                    serializer.Serialize(writer, _macroDeckDevices);
                }

                OnDevicesChange?.Invoke(null, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                MacroDeckLogger.Error("Error while saving known devices: " + ex.Message);
            }
        }

        public static void AddKnownDevice(MacroDeckDevice macroDeckDevice)
        {
            if (!_macroDeckDevices.Contains(macroDeckDevice) && _macroDeckDevices.Find(x => x.ClientId.Equals(macroDeckDevice.ClientId)) == null)
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

        public static MacroDeckDevice GetMacroDeckDevice(string clientId)
        {
            foreach (var macroDeckDevice in _macroDeckDevices)
            {
                if (macroDeckDevice.ClientId.Equals(clientId))
                {
                    return macroDeckDevice;
                }
            }
            return null;
        }

        public static MacroDeckDevice GetMacroDeckDeviceByDisplayName(string displayName)
        {
            foreach (var macroDeckDevice in _macroDeckDevices)
            {
                if (macroDeckDevice.DisplayName.Equals(displayName))
                {
                    return macroDeckDevice;
                }
            }
            return null;
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
            if (blocked && macroDeckDevice.Available)
            {
                MacroDeckServer.GetMacroDeckClient(macroDeckDevice.ClientId).SocketConnection.Close();
            }
        }

        public static void RenameMacroDeckDevice(MacroDeckDevice macroDeckDevice, string displayName)
        {
            if (_macroDeckDevices.Contains(macroDeckDevice))
            {
                macroDeckDevice.DisplayName = displayName;
                SaveKnownDevices();
            }
        }

        public static void RemoveKnownDevice(MacroDeckDevice macroDeckDevice)
        {
            if (_macroDeckDevices.Contains(macroDeckDevice))
            {
                _macroDeckDevices.Remove(macroDeckDevice);
                SaveKnownDevices();
            }
        }

        public static bool IsDisplayNameAvailable(string displayName)
        {
            return !(_macroDeckDevices.FindAll(macroDeckDevice => macroDeckDevice.DisplayName.Equals(displayName)).Count > 0);
        }

        public static List<MacroDeckDevice> GetKnownDevices()
        {
            return _macroDeckDevices;
        }

        public static bool RequestConnection(MacroDeckClient macroDeckClient)
        {
            if (MacroDeck.Configuration.AskOnNewConnections)
            {
                if (IsKnownDevice(macroDeckClient.ClientId))
                {
                    var macroDeckDevice = GetMacroDeckDevice(macroDeckClient.ClientId);
                    if (macroDeckDevice.Blocked)
                    {
                        return false;
                    }

                    macroDeckDevice.ClientId = macroDeckClient.ClientId;
                    macroDeckDevice.DeviceType = macroDeckClient.DeviceType;
                    return true;
                }

                Form mainForm = null;
                var dialogResult = false;
                foreach (Form form in Application.OpenForms)
                {
                    if (form.Name.Equals("MainWindow"))
                    {
                        mainForm = form;
                    }
                }
                if (mainForm != null && mainForm.IsHandleCreated && !mainForm.IsDisposed)
                {
                    mainForm.Invoke(() =>
                    {
                        dialogResult = ShowConnectionDialog(macroDeckClient);
                    });
                }
                else
                {
                    dialogResult = ShowConnectionDialog(macroDeckClient);
                }
                if (dialogResult)
                {
                    var macroDeckDevice = new MacroDeckDevice
                    {
                        ClientId = macroDeckClient.ClientId,
                        DisplayName = "Client " + macroDeckClient.ClientId,
                        ProfileId = ProfileManager.Profiles.FirstOrDefault().ProfileId,
                    };
                    AddKnownDevice(macroDeckDevice);
                    macroDeckDevice.ClientId = macroDeckClient.ClientId;
                    macroDeckDevice.DeviceType = macroDeckClient.DeviceType;
                }
                return dialogResult;
            }

            if (!IsKnownDevice(macroDeckClient.ClientId))
            {
                var macroDeckDevice = new MacroDeckDevice
                {
                    ClientId = macroDeckClient.ClientId,
                    DisplayName = "Client " + macroDeckClient.ClientId,
                    DeviceType = macroDeckClient.DeviceType
                };
                AddKnownDevice(macroDeckDevice);
            }
            return true;
        }

        private static bool ShowConnectionDialog(MacroDeckClient macroDeckClient)
        {
            SystemSounds.Exclamation.Play();
            using var newConnectionDialog = new NewConnectionDialog(macroDeckClient);
            if (newConnectionDialog.ShowDialog() == DialogResult.Yes)
            {
                return true;
            }

            macroDeckClient?.SocketConnection?.Close();
            if (newConnectionDialog.Blocked)
            {
                var macroDeckDevice = new MacroDeckDevice
                {
                    ClientId = macroDeckClient.ClientId,
                    DisplayName = "Client " + macroDeckClient.ClientId,
                    Blocked = true
                };
                AddKnownDevice(macroDeckDevice);
            }
            return false;
        }

    }
}
