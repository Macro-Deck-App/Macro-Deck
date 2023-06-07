using SuchByte.MacroDeck.Device;
using SuchByte.MacroDeck.GUI.CustomControls;
using SuchByte.MacroDeck.InternalPlugins.DevicePlugin.ViewModels;
using SuchByte.MacroDeck.Language;
using SuchByte.MacroDeck.Plugins;
using SuchByte.MacroDeck.Profiles;

namespace SuchByte.MacroDeck.InternalPlugins.DevicePlugin.Views;

public partial class SetProfileActionConfigView : ActionConfigControl
{

    private readonly SetProfileActionConfigViewModel _viewModel;

    public SetProfileActionConfigView(PluginAction action)
    {
        InitializeComponent();
        lblDevice.Text = LanguageManager.Strings.Device;
        lblProfile.Text = LanguageManager.Strings.Profile;
        radioCurrentDevice.Text = LanguageManager.Strings.WhereExecuted;
        _viewModel = new SetProfileActionConfigViewModel(action);
    }

    private void SetProfileActionConfigView_Load(object sender, EventArgs e)
    {
        LoadKnownDevices();
        LoadProfiles();
        LoadCurrentConfiguration();
    }

    private void LoadCurrentConfiguration()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_viewModel.ClientId))
            {
                radioCurrentDevice.Checked = true;
            } else
            {
                radioFixedDevice.Checked = true;
                var macroDeckDevice = DeviceManager.GetKnownDevices().Find(x => x.ClientId.Equals(_viewModel.ClientId));
                if (macroDeckDevice != null)
                {
                    devicesList.Text = macroDeckDevice.DisplayName ?? "";
                }
            }
            var profile = ProfileManager.FindProfileById(_viewModel.ProfileId);

            if (profile != null)
            {
                profilesList.Text = profile.DisplayName;
            }
        } catch { }
    }

    private void LoadKnownDevices()
    {
        foreach (var macroDeckDevice in DeviceManager.GetKnownDevices().ToArray())
        {
            devicesList.Items.Add(macroDeckDevice.DisplayName);
        }
    }

    private void LoadProfiles()
    {
        foreach (var macroDeckProfile in ProfileManager.Profiles)
        {
            profilesList.Items.Add(macroDeckProfile.DisplayName);
        }
    }

    public override bool OnActionSave()
    {
        if (radioCurrentDevice.Checked)
        {
            _viewModel.ClientId = "";
        } else
        {
            var macroDeckDevice = DeviceManager.GetKnownDevices().Find(x => x.DisplayName.Equals(devicesList.Text));
            if (macroDeckDevice != null)
            {
                _viewModel.ClientId = macroDeckDevice.ClientId ?? "";
            }
        }
        var profile = ProfileManager.FindProfileByDisplayName(profilesList.Text);

        if (profile != null)
        {
            _viewModel.ProfileId = profile.ProfileId;
        }
        if ((radioFixedDevice.Checked && string.IsNullOrWhiteSpace(_viewModel.ClientId)) || string.IsNullOrWhiteSpace(_viewModel.ProfileId)) return false;
            
        return _viewModel.SaveConfig();
    }

    private void radioFixedDevice_CheckedChanged(object sender, EventArgs e)
    {
        devicesList.Enabled = radioFixedDevice.Checked;
    }
}