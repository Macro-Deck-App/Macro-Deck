using System.Windows.Forms;
using SuchByte.MacroDeck.Device;
using SuchByte.MacroDeck.GUI.CustomControls;
using SuchByte.MacroDeck.Language;
using SuchByte.MacroDeck.Server;
using SuchByte.MacroDeck.Startup;

namespace SuchByte.MacroDeck.GUI.MainWindowContents;

public partial class DeviceManagerView : UserControl
{
    public DeviceManagerView()
    {
        InitializeComponent();
        Dock = DockStyle.Fill;
        Name = LanguageManager.Strings.DeviceManagerTitle;
        lblKnownDevices.Text = LanguageManager.Strings.KnownDevices;
        lblBehaviour.Text = LanguageManager.Strings.Behaviour;
        radioAllowAll.Text = LanguageManager.Strings.AllowAllNewConnections;
        radioAskNewConnections.Text = LanguageManager.Strings.AskOnNewConnections;
        radioBlockNew.Text = LanguageManager.Strings.BlockAllNewConnections;
    }

    private void DeviceManagerPage_Load(object sender, EventArgs e)
    {
        LoadDevices();
        MacroDeckServer.OnDeviceConnectionStateChanged += OnClientsChanged;
        DeviceManager.OnDevicesChange += OnClientsChanged;
        radioAllowAll.CheckedChanged -= RadioBehaviour_CheckedChanged;
        radioAskNewConnections.CheckedChanged -= RadioBehaviour_CheckedChanged;
        radioBlockNew.CheckedChanged -= RadioBehaviour_CheckedChanged;
        radioAllowAll.Checked = (!MacroDeck.Configuration.AskOnNewConnections && !MacroDeck.Configuration.BlockNewConnections);
        radioAskNewConnections.Checked = MacroDeck.Configuration.AskOnNewConnections;
        radioBlockNew.Checked = MacroDeck.Configuration.BlockNewConnections;
        radioAllowAll.CheckedChanged += RadioBehaviour_CheckedChanged;
        radioAskNewConnections.CheckedChanged += RadioBehaviour_CheckedChanged;
        radioBlockNew.CheckedChanged += RadioBehaviour_CheckedChanged;
    }

    private void OnClientsChanged(object sender, EventArgs e)
    {
        LoadDevices();
    }

    private void LoadDevices()
    {
        if (InvokeRequired)
        {
            Invoke(() => LoadDevices());
            return;
        }

        foreach (var control in devicesList.Controls.OfType<DeviceInfo>())
        {
            if (!DeviceManager.GetKnownDevices().Contains(control.MacroDeckDevice))
            {
                control.Dispose();
                devicesList.Controls.Remove(control);
                continue;
            }
            control.LoadDevice();
        }

        foreach (var macroDeckDevice in DeviceManager.GetKnownDevices().ToArray())
        {
            if (devicesList.Controls.OfType<DeviceInfo>().Where(x => x.MacroDeckDevice.Equals(macroDeckDevice)).FirstOrDefault() != null)
            {
                continue;
            }

            var deviceInfo = new DeviceInfo(macroDeckDevice);
            devicesList.Controls.Add(deviceInfo);
        }
    }

    private void RadioBehaviour_CheckedChanged(object sender, EventArgs e)
    {
        MacroDeck.Configuration.AskOnNewConnections = radioAskNewConnections.Checked;
        MacroDeck.Configuration.BlockNewConnections = radioBlockNew.Checked;
        MacroDeck.Configuration.Save(ApplicationPaths.MainConfigFilePath);
    }
}