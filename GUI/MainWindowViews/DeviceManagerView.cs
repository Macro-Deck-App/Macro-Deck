using SuchByte.MacroDeck.Device;
using SuchByte.MacroDeck.GUI.CustomControls;
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
            MacroDeckServer.OnDeviceConnectionStateChanged += new EventHandler(this.OnClientsChanged);
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
            this.Invoke(new Action(() =>
                this.LoadDevices()
            ));
        }

        private void LoadDevices()
        {
            DeviceManager.OnDevicesChange -= this.OnClientsChanged;
            foreach (DeviceInfo control in this.devicesList.Controls)
            {
                if (!DeviceManager.GetKnownDevices().Contains(control.MacroDeckDevice))
                {
                    control.Dispose();
                    this.devicesList.Controls.Remove(control);
                    continue;
                }
                control.LoadDevice();
            }
            //this.devicesList.Controls.Clear();

            foreach (MacroDeckDevice macroDeckDevice in DeviceManager.GetKnownDevices())
            {
                if (this.devicesList.Controls.OfType<DeviceInfo>().Where(x => x.MacroDeckDevice.Equals(macroDeckDevice)).FirstOrDefault() != null)
                {
                    continue;
                }

                Task.Run(() =>
                {
                    DeviceInfo deviceInfo = new DeviceInfo(macroDeckDevice);
                    this.Invoke(new Action(() => this.devicesList.Controls.Add(deviceInfo)));
                });
            }

            DeviceManager.OnDevicesChange += new EventHandler(this.OnClientsChanged);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        private void RadioBehaviour_CheckedChanged(object sender, EventArgs e)
        {
            MacroDeck.Configuration.AskOnNewConnections = this.radioAskNewConnections.Checked;
            MacroDeck.Configuration.BlockNewConnections = this.radioBlockNew.Checked;
            MacroDeck.SaveConfiguration();
        }
    }
}
