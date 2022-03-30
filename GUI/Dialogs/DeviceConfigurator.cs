using SuchByte.MacroDeck.Device;
using SuchByte.MacroDeck.GUI.CustomControls;
using SuchByte.MacroDeck.Language;
using SuchByte.MacroDeck.Logging;
using SuchByte.MacroDeck.Server;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace SuchByte.MacroDeck.GUI.Dialogs
{
    public partial class DeviceConfigurator : DialogForm
    {
        MacroDeckDevice _macroDeckDevice;

        public DeviceConfigurator(MacroDeckDevice macroDeckDevice)
        {
            this._macroDeckDevice = macroDeckDevice;
            InitializeComponent();
            this.lblBrightness.Text = LanguageManager.Strings.Brightness;
            this.checkAutoConnect.Text = LanguageManager.Strings.AutoConnect;
            this.btnOk.Text = LanguageManager.Strings.Ok;
        }

        private void DeviceConfigurator_Load(object sender, EventArgs e)
        {
            this.brightness.Scroll -= Brightness_Scroll;
            this.brightness.Value = (int)(this._macroDeckDevice.Configuration.Brightness * 10.0);
            this.brightness.Scroll += Brightness_Scroll;
            this.checkAutoConnect.CheckedChanged -= CheckAutoConnect_CheckedChanged;
            this.checkAutoConnect.Checked = this._macroDeckDevice.Configuration.AutoConnect;
            this.checkAutoConnect.CheckedChanged += CheckAutoConnect_CheckedChanged;
            this.radioKeepAwakeNever.CheckedChanged -= RadioKeepAwakeNever_CheckedChanged;
            this.radioKeepAwakeConnected.CheckedChanged -= RadioKeepAwakeConnected_CheckedChanged;
            this.radioKeepAwakeAlways.CheckedChanged -= RadioKeepAwakeAlways_CheckedChanged;
            switch (this._macroDeckDevice.Configuration.WakeLockMethod)
            {
                case WakeLockMethod.Never:
                    this.radioKeepAwakeNever.Checked = true;
                    break;
                case WakeLockMethod.Connected:
                    this.radioKeepAwakeConnected.Checked = true;
                    break;
                case WakeLockMethod.Always:
                    this.radioKeepAwakeAlways.Checked = true;
                    break;
            }

            this.radioKeepAwakeNever.CheckedChanged += RadioKeepAwakeNever_CheckedChanged;
            this.radioKeepAwakeConnected.CheckedChanged += RadioKeepAwakeConnected_CheckedChanged;
            this.radioKeepAwakeAlways.CheckedChanged += RadioKeepAwakeAlways_CheckedChanged;

        }

        private void Brightness_Scroll(object sender, EventArgs e)
        {
            if (_macroDeckDevice == null || !_macroDeckDevice.Available) return;
            this._macroDeckDevice.Configuration.Brightness = this.brightness.Value / 10.0f;
            DeviceManager.SaveKnownDevices();
            MacroDeckClient macroDeckClient = MacroDeckServer.GetMacroDeckClient(this._macroDeckDevice.ClientId);
            if (macroDeckClient != null)
            {
                macroDeckClient.DeviceMessage.SendConfiguration(macroDeckClient);
            }
        }

        private void BtnOk_Click(object sender, EventArgs e)
        {
            this.Close();
        }

        private void CheckAutoConnect_CheckedChanged(object sender, EventArgs e)
        {
            MacroDeckLogger.Trace(GetType(), $"Set auto connect to { this.checkAutoConnect.Checked }");
            if (_macroDeckDevice == null || !_macroDeckDevice.Available) return;
            this._macroDeckDevice.Configuration.AutoConnect = this.checkAutoConnect.Checked;
            DeviceManager.SaveKnownDevices();
            MacroDeckClient macroDeckClient = MacroDeckServer.GetMacroDeckClient(this._macroDeckDevice.ClientId);
            if (macroDeckClient != null)
            {
                macroDeckClient.DeviceMessage.SendConfiguration(macroDeckClient);
            }
        }

        private void RadioKeepAwakeNever_CheckedChanged(object sender, EventArgs e)
        {
            if (this.radioKeepAwakeNever.Checked)
            {
                MacroDeckLogger.Trace(GetType(), "Set keepWake to never");
                if (_macroDeckDevice == null || !_macroDeckDevice.Available) return;
                this._macroDeckDevice.Configuration.WakeLockMethod = WakeLockMethod.Never;
                DeviceManager.SaveKnownDevices();
                MacroDeckClient macroDeckClient = MacroDeckServer.GetMacroDeckClient(this._macroDeckDevice.ClientId);
                if (macroDeckClient != null)
                {
                    macroDeckClient.DeviceMessage.SendConfiguration(macroDeckClient);
                }
            }
        }

        private void RadioKeepAwakeConnected_CheckedChanged(object sender, EventArgs e)
        {
            if (this.radioKeepAwakeConnected.Checked)
            {
                MacroDeckLogger.Trace(GetType(), "Set keepWake to connected");
                if (_macroDeckDevice == null || !_macroDeckDevice.Available) return;
                this._macroDeckDevice.Configuration.WakeLockMethod = WakeLockMethod.Connected;
                DeviceManager.SaveKnownDevices();
                MacroDeckClient macroDeckClient = MacroDeckServer.GetMacroDeckClient(this._macroDeckDevice.ClientId);
                if (macroDeckClient != null)
                {
                    macroDeckClient.DeviceMessage.SendConfiguration(macroDeckClient);
                }
            }
        }

        private void RadioKeepAwakeAlways_CheckedChanged(object sender, EventArgs e)
        {
            if (this.radioKeepAwakeAlways.Checked)
            {
                MacroDeckLogger.Trace(GetType(), "Set keepWake to always");
                if (_macroDeckDevice == null || !_macroDeckDevice.Available) return;
                this._macroDeckDevice.Configuration.WakeLockMethod = WakeLockMethod.Always;
                DeviceManager.SaveKnownDevices();
                MacroDeckClient macroDeckClient = MacroDeckServer.GetMacroDeckClient(this._macroDeckDevice.ClientId);
                if (macroDeckClient != null)
                {
                    macroDeckClient.DeviceMessage.SendConfiguration(macroDeckClient);
                }
            }
        }
    }
}
