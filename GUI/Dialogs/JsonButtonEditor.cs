using Newtonsoft.Json;
using SuchByte.MacroDeck.GUI.CustomControls;
using SuchByte.MacroDeck.Logging;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace SuchByte.MacroDeck.GUI.Dialogs
{
    public partial class JsonButtonEditor : DialogForm
    {

        static JsonSerializerSettings jsonSerializerSettings = new JsonSerializerSettings
        {
            TypeNameHandling = TypeNameHandling.Auto,
            NullValueHandling = NullValueHandling.Ignore,
            Formatting = Formatting.Indented,
            Error = (sender, args) => {
                MacroDeckLogger.Warning(typeof(JsonButtonEditor), args.ErrorContext.Error.Message);
                args.ErrorContext.Handled = true; 
            }
        };


        public ActionButton.ActionButton ActionButton { get; set; }


        public JsonButtonEditor(ActionButton.ActionButton actionButton)
        {
            InitializeComponent();

            this.ActionButton = actionButton;
            if (this.ActionButton.LabelOff != null)
            {
                this.ActionButton.LabelOff.LabelBase64 = string.Empty;
            }
            if (this.ActionButton.LabelOn != null)
            {
                this.ActionButton.LabelOn.LabelBase64 = string.Empty;
            }
            this.jsonTextBox.Text = JsonConvert.SerializeObject(this.ActionButton, jsonSerializerSettings);
        }

        private void BtnApply_Click(object sender, EventArgs e)
        {
            this.ActionButton = JsonConvert.DeserializeObject<ActionButton.ActionButton>(this.jsonTextBox.Text, jsonSerializerSettings);
            this.DialogResult = DialogResult.OK;
            this.Close();
        }
    }
}
