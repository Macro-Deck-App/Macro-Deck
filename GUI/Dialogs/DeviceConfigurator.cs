using SuchByte.MacroDeck.Device;
using SuchByte.MacroDeck.GUI.CustomControls;
using SuchByte.MacroDeck.Server;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace SuchByte.MacroDeck.GUI.Dialogs
{
    public partial class DeviceConfigurator : DialogForm
    {
        MacroDeckDevice _macroDeckDevice;

        public DeviceConfigurator(MacroDeckDevice macroDeckDevice)
        {
            this._macroDeckDevice = macroDeckDevice;
            InitializeComponent();
        }

        private void DeviceConfigurator_Load(object sender, EventArgs e)
        {
            this.brightness.Scroll -= Brightness_Scroll;
            this.brightness.Value = (int)(this._macroDeckDevice.Configuration.Brightness * 10.0);
            this.brightness.Scroll += Brightness_Scroll;
        }

        private void Brightness_Scroll(object sender, EventArgs e)
        {
            this._macroDeckDevice.Configuration.Brightness = this.brightness.Value / 10.0f;
            DeviceManager.SaveKnownDevices();
            //this._macroDeckDevice.ClientId.SendConfiguration(MacroDeckServer.GetMacroDeckClient(this._macroDeckDevice.ClientId));
        }
    }
}
