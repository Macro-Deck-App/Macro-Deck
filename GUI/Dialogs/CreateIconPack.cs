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
    public partial class CreateIconPack : DialogForm
    {
        public CreateIconPack()
        {
            InitializeComponent();
            this.lblName.Text = Language.LanguageManager.Strings.Name;
            this.lblAuthor.Text = Language.LanguageManager.Strings.Author;
            this.lblVersion.Text = Language.LanguageManager.Strings.Version;
            this.author.Text = Environment.UserName;
            this.version.Text = "1.0.0";
        }

        public CreateIconPack(string iconPackName, string author, string version)
        {
            this.iconPackName.Text = iconPackName;
            this.author.Text = author;
            this.version.Text = version;
        }

        private string _iconPackName;
        private string _author;
        private string _version;

        public string IconPackName { get { return this._iconPackName; } }
        public string Author { get { return this._author; } }
        public string Version { get { return this._version; } }

        private void BtnOk_Click(object sender, EventArgs e)
        {
            if (this.iconPackName.Text.Length < 2) return;
            if (IconManager.GetIconPackByName(this.iconPackName.Text) != null)
            {
                using (var messageBox = new GUI.CustomControls.MessageBox())
                {
                    messageBox.ShowDialog(Language.LanguageManager.Strings.CantCreateIconPack, string.Format(Language.LanguageManager.Strings.IconPackCalledXAlreadyExists, this.iconPackName.Text), MessageBoxButtons.OK);
                    messageBox.Dispose();
                    return;
                }
            } else
            {
                this._iconPackName = this.iconPackName.Text;
                this._author = this.author.Text;
                this._version = this.version.Text;
                IconManager.CreateIconPack(this._iconPackName, this._author, this._version);
                this.DialogResult = DialogResult.OK;
                this.Close();
            }
        }
    }
}
