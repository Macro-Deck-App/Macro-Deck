using SuchByte.MacroDeck.GUI.CustomControls;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace SuchByte.MacroDeck.GUI.Dialogs
{
    public partial class IconImportQuality : DialogForm
    {

        public int Pixels;

        public IconImportQuality()
        {
            InitializeComponent();
            this.lblInfo.Text = Language.LanguageManager.Strings.IconImportQualityInfo;
            this.qualityOriginal.Text = Language.LanguageManager.Strings.Original;
            this.qualityHigh.Text = Language.LanguageManager.Strings.High350px;
            this.qualityNormal.Text = Language.LanguageManager.Strings.Normal200px;
            this.qualityLow.Text = Language.LanguageManager.Strings.Low150px;
            this.qualityLowest.Text = Language.LanguageManager.Strings.Lowest100px;
            this.btnOk.Text = Language.LanguageManager.Strings.Ok;
        }

        private void btnOk_Click(object sender, EventArgs e)
        {
            if (this.qualityOriginal.Checked)
            {
                this.Pixels = -1;
            } else if (this.qualityHigh.Checked)
            {
                this.Pixels = 350;
            }
            else if (this.qualityNormal.Checked)
            {
                this.Pixels = 250;
            }
            else if (this.qualityLow.Checked)
            {
                this.Pixels = 150;
            }
            else if (this.qualityLowest.Checked)
            {
                this.Pixels = 100;
            }
            this.DialogResult = DialogResult.OK;
        }
    }
}
