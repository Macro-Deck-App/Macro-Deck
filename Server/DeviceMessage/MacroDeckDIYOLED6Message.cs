using Newtonsoft.Json.Linq;
using SuchByte.MacroDeck.Device;
using SuchByte.MacroDeck.Icons;
using SuchByte.MacroDeck.JSON;
using SuchByte.MacroDeck.Model;
using SuchByte.MacroDeck.Profiles;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SuchByte.MacroDeck.Server.DeviceMessage
{
    public class MacroDeckDIYOLED6Message : IDeviceMessage
    {
        public void Connected(MacroDeckClient macroDeckClient)
        {
            SendConfiguration(macroDeckClient);
        }

        public void SendAllButtons(MacroDeckClient macroDeckClient)
        {
            if (macroDeckClient.Profile == null || macroDeckClient.Profile.ProfileTarget != DeviceClass.Macro_Deck_DIY_OLED_6_V1)
            {
                MacroDeckProfile profile = ProfileManager.Profiles.FindAll(x => x.ProfileTarget == DeviceClass.Macro_Deck_DIY_OLED_6_V1).FirstOrDefault();
                if (profile == null)
                {
                    profile = ProfileManager.CreateProfile("Macro Deck DIY OLED v1", DeviceClass.Macro_Deck_DIY_OLED_6_V1);
                    profile.Columns = 3;
                    profile.Rows = 2;
                    ProfileManager.Save();
                }
                DeviceManager.GetMacroDeckDevice(macroDeckClient.ClientId).ProfileId = profile.ProfileId;
                DeviceManager.SaveKnownDevices();
                macroDeckClient.Profile = profile;
                macroDeckClient.Folder = profile.Folders.FirstOrDefault();
            }

            if (macroDeckClient.Folder == null || macroDeckClient.Folder.ActionButtons == null) return;
            var buttons = new ConcurrentBag<JObject>();

            foreach (ActionButton.ActionButton actionButton in macroDeckClient.Folder.ActionButtons)
            {
                string Icon = "";
                string LabelBase64 = "";
                switch (actionButton.State)
                {
                    case false:
                        if (actionButton.IconOff != null)
                        {
                            if (actionButton.IconOff.Length > 0)
                            {
                                Icon = IconManager.GetIcon(IconManager.GetIconPackByName(actionButton.IconOff.Split(".")[0]), long.Parse(actionButton.IconOff.Split(".")[1])).IconHex128_64Base64;
                            }
                        }
                        if (!string.IsNullOrWhiteSpace(actionButton.LabelOff.LabelText))
                        {
                            LabelBase64 = actionButton.LabelOff.LabelHex128_64Base64;
                        }
                        break;
                    case true:
                        if (actionButton.IconOn != null)
                        {
                            if (actionButton.IconOn.Length > 0)
                            {
                                Icon = IconManager.GetIcon(IconManager.GetIconPackByName(actionButton.IconOn.Split(".")[0]), long.Parse(actionButton.IconOn.Split(".")[1])).IconHex128_64Base64;
                            }
                        }
                        if (!string.IsNullOrWhiteSpace(actionButton.LabelOn.LabelText))
                        {
                            LabelBase64 = actionButton.LabelOn.LabelHex128_64Base64;
                        }
                        break;
                }
                JObject actionButtonObject = JObject.FromObject(new
                {
                    actionButton.ButtonId,
                    Icon,
                    actionButton.Position_X,
                    actionButton.Position_Y,
                    LabelBase64,
                });
                buttons.Add(actionButtonObject);
            }
            JObject buttonsObject = JObject.FromObject(new
            {
                Method = JsonMethod.GET_BUTTONS.ToString(),
                Buttons = buttons
            });

            MacroDeckServer.Send(macroDeckClient.SocketConnection, buttonsObject);
        }

        public void SendConfiguration(MacroDeckClient macroDeckClient)
        {
            if (macroDeckClient == null || macroDeckClient.SocketConnection == null || !macroDeckClient.SocketConnection.IsAvailable) return;
            JObject configurationObject = JObject.FromObject(new
            {
                Method = JsonMethod.GET_CONFIG.ToString(),
                Rows = macroDeckClient.Profile.Rows,
                Columns = macroDeckClient.Profile.Columns,
                Brightness = DeviceManager.GetMacroDeckDevice(macroDeckClient.ClientId).Configuration.Brightness,
            });
            MacroDeckServer.Send(macroDeckClient.SocketConnection, configurationObject);
            GC.Collect();
        }

        public void SendIconPacks(MacroDeckClient macroDeckClient)
        {
            // Send nothing because this device does not support icon caching
        }

        public void UpdateButton(MacroDeckClient macroDeckClient, ActionButton.ActionButton actionButton)
        {
            if (macroDeckClient.Folder == null || !macroDeckClient.Folder.ActionButtons.Contains(actionButton)) return;
            string Icon = "";
            string LabelBase64 = "";
            switch (actionButton.State)
            {
                case false:
                    if (actionButton.IconOff != null)
                    {
                        if (actionButton.IconOff.Length > 0)
                        {
                            Icon = IconManager.GetIcon(IconManager.GetIconPackByName(actionButton.IconOff.Split(".")[0]), long.Parse(actionButton.IconOff.Split(".")[1])).IconHex128_64Base64;
                        }
                    }
                    if (!string.IsNullOrWhiteSpace(actionButton.LabelOff.LabelText))
                    {
                        LabelBase64 = actionButton.LabelOff.LabelHex128_64Base64;
                    }
                    break;
                case true:
                    if (actionButton.IconOn != null)
                    {
                        if (actionButton.IconOn.Length > 0)
                        {
                            Icon = IconManager.GetIcon(IconManager.GetIconPackByName(actionButton.IconOn.Split(".")[0]), long.Parse(actionButton.IconOn.Split(".")[1])).IconHex128_64Base64;
                        }
                    }
                    if (!string.IsNullOrWhiteSpace(actionButton.LabelOn.LabelText))
                    {
                        LabelBase64 = actionButton.LabelOn.LabelHex128_64Base64;
                    }
                    break;
            }
            JObject actionButtonObject = JObject.FromObject(new
            {
                actionButton.ButtonId,
                Icon,
                actionButton.Position_X,
                actionButton.Position_Y,
                LabelBase64,
            });

            JObject updateObject = JObject.FromObject(new
            {
                Method = JsonMethod.UPDATE_BUTTON.ToString(),
                Buttons = new List<JObject>
                {
                    actionButtonObject
                },
            });

            MacroDeckServer.Send(macroDeckClient.SocketConnection, updateObject);
        }
    }
}
