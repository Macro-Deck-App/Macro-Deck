using System;
using System.Windows.Forms;
using Newtonsoft.Json;
using SuchByte.MacroDeck.GUI.CustomControls;
using SuchByte.MacroDeck.Logging;

namespace SuchByte.MacroDeck.GUI.Dialogs
{
    public partial class JsonButtonEditor : DialogForm
    {

        static JsonSerializerSettings jsonSerializerSettings = new()
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

            ActionButton = actionButton;
            if (ActionButton.LabelOff != null)
            {
                ActionButton.LabelOff.LabelBase64 = string.Empty;
            }
            if (ActionButton.LabelOn != null)
            {
                ActionButton.LabelOn.LabelBase64 = string.Empty;
            }
            jsonTextBox.Text = JsonConvert.SerializeObject(ActionButton, jsonSerializerSettings);
        }

        private void BtnApply_Click(object sender, EventArgs e)
        {
            ActionButton = JsonConvert.DeserializeObject<ActionButton.ActionButton>(jsonTextBox.Text, jsonSerializerSettings);
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
