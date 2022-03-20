﻿using SuchByte.MacroDeck.Device;
using SuchByte.MacroDeck.GUI.Dialogs;
using SuchByte.MacroDeck.Language;
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
    public partial class DeviceInfo : RoundedUserControl
    {
        MacroDeckDevice _macroDeckDevice;
        public MacroDeckDevice MacroDeckDevice { get { return this._macroDeckDevice; } }

        public DeviceInfo(MacroDeckDevice macroDeckDevice)
        {
            this._macroDeckDevice = macroDeckDevice;
            InitializeComponent();
            this.btnConfigure.Text = LanguageManager.Strings.DeviceSettings;
            this.lblIdLabel.Text = LanguageManager.Strings.Id;
            this.lblStatusLabel.Text = LanguageManager.Strings.Status;
            this.lblDisplayName.Text = LanguageManager.Strings.Name;
            this.lblProfile.Text = LanguageManager.Strings.Profile;
            this.checkBlockConnection.Text = LanguageManager.Strings.BlockConnection;
        }

        private void DeviceInfo_Load(object sender, EventArgs e)
        {
            this.LoadDevice();

        }

        public void LoadDevice()
        {
            this.profiles.SelectedIndexChanged -= this.Profiles_SelectedIndexChanged;
            this.profiles.Items.Clear();
            foreach (MacroDeckProfile macroDeckProfile in ProfileManager.Profiles)
            {
                this.profiles.Items.Add(macroDeckProfile.DisplayName);
            }

            if (this._macroDeckDevice.ProfileId != null && this._macroDeckDevice.ProfileId.Length > 0)
            {
                MacroDeckProfile macroDeckProfile = ProfileManager.FindProfileById(this._macroDeckDevice.ProfileId);
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
            this.lblStatus.Text = this._macroDeckDevice.Available ? LanguageManager.Strings.Connected : LanguageManager.Strings.Disconnected;
            this.lblStatus.ForeColor = this._macroDeckDevice.Available ? Color.Green : Color.Red;

            switch (this._macroDeckDevice.DeviceType)
            {
                case DeviceType.Web:
                    this.lblDeviceType.Text = LanguageManager.Strings.WebClient;
                    this.iconDeviceType.Image = Properties.Resources.Web;
                    this.btnConfigure.Visible = false;
                    this.profiles.Enabled = true;
                    break;
                case DeviceType.Android:
                    this.lblDeviceType.Text = LanguageManager.Strings.AndroidApp;
                    this.iconDeviceType.Image = Properties.Resources.Android;
                    this.btnConfigure.Visible = this._macroDeckDevice.Available;
                    this.profiles.Enabled = true;
                    break;
                case DeviceType.iOS:
                    this.lblDeviceType.Text = LanguageManager.Strings.IOSApp;
                    this.iconDeviceType.Image = Properties.Resources.iOS;
                    this.btnConfigure.Visible = false; //TODO
                    this.profiles.Enabled = true;
                    break;
                case DeviceType.Macro_Deck_DIY_OLED_6_V1:
                    this.lblDeviceType.Text = "Macro Deck DIY OLED 6 v1";
                    this.iconDeviceType.Image = null;
                    this.btnConfigure.Visible = false; //TODO
                    this.profiles.Enabled = false;
                    break;
                default:
                    this.lblDeviceType.Text = LanguageManager.Strings.WebClient;
                    this.iconDeviceType.Image = Properties.Resources.Web;
                    this.btnConfigure.Visible = false;
                    this.profiles.Enabled = true;
                    break;
            }
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
                    msgBox.ShowDialog(LanguageManager.Strings.CantChangeName, string.Format(LanguageManager.Strings.DeviceCalledXAlreadyExists, this.displayName.Text), MessageBoxButtons.OK);
                }
            }
        }

        private void CheckBlockConnection_CheckedChanged(object sender, EventArgs e)
        {
            DeviceManager.SetBlocked(this._macroDeckDevice, this.checkBlockConnection.Checked);
        }

        private void Profiles_SelectedIndexChanged(object sender, EventArgs e)
        {
            MacroDeckProfile macroDeckProfile = ProfileManager.FindProfileByDisplayName(this.profiles.Text);
            if (macroDeckProfile != null)
            {
                DeviceManager.SetProfile(this._macroDeckDevice, macroDeckProfile);
                DeviceManager.SaveKnownDevices();
            }
        }

        private void BtnConfigure_Click(object sender, EventArgs e)
        {
            using (var deviceConfigurator = new DeviceConfigurator(this._macroDeckDevice))
            {
                deviceConfigurator.ShowDialog();
            }
        }
    }
}
