using SuchByte.MacroDeck.GUI.CustomControls;
using SuchByte.MacroDeck.InternalPlugins.ActionButtonPlugin.ViewModels;
using SuchByte.MacroDeck.Language;
using SuchByte.MacroDeck.Plugins;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace SuchByte.MacroDeck.InternalPlugins.ActionButtonPlugin.Views
{
    public partial class ActionButtonSetBackgroundColorActionConfigView : ActionConfigControl
    {
        private readonly ActionButtonSetBackgroundColorActionConfigViewModel _viewModel;

        public ActionButtonSetBackgroundColorActionConfigView(PluginAction pluginAction)
        {
            this._viewModel = new ActionButtonSetBackgroundColorActionConfigViewModel(pluginAction);
            InitializeComponent();
            this.radioFixed.Text = LanguageManager.Strings.Fixed;
            this.radioRandom.Text = LanguageManager.Strings.Random;
        }

        private void ActionButtonSetBackgroundColorActionConfigView_Load(object sender, EventArgs e)
        {
            this.btnChangeColor.BackColor = this._viewModel.Color;
            switch (this._viewModel.Method)
            {
                case Enums.SetBackgroundColorMethod.Fixed:
                    this.radioFixed.Checked = true;
                    break;
                case Enums.SetBackgroundColorMethod.Random:
                    this.radioRandom.Checked = true;
                    break;
            }
        }

        public override bool OnActionSave()
        {
            this._viewModel.Color = this.btnChangeColor.BackColor;
            if (this.radioFixed.Checked)
            {
                this._viewModel.Method = Enums.SetBackgroundColorMethod.Fixed;
            } else if (this.radioRandom.Checked)
            {
                this._viewModel.Method = Enums.SetBackgroundColorMethod.Random;
            }
            return this._viewModel.SaveConfig();
        }

        private void BtnChangeColor_Click(object sender, EventArgs e)
        {
            using (var colorDialog = new ColorDialog()
            {
                Color = this.btnChangeColor.BackColor,
                FullOpen = true,
                CustomColors = new int[] { ColorTranslator.ToOle(Color.FromArgb(35,35,35)) }
            })
            {
                if (colorDialog.ShowDialog() == DialogResult.OK)
                {
                    this.btnChangeColor.BackColor = colorDialog.Color;
                }
            }
        }

        private void RadioRandom_CheckedChanged(object sender, EventArgs e)
        {
            
        }

        private void RadioFixed_CheckedChanged(object sender, EventArgs e)
        {
            this.btnChangeColor.Visible = this.radioFixed.Checked;
        }
    }
}
