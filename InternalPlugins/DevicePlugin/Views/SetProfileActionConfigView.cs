using SuchByte.MacroDeck.Device;
using SuchByte.MacroDeck.GUI.CustomControls;
using SuchByte.MacroDeck.InternalPlugins.DevicePlugin.ViewModels;
using SuchByte.MacroDeck.Language;
using SuchByte.MacroDeck.Plugins;
using SuchByte.MacroDeck.Profiles;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace SuchByte.MacroDeck.InternalPlugins.DevicePlugin.Views
{
    public partial class SetProfileActionConfigView : ActionConfigControl
    {

        private readonly SetProfileActionConfigViewModel _viewModel;

        public SetProfileActionConfigView(PluginAction action)
        {
            InitializeComponent();
            this.lblDevice.Text = LanguageManager.Strings.Device;
            this.lblProfile.Text = LanguageManager.Strings.Profile;
            this.radioCurrentDevice.Text = LanguageManager.Strings.WhereExecuted;
            this._viewModel = new SetProfileActionConfigViewModel(action);
        }

        private void SetProfileActionConfigView_Load(object sender, EventArgs e)
        {
            this.LoadKnownDevices();
            this.LoadProfiles();
            this.LoadCurrentConfiguration();
        }

        private void LoadCurrentConfiguration()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(this._viewModel.ClientId))
                {
                    this.radioCurrentDevice.Checked = true;
                } else
                {
                    this.radioFixedDevice.Checked = true;
                    var macroDeckDevice = DeviceManager.GetKnownDevices().Find(x => x.ClientId.Equals(this._viewModel.ClientId));
                    if (macroDeckDevice != null)
                    {
                        this.devicesList.Text = macroDeckDevice.DisplayName ?? "";
                    }
                }
                var profile = ProfileManager.FindProfileById(this._viewModel.ProfileId);

                if (profile != null)
                {
                    this.profilesList.Text = profile.DisplayName;
                }
            } catch { }
        }

        private void LoadKnownDevices()
        {
            foreach (MacroDeckDevice macroDeckDevice in DeviceManager.GetKnownDevices().ToArray())
            {
                this.devicesList.Items.Add(macroDeckDevice.DisplayName);
            }
        }

        private void LoadProfiles()
        {
            foreach (MacroDeckProfile macroDeckProfile in ProfileManager.Profiles)
            {
                this.profilesList.Items.Add(macroDeckProfile.DisplayName);
            }
        }

        public override bool OnActionSave()
        {
            if (this.radioCurrentDevice.Checked)
            {
                this._viewModel.ClientId = "";
            } else
            {
                var macroDeckDevice = DeviceManager.GetKnownDevices().Find(x => x.DisplayName.Equals(this.devicesList.Text));
                if (macroDeckDevice != null)
                {
                    this._viewModel.ClientId = macroDeckDevice.ClientId ?? "";
                }
            }
            var profile = ProfileManager.FindProfileByDisplayName(this.profilesList.Text);

            if (profile != null)
            {
                this._viewModel.ProfileId = profile.ProfileId;
            }
            if ((this.radioFixedDevice.Checked && string.IsNullOrWhiteSpace(this._viewModel.ClientId)) || string.IsNullOrWhiteSpace(this._viewModel.ProfileId)) return false;
            
            return this._viewModel.SaveConfig();
        }

        private void radioFixedDevice_CheckedChanged(object sender, EventArgs e)
        {
            this.devicesList.Enabled = this.radioFixedDevice.Checked;
        }
    }
}
