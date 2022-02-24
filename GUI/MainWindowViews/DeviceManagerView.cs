using SuchByte.MacroDeck.Device;
using SuchByte.MacroDeck.GUI.CustomControls;
using SuchByte.MacroDeck.Logging;
using SuchByte.MacroDeck.Server;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SuchByte.MacroDeck.GUI.MainWindowContents
{
    public partial class DeviceManagerView : UserControl
    {
        public DeviceManagerView()
        {
            InitializeComponent();
            this.Dock = DockStyle.Fill;
            this.Name = Language.LanguageManager.Strings.DeviceManagerTitle;
            this.lblKnownDevices.Text = Language.LanguageManager.Strings.KnownDevices;
            this.lblBehaviour.Text = Language.LanguageManager.Strings.Behaviour;
            this.radioAllowAll.Text = Language.LanguageManager.Strings.AllowAllNewConnections;
            this.radioAskNewConnections.Text = Language.LanguageManager.Strings.AskOnNewConnections;
            this.radioBlockNew.Text = Language.LanguageManager.Strings.BlockAllNewConnections;
        }

        private void DeviceManagerPage_Load(object sender, EventArgs e)
        {
            this.LoadDevices();
            MacroDeckServer.OnDeviceConnectionStateChanged += this.OnClientsChanged;
            DeviceManager.OnDevicesChange += this.OnClientsChanged;
            this.radioAllowAll.CheckedChanged -= RadioBehaviour_CheckedChanged;
            this.radioAskNewConnections.CheckedChanged -= RadioBehaviour_CheckedChanged;
            this.radioBlockNew.CheckedChanged -= RadioBehaviour_CheckedChanged;
            this.radioAllowAll.Checked = (!MacroDeck.Configuration.AskOnNewConnections && !MacroDeck.Configuration.BlockNewConnections);
            this.radioAskNewConnections.Checked = MacroDeck.Configuration.AskOnNewConnections;
            this.radioBlockNew.Checked = MacroDeck.Configuration.BlockNewConnections;
            this.radioAllowAll.CheckedChanged += RadioBehaviour_CheckedChanged;
            this.radioAskNewConnections.CheckedChanged += RadioBehaviour_CheckedChanged;
            this.radioBlockNew.CheckedChanged += RadioBehaviour_CheckedChanged;
        }

        private void OnClientsChanged(object sender, EventArgs e)
        {
            this.LoadDevices();
        }

        private void LoadDevices()
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new Action(() => LoadDevices()));
                return;
            }

            foreach (DeviceInfo control in this.devicesList.Controls.OfType<DeviceInfo>())
            {
                if (!DeviceManager.GetKnownDevices().Contains(control.MacroDeckDevice))
                {
                    control.Dispose();
                    this.devicesList.Controls.Remove(control);
                    continue;
                }
                control.LoadDevice();
            }

            foreach (MacroDeckDevice macroDeckDevice in DeviceManager.GetKnownDevices().ToArray())
            {
                if (this.devicesList.Controls.OfType<DeviceInfo>().Where(x => x.MacroDeckDevice.Equals(macroDeckDevice)).FirstOrDefault() != null)
                {
                    continue;
                }

                DeviceInfo deviceInfo = new DeviceInfo(macroDeckDevice);
                this.devicesList.Controls.Add(deviceInfo);
            }
        }

        private void RadioBehaviour_CheckedChanged(object sender, EventArgs e)
        {
            MacroDeck.Configuration.AskOnNewConnections = this.radioAskNewConnections.Checked;
            MacroDeck.Configuration.BlockNewConnections = this.radioBlockNew.Checked;
            MacroDeck.SaveConfiguration();
        }
    }
}
