using System;
using SuchByte.MacroDeck.Device;
using SuchByte.MacroDeck.GUI.CustomControls;
using SuchByte.MacroDeck.Language;
using SuchByte.MacroDeck.Logging;
using SuchByte.MacroDeck.Server;

namespace SuchByte.MacroDeck.GUI.Dialogs;

public partial class DeviceConfigurator : DialogForm
{
    MacroDeckDevice _macroDeckDevice;

    public DeviceConfigurator(MacroDeckDevice macroDeckDevice)
    {
        _macroDeckDevice = macroDeckDevice;
        InitializeComponent();
        lblBrightness.Text = LanguageManager.Strings.Brightness;
        checkAutoConnect.Text = LanguageManager.Strings.AutoConnect;
        btnOk.Text = LanguageManager.Strings.Ok;
    }

    private void DeviceConfigurator_Load(object sender, EventArgs e)
    {
        brightness.Scroll -= Brightness_Scroll;
        brightness.Value = (int)(_macroDeckDevice.Configuration.Brightness * 10.0);
        brightness.Scroll += Brightness_Scroll;
        checkAutoConnect.CheckedChanged -= CheckAutoConnect_CheckedChanged;
        checkAutoConnect.Checked = _macroDeckDevice.Configuration.AutoConnect;
        checkAutoConnect.CheckedChanged += CheckAutoConnect_CheckedChanged;
        radioKeepAwakeNever.CheckedChanged -= RadioKeepAwakeNever_CheckedChanged;
        radioKeepAwakeConnected.CheckedChanged -= RadioKeepAwakeConnected_CheckedChanged;
        radioKeepAwakeAlways.CheckedChanged -= RadioKeepAwakeAlways_CheckedChanged;
        switch (_macroDeckDevice.Configuration.WakeLockMethod)
        {
            case WakeLockMethod.Never:
                radioKeepAwakeNever.Checked = true;
                break;
            case WakeLockMethod.Connected:
                radioKeepAwakeConnected.Checked = true;
                break;
            case WakeLockMethod.Always:
                radioKeepAwakeAlways.Checked = true;
                break;
        }

        radioKeepAwakeNever.CheckedChanged += RadioKeepAwakeNever_CheckedChanged;
        radioKeepAwakeConnected.CheckedChanged += RadioKeepAwakeConnected_CheckedChanged;
        radioKeepAwakeAlways.CheckedChanged += RadioKeepAwakeAlways_CheckedChanged;

    }

    private void Brightness_Scroll(object sender, EventArgs e)
    {
        if (_macroDeckDevice == null || !_macroDeckDevice.Available) return;
        _macroDeckDevice.Configuration.Brightness = brightness.Value / 10.0f;
        DeviceManager.SaveKnownDevices();
        var macroDeckClient = MacroDeckServer.GetMacroDeckClient(_macroDeckDevice.ClientId);
        macroDeckClient?.DeviceMessage.SendConfiguration(macroDeckClient);
    }

    private void BtnOk_Click(object sender, EventArgs e)
    {
        Close();
    }

    private void CheckAutoConnect_CheckedChanged(object sender, EventArgs e)
    {
        MacroDeckLogger.Trace(GetType(), $"Set auto connect to { checkAutoConnect.Checked }");
        if (_macroDeckDevice == null || !_macroDeckDevice.Available) return;
        _macroDeckDevice.Configuration.AutoConnect = checkAutoConnect.Checked;
        DeviceManager.SaveKnownDevices();
        var macroDeckClient = MacroDeckServer.GetMacroDeckClient(_macroDeckDevice.ClientId);
        macroDeckClient?.DeviceMessage.SendConfiguration(macroDeckClient);
    }

    private void RadioKeepAwakeNever_CheckedChanged(object sender, EventArgs e)
    {
        if (radioKeepAwakeNever.Checked)
        {
            MacroDeckLogger.Trace(GetType(), "Set keepWake to never");
            if (_macroDeckDevice == null || !_macroDeckDevice.Available) return;
            _macroDeckDevice.Configuration.WakeLockMethod = WakeLockMethod.Never;
            DeviceManager.SaveKnownDevices();
            var macroDeckClient = MacroDeckServer.GetMacroDeckClient(_macroDeckDevice.ClientId);
            macroDeckClient?.DeviceMessage.SendConfiguration(macroDeckClient);
        }
    }

    private void RadioKeepAwakeConnected_CheckedChanged(object sender, EventArgs e)
    {
        if (radioKeepAwakeConnected.Checked)
        {
            MacroDeckLogger.Trace(GetType(), "Set keepWake to connected");
            if (_macroDeckDevice == null || !_macroDeckDevice.Available) return;
            _macroDeckDevice.Configuration.WakeLockMethod = WakeLockMethod.Connected;
            DeviceManager.SaveKnownDevices();
            var macroDeckClient = MacroDeckServer.GetMacroDeckClient(_macroDeckDevice.ClientId);
            macroDeckClient?.DeviceMessage.SendConfiguration(macroDeckClient);
        }
    }

    private void RadioKeepAwakeAlways_CheckedChanged(object sender, EventArgs e)
    {
        if (radioKeepAwakeAlways.Checked)
        {
            MacroDeckLogger.Trace(GetType(), "Set keepWake to always");
            if (_macroDeckDevice == null || !_macroDeckDevice.Available) return;
            _macroDeckDevice.Configuration.WakeLockMethod = WakeLockMethod.Always;
            DeviceManager.SaveKnownDevices();
            var macroDeckClient = MacroDeckServer.GetMacroDeckClient(_macroDeckDevice.ClientId);
            macroDeckClient?.DeviceMessage.SendConfiguration(macroDeckClient);
        }
    }
}