using System;
using System.Drawing;
using System.Windows.Forms;
using SuchByte.MacroDeck.Device;
using SuchByte.MacroDeck.GUI.Dialogs;
using SuchByte.MacroDeck.Language;
using SuchByte.MacroDeck.Profiles;
using SuchByte.MacroDeck.Properties;
using SuchByte.MacroDeck.Server;

namespace SuchByte.MacroDeck.GUI.CustomControls;

public partial class DeviceInfo : RoundedUserControl
{
    MacroDeckDevice _macroDeckDevice;
    public MacroDeckDevice MacroDeckDevice => _macroDeckDevice;

    public DeviceInfo(MacroDeckDevice macroDeckDevice)
    {
        _macroDeckDevice = macroDeckDevice;
        InitializeComponent();
        btnConfigure.Text = LanguageManager.Strings.DeviceSettings;
        lblIdLabel.Text = LanguageManager.Strings.Id;
        lblStatusLabel.Text = LanguageManager.Strings.Status;
        lblDisplayName.Text = LanguageManager.Strings.Name;
        lblProfile.Text = LanguageManager.Strings.Profile;
        checkBlockConnection.Text = LanguageManager.Strings.BlockConnection;
    }

    private void DeviceInfo_Load(object sender, EventArgs e)
    {
        LoadDevice();

    }

    public void LoadDevice()
    {
        profiles.SelectedIndexChanged -= Profiles_SelectedIndexChanged;
        profiles.Items.Clear();
        foreach (var macroDeckProfile in ProfileManager.Profiles)
        {
            profiles.Items.Add(macroDeckProfile.DisplayName);
        }

        if (_macroDeckDevice.ProfileId != null && _macroDeckDevice.ProfileId.Length > 0)
        {
            var macroDeckProfile = ProfileManager.FindProfileById(_macroDeckDevice.ProfileId);
            if (macroDeckProfile != null)
            {
                profiles.Text = macroDeckProfile.DisplayName;
            }
        }
        profiles.SelectedIndexChanged += Profiles_SelectedIndexChanged;

        lblId.Text = _macroDeckDevice.ClientId;
        checkBlockConnection.CheckedChanged -= CheckBlockConnection_CheckedChanged;
        checkBlockConnection.Checked = _macroDeckDevice.Blocked;
        checkBlockConnection.CheckedChanged += CheckBlockConnection_CheckedChanged;
        displayName.Text = _macroDeckDevice.DisplayName;
        lblStatus.Text = _macroDeckDevice.Available ? LanguageManager.Strings.Connected : LanguageManager.Strings.Disconnected;
        lblStatus.ForeColor = _macroDeckDevice.Available ? Color.Green : Color.Red;

        switch (_macroDeckDevice.DeviceType)
        {
            case DeviceType.Web:
                lblDeviceType.Text = LanguageManager.Strings.WebClient;
                iconDeviceType.Image = Resources.Web;
                btnConfigure.Visible = false;
                profiles.Enabled = true;
                break;
            case DeviceType.Android:
                lblDeviceType.Text = LanguageManager.Strings.AndroidApp;
                iconDeviceType.Image = Resources.Android;
                btnConfigure.Visible = _macroDeckDevice.Available;
                profiles.Enabled = true;
                break;
            case DeviceType.iOS:
                lblDeviceType.Text = LanguageManager.Strings.IOSApp;
                iconDeviceType.Image = Resources.iOS;
                btnConfigure.Visible = false; //TODO
                profiles.Enabled = true;
                break;
            default:
                lblDeviceType.Text = LanguageManager.Strings.WebClient;
                iconDeviceType.Image = Resources.Web;
                btnConfigure.Visible = false;
                profiles.Enabled = true;
                break;
        }
    }

    private void BtnRemove_Click(object sender, EventArgs e)
    {
        if (_macroDeckDevice.Available)
        {
            MacroDeckServer.GetMacroDeckClient(_macroDeckDevice.ClientId).SocketConnection.Close();
        }
        DeviceManager.RemoveKnownDevice(_macroDeckDevice);
    }

    private void BtnChangeDisplayName_Click(object sender, EventArgs e)
    {
        if (DeviceManager.IsDisplayNameAvailable(displayName.Text))
        {
            DeviceManager.RenameMacroDeckDevice(_macroDeckDevice, displayName.Text);
        } else
        {
            using var msgBox = new MessageBox();
            msgBox.ShowDialog(LanguageManager.Strings.CantChangeName, string.Format(LanguageManager.Strings.DeviceCalledXAlreadyExists, displayName.Text), MessageBoxButtons.OK);
        }
    }

    private void CheckBlockConnection_CheckedChanged(object sender, EventArgs e)
    {
        DeviceManager.SetBlocked(_macroDeckDevice, checkBlockConnection.Checked);
    }

    private void Profiles_SelectedIndexChanged(object sender, EventArgs e)
    {
        var macroDeckProfile = ProfileManager.FindProfileByDisplayName(profiles.Text);
        if (macroDeckProfile != null)
        {
            DeviceManager.SetProfile(_macroDeckDevice, macroDeckProfile);
            DeviceManager.SaveKnownDevices();
        }
    }

    private void BtnConfigure_Click(object sender, EventArgs e)
    {
        using var deviceConfigurator = new DeviceConfigurator(_macroDeckDevice);
        deviceConfigurator.ShowDialog();
    }
}