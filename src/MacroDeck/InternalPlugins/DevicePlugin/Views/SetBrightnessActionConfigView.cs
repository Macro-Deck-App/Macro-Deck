using SuchByte.MacroDeck.Device;
using SuchByte.MacroDeck.GUI.CustomControls;
using SuchByte.MacroDeck.InternalPlugins.DevicePlugin.ViewModels;
using SuchByte.MacroDeck.Plugins;
using SuchByte.MacroDeck.Server;

namespace SuchByte.MacroDeck.InternalPlugins.DevicePlugin.Views;

public partial class SetBrightnessActionConfigView : ActionConfigControl
{
    private readonly SetBrightnessActionConfigViewModel _viewModel;


    public SetBrightnessActionConfigView(PluginAction action)
    {
        InitializeComponent();
        _viewModel = new SetBrightnessActionConfigViewModel(action);
    }

    private void SetBrightnessActionConfigView_Load(object sender, EventArgs e)
    {
        LoadKnownDevices();
        LoadCurrentConfiguration();
    }

    private void LoadCurrentConfiguration()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_viewModel.ClientId))
            {
                radioCurrentDevice.Checked = true;
            }
            else
            {
                radioFixedDevice.Checked = true;
                var macroDeckDevice = DeviceManager.GetKnownDevices().Find(x => x.ClientId.Equals(_viewModel.ClientId));
                if (macroDeckDevice != null)
                {
                    devicesList.Text = macroDeckDevice.DisplayName ?? "";
                }
            }
            brightness.Value = (int)(_viewModel.Brightness * 10.0);
        }
        catch { }
    }

    private void LoadKnownDevices()
    {
        foreach (var macroDeckDevice in DeviceManager.GetKnownDevices().FindAll(x => x.DeviceType == DeviceType.Android || x.DeviceType == DeviceType.iOS).ToArray())
        {
            devicesList.Items.Add(macroDeckDevice.DisplayName);
        }
    }
    public override bool OnActionSave()
    {
        if (radioCurrentDevice.Checked)
        {
            _viewModel.ClientId = "";
        }
        else
        {
            var macroDeckDevice = DeviceManager.GetKnownDevices().Find(x => x.DisplayName.Equals(devicesList.Text));
            if (macroDeckDevice != null)
            {
                _viewModel.ClientId = macroDeckDevice.ClientId ?? "";
            }
        }

        _viewModel.Brightness = brightness.Value / 10.0f;
        if (radioFixedDevice.Checked && string.IsNullOrWhiteSpace(_viewModel.ClientId)) return false;

        return _viewModel.SaveConfig();
    }

    private void DevicesList_SelectedIndexChanged(object sender, EventArgs e)
    {
        var macroDeckDevice = DeviceManager.GetKnownDevices().Find(x => x.DisplayName.Equals(devicesList.Text));
        var configuration = macroDeckDevice?.Configuration;
        if (configuration == null) return;
        brightness.Value = (int)(configuration.Brightness * 10.0);
    }

    private void Brightness_Scroll(object sender, EventArgs e)
    {
        if (!radioFixedDevice.Checked) return;
        var macroDeckDevice = DeviceManager.GetKnownDevices().Find(x => x.DisplayName.Equals(devicesList.Text));
        if (macroDeckDevice == null || !macroDeckDevice.Available) return;
        macroDeckDevice.Configuration.Brightness = brightness.Value / 10.0f;
        var macroDeckClient = MacroDeckServer.GetMacroDeckClient(macroDeckDevice.ClientId);
        macroDeckClient?.DeviceMessage.SendConfiguration(macroDeckClient);
    }
}