using SuchByte.MacroDeck.Device;
using SuchByte.MacroDeck.GUI.CustomControls;
using SuchByte.MacroDeck.InternalPlugins.DevicePlugin.ViewModels;
using SuchByte.MacroDeck.Plugins;
using SuchByte.MacroDeck.Server;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace SuchByte.MacroDeck.InternalPlugins.DevicePlugin.Views
{
    public partial class SetBrightnessActionConfigView : ActionConfigControl
    {
        private readonly SetBrightnessActionConfigViewModel _viewModel;


        public SetBrightnessActionConfigView(PluginAction action)
        {
            InitializeComponent();
            this._viewModel = new SetBrightnessActionConfigViewModel(action);
        }

        private void SetBrightnessActionConfigView_Load(object sender, EventArgs e)
        {
            this.LoadKnownDevices();
            this.LoadCurrentConfiguration();
        }

        private void LoadCurrentConfiguration()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(this._viewModel.ClientId))
                {
                    this.radioCurrentDevice.Checked = true;
                }
                else
                {
                    this.radioFixedDevice.Checked = true;
                    var macroDeckDevice = DeviceManager.GetKnownDevices().Find(x => x.ClientId.Equals(this._viewModel.ClientId));
                    if (macroDeckDevice != null)
                    {
                        this.devicesList.Text = macroDeckDevice.DisplayName ?? "";
                    }
                }
                this.brightness.Value = (int)(this._viewModel.Brightness * 10.0);
            }
            catch { }
        }

        private void LoadKnownDevices()
        {
            foreach (MacroDeckDevice macroDeckDevice in DeviceManager.GetKnownDevices().FindAll(x => x.DeviceType == DeviceType.Android || x.DeviceType == DeviceType.iOS).ToArray())
            {
                this.devicesList.Items.Add(macroDeckDevice.DisplayName);
            }
        }
        public override bool OnActionSave()
        {
            if (this.radioCurrentDevice.Checked)
            {
                this._viewModel.ClientId = "";
            }
            else
            {
                var macroDeckDevice = DeviceManager.GetKnownDevices().Find(x => x.DisplayName.Equals(this.devicesList.Text));
                if (macroDeckDevice != null)
                {
                    this._viewModel.ClientId = macroDeckDevice.ClientId ?? "";
                }
            }

            this._viewModel.Brightness = this.brightness.Value / 10.0f;
            if (this.radioFixedDevice.Checked && string.IsNullOrWhiteSpace(this._viewModel.ClientId)) return false;

            return this._viewModel.SaveConfig();
        }

        private void DevicesList_SelectedIndexChanged(object sender, EventArgs e)
        {
            var macroDeckDevice = DeviceManager.GetKnownDevices().Find(x => x.DisplayName.Equals(this.devicesList.Text));
            if (macroDeckDevice == null) return;
            var configuration = macroDeckDevice.Configuration;
            if (configuration == null) return;
            this.brightness.Value = (int)(configuration.Brightness * 10.0);
        }

        private void Brightness_Scroll(object sender, EventArgs e)
        {
            if (!this.radioFixedDevice.Checked) return;
            var macroDeckDevice = DeviceManager.GetKnownDevices().Find(x => x.DisplayName.Equals(this.devicesList.Text));
            if (macroDeckDevice == null || !macroDeckDevice.Available) return;
            macroDeckDevice.Configuration.Brightness = this.brightness.Value / 10.0f;
            MacroDeckClient macroDeckClient = MacroDeckServer.GetMacroDeckClient(macroDeckDevice.ClientId);
            if (macroDeckClient != null)
            {
                macroDeckClient.DeviceMessage.SendConfiguration(macroDeckClient);
            }
        }
    }
}
