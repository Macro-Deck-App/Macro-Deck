using Newtonsoft.Json;
using SuchByte.MacroDeck.Profiles;
using SuchByte.MacroDeck.Server;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SuchByte.MacroDeck.Device
{
    public class DeviceManager
    {

        private static List<MacroDeckDevice> _macroDeckDevices = new List<MacroDeckDevice>();

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
            JsonSerializer serializer = new JsonSerializer
            {
                TypeNameHandling = TypeNameHandling.Auto,
                NullValueHandling = NullValueHandling.Ignore,
            };

            try
            {
                using (StreamWriter sw = new StreamWriter(MacroDeck.DevicesFilePath))
                using (JsonWriter writer = new JsonTextWriter(sw))
                {
                    serializer.Serialize(writer, _macroDeckDevices);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }

        public static void AddKnownDevice(MacroDeckDevice macroDeckDevice)
        {
            if (!_macroDeckDevices.Contains(macroDeckDevice))
            {
                _macroDeckDevices.Add(macroDeckDevice);
            }
            SaveKnownDevices();

            if (OnDevicesChange != null)
            {
                OnDevicesChange(macroDeckDevice, EventArgs.Empty);
            }
        }

        public static bool IsKnownDevice(string clientId)
        {
            foreach (MacroDeckDevice macroDeckDevice in _macroDeckDevices)
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
            foreach (MacroDeckDevice macroDeckDevice in _macroDeckDevices)
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
            foreach (MacroDeckDevice macroDeckDevice in _macroDeckDevices)
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
            if (OnDevicesChange != null)
            {
                OnDevicesChange(macroDeckDevice, EventArgs.Empty);
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
            if (OnDevicesChange != null)
            {
                OnDevicesChange(macroDeckDevice, EventArgs.Empty);
            }
        }

        public static void RenameMacroDeckDevice(MacroDeckDevice macroDeckDevice, string displayName)
        {
            if (_macroDeckDevices.Contains(macroDeckDevice))
            {
                macroDeckDevice.DisplayName = displayName;
                SaveKnownDevices();
            }
            if (OnDevicesChange != null)
            {
                OnDevicesChange(macroDeckDevice, EventArgs.Empty);
            }
        }

        public static void RemoveKnownDevice(MacroDeckDevice macroDeckDevice)
        {
            if (_macroDeckDevices.Contains(macroDeckDevice))
            {
                _macroDeckDevices.Remove(macroDeckDevice);
                SaveKnownDevices();
            }
            if (OnDevicesChange != null)
            {
                OnDevicesChange(macroDeckDevice, EventArgs.Empty);
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

    }
}
