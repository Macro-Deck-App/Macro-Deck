using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using SuchByte.MacroDeck.Device;
using SuchByte.MacroDeck.Icons;
using SuchByte.MacroDeck.JSON;
using SuchByte.MacroDeck.Logging;
using SuchByte.MacroDeck.Model;

namespace SuchByte.MacroDeck.Server.DeviceMessage;

public class SoftwareClientMessage : IDeviceMessage
{

    public void Connected(MacroDeckClient macroDeckClient)
    {
        SendConfiguration(macroDeckClient);
        SendAllButtons(macroDeckClient);
        if (macroDeckClient.DeviceType != DeviceManager.GetMacroDeckDevice(macroDeckClient.ClientId).DeviceType)
        {
            DeviceManager.GetMacroDeckDevice(macroDeckClient.ClientId).DeviceType = macroDeckClient.DeviceType;
            DeviceManager.SaveKnownDevices();
        }
    }

    public void SendAllButtons(MacroDeckClient macroDeckClient)
    {
        if (macroDeckClient == null || macroDeckClient.Folder == null || macroDeckClient.Folder.ActionButtons == null) return;
        var buttons = new ConcurrentBag<JObject>();

        Parallel.ForEach(macroDeckClient.Folder.ActionButtons, actionButton =>
        {
            var IconBase64 = "";
            var LabelBase64 = "";
            string BackgroundColorHex;
            if (!actionButton.State)
            {
                if (!string.IsNullOrWhiteSpace(actionButton.IconOff))
                {
                    var icon = IconManager.GetIconByString(actionButton.IconOff);
                    if (icon != null)
                    {
                        IconBase64 = icon.IconBase64;
                    }
                }
                if (!string.IsNullOrWhiteSpace(actionButton.LabelOff.LabelText))
                {
                    LabelBase64 = actionButton.LabelOff.LabelBase64 ?? "";
                }
                BackgroundColorHex = $"#{actionButton.BackColorOff.R:X2}{actionButton.BackColorOff.G:X2}{actionButton.BackColorOff.B:X2}";
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(actionButton.IconOn))
                {
                    var icon = IconManager.GetIconByString(actionButton.IconOn);
                    if (icon != null)
                    {
                        IconBase64 = icon.IconBase64;
                    }
                }
                if (!string.IsNullOrWhiteSpace(actionButton.LabelOn.LabelText))
                {
                    LabelBase64 = actionButton.LabelOn.LabelBase64 ?? "";
                }
                BackgroundColorHex = $"#{actionButton.BackColorOn.R:X2}{actionButton.BackColorOn.G:X2}{actionButton.BackColorOn.B:X2}";
            }

            var actionButtonObject = JObject.FromObject(new
            {
                IconBase64,
                actionButton.Position_X,
                actionButton.Position_Y,
                LabelBase64,
                BackgroundColorHex
            });
            buttons.Add(actionButtonObject);
        });

        var buttonsObject = JObject.FromObject(new
        {
            Method = JsonMethod.GET_BUTTONS.ToString(),
            Buttons = buttons
        });

        MacroDeckServer.Send(macroDeckClient.SocketConnection, buttonsObject);
    }

    public void SendConfiguration(MacroDeckClient macroDeckClient)
    {
        if (macroDeckClient == null || macroDeckClient.SocketConnection == null || !macroDeckClient.SocketConnection.IsAvailable) return;
        var configurationObject = JObject.FromObject(new
        {
            Method = JsonMethod.GET_CONFIG.ToString(),
            macroDeckClient.Profile.Rows,
            macroDeckClient.Profile.Columns,
            macroDeckClient.Profile.ButtonSpacing,
            macroDeckClient.Profile.ButtonRadius,
            macroDeckClient.Profile.ButtonBackground,
            DeviceManager.GetMacroDeckDevice(macroDeckClient.ClientId).Configuration.Brightness,
            DeviceManager.GetMacroDeckDevice(macroDeckClient.ClientId).Configuration.AutoConnect,
            WakeLock = Enum.GetName(typeof(WakeLockMethod), DeviceManager.GetMacroDeckDevice(macroDeckClient.ClientId).Configuration.WakeLockMethod),
            SupportButtonReleaseLongPress = true,
        });
        MacroDeckLogger.Trace(GetType(), configurationObject.ToString());
        MacroDeckServer.Send(macroDeckClient.SocketConnection, configurationObject);
    }

    public void UpdateButton(MacroDeckClient macroDeckClient, ActionButton.ActionButton actionButton)
    {
        if (macroDeckClient.Folder == null || !macroDeckClient.Folder.ActionButtons.Contains(actionButton)) return;
        var IconBase64 = "";
        var LabelBase64 = "";
        string BackgroundColorHex;
        if (!actionButton.State)
        {
            if (!string.IsNullOrWhiteSpace(actionButton.IconOff))
            {
                var icon = IconManager.GetIconByString(actionButton.IconOff);
                if (icon != null)
                {
                    IconBase64 = icon.IconBase64;
                }
            }
            if (!string.IsNullOrWhiteSpace(actionButton.LabelOff.LabelText))
            {
                LabelBase64 = actionButton.LabelOff.LabelBase64 ?? "";
            }
            BackgroundColorHex = $"#{actionButton.BackColorOff.R:X2}{actionButton.BackColorOff.G:X2}{actionButton.BackColorOff.B:X2}";
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(actionButton.IconOn))
            {
                var icon = IconManager.GetIconByString(actionButton.IconOn);
                if (icon != null)
                {
                    IconBase64 = icon.IconBase64;
                }
            }
            if (!string.IsNullOrWhiteSpace(actionButton.LabelOn.LabelText))
            {
                LabelBase64 = actionButton.LabelOn.LabelBase64 ?? "";
            }
            BackgroundColorHex = $"#{actionButton.BackColorOn.R:X2}{actionButton.BackColorOn.G:X2}{actionButton.BackColorOn.B:X2}";
        }

        var actionButtonObject = JObject.FromObject(new
        {
            IconBase64,
            actionButton.Position_X,
            actionButton.Position_Y,
            LabelBase64,
            BackgroundColorHex
        });

        var updateObject = JObject.FromObject(new
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