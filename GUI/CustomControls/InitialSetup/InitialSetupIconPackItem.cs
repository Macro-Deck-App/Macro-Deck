using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace SuchByte.MacroDeck.GUI.CustomControls
{
    public partial class InitialSetupIconPackItem : RoundedUserControl
    {
        private JObject _jsonObject;

        public JObject JsonObject { get { return this._jsonObject; } }

        public bool Install { get { return this.checkInstall.Checked; } }

        public void SetInstall(bool install)
        {
            this.checkInstall.Checked = install;
            this.checkInstall.Enabled = !install;
        }

        public InitialSetupIconPackItem(JObject jsonObject)
        {
            InitializeComponent();

            this._jsonObject = jsonObject;

            this.lblName.Text = jsonObject["name"].ToString();
            this.lblDescription.Text = jsonObject["description"].ToString();
            this.preview.BackgroundImage = Utils.Base64.GetImageFromBase64(jsonObject["preview"].ToString());
        }
    }
}
