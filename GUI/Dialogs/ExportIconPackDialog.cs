using SuchByte.MacroDeck.GUI.CustomControls;
using SuchByte.MacroDeck.Icons;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace SuchByte.MacroDeck.GUI.Dialogs
{
    public partial class ExportIconPackDialog : DialogForm
    {

        public IconPackLegacy IconPack;


        public ExportIconPackDialog(IconPackLegacy iconPack)
        {
            InitializeComponent();
            this.lblAuthor.Text = Language.LanguageManager.Strings.Author;
            this.lblVersion.Text = Language.LanguageManager.Strings.Version;
            this.lblDescription.Text = Language.LanguageManager.Strings.Description;
            this.lblLicense.Text = Language.LanguageManager.Strings.License;
            this.btnOk.Text = Language.LanguageManager.Strings.Ok;
            this.IconPack = iconPack;
        }

        private void ExportIconPackDialog_Load(object sender, EventArgs e)
        {
            this.author.Text = this.IconPack.Author;
            this.version.Text = this.IconPack.Version;
            this.description.Text = this.IconPack.Description;

        }

        private void BtnOk_Click(object sender, EventArgs e)
        {
            if (this.author.Text.Length < 2) return;
            if (this.version.Text.Length < 2) return;

            this.IconPack.Author = this.author.Text;
            this.IconPack.Version = this.version.Text;
            this.IconPack.Description = this.description.Text;

            try
            {
                //this.IconPack.IconPreviewBase64 = Utils.Base64.GetBase64FromImage(Utils.IconPackPreview.GeneratePreviewImage(this.IconPack));
            } catch { }

            this.DialogResult = DialogResult.OK;
        }
    }
}
