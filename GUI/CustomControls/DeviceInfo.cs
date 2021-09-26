﻿using SuchByte.MacroDeck.Device;
using SuchByte.MacroDeck.Profiles;
using SuchByte.MacroDeck.Server;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace SuchByte.MacroDeck.GUI.CustomControls
{
    public partial class DeviceInfo : UserControl
    {
        MacroDeckDevice _macroDeckDevice;
        public MacroDeckDevice MacroDeckDevice { get { return this._macroDeckDevice; } }

        public DeviceInfo(MacroDeckDevice macroDeckDevice)
        {
            this._macroDeckDevice = macroDeckDevice;
            InitializeComponent();
            this.lblIdLabel.Text = Language.LanguageManager.Strings.Id;
            this.lblStatusLabel.Text = Language.LanguageManager.Strings.Status;
            this.lblDisplayName.Text = Language.LanguageManager.Strings.Name;
            this.lblProfile.Text = Language.LanguageManager.Strings.Profile;
            this.checkBlockConnection.Text = Language.LanguageManager.Strings.BlockConnection;
        }

        private void DeviceInfo_Load(object sender, EventArgs e)
        {
            this.LoadDevice();

        }

        public void LoadDevice()
        {
            this.profiles.SelectedIndexChanged -= this.Profiles_SelectedIndexChanged;
            this.profiles.Items.Clear();
            foreach (MacroDeckProfile macroDeckProfile in MacroDeck.ProfileManager.Profiles)
            {
                this.profiles.Items.Add(macroDeckProfile.DisplayName);
            }

            if (this._macroDeckDevice.ProfileId != null && this._macroDeckDevice.ProfileId.Length > 0)
            {
                MacroDeckProfile macroDeckProfile = MacroDeck.ProfileManager.FindProfileById(this._macroDeckDevice.ProfileId);
                if (macroDeckProfile != null)
                {
                    this.profiles.Text = macroDeckProfile.DisplayName;
                }
            }
            this.profiles.SelectedIndexChanged += this.Profiles_SelectedIndexChanged;

            this.lblId.Text = this._macroDeckDevice.ClientId;
            this.checkBlockConnection.CheckedChanged -= this.CheckBlockConnection_CheckedChanged;
            this.checkBlockConnection.Checked = this._macroDeckDevice.Blocked;
            this.checkBlockConnection.CheckedChanged += this.CheckBlockConnection_CheckedChanged;
            this.displayName.Text = this._macroDeckDevice.DisplayName;
            this.lblStatus.Text = this._macroDeckDevice.Available ? Language.LanguageManager.Strings.Connected : Language.LanguageManager.Strings.Disconnected;
            this.lblStatus.ForeColor = this._macroDeckDevice.Available ? Color.Green : Color.Red;
        }

        private void BtnRemove_Click(object sender, EventArgs e)
        {
            if (this._macroDeckDevice.Available)
            {
                MacroDeckServer.GetMacroDeckClient(this._macroDeckDevice.ClientId).SocketConnection.Close();
            }
            DeviceManager.RemoveKnownDevice(this._macroDeckDevice);
        }

        private void BtnChangeDisplayName_Click(object sender, EventArgs e)
        {
            if (DeviceManager.IsDisplayNameAvailable(this.displayName.Text))
            {
                DeviceManager.RenameMacroDeckDevice(this._macroDeckDevice, this.displayName.Text);
            } else
            {
                using (MessageBox msgBox = new MessageBox())
                {
                    msgBox.ShowDialog(Language.LanguageManager.Strings.CantChangeName, String.Format(Language.LanguageManager.Strings.DeviceCalledXAlreadyExists, this.displayName.Text), MessageBoxButtons.OK);
                }
            }
        }

        private void CheckBlockConnection_CheckedChanged(object sender, EventArgs e)
        {
            DeviceManager.SetBlocked(this._macroDeckDevice, this.checkBlockConnection.Checked);
        }

        private void Profiles_SelectedIndexChanged(object sender, EventArgs e)
        {
            MacroDeckProfile macroDeckProfile = MacroDeck.ProfileManager.FindProfileByDisplayName(this.profiles.Text);
            if (macroDeckProfile != null)
            {
                DeviceManager.SetProfile(this._macroDeckDevice, macroDeckProfile);
            }
        }

        private void btnApplyProfile_Click(object sender, EventArgs e)
        {
            
        }
    }
}
