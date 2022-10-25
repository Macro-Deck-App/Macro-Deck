using System;
using System.Windows.Forms;
using SuchByte.MacroDeck.GUI.CustomControls;
using SuchByte.MacroDeck.Icons;
using SuchByte.MacroDeck.Language;
using MessageBox = SuchByte.MacroDeck.GUI.CustomControls.MessageBox;

namespace SuchByte.MacroDeck.GUI.Dialogs
{
    public partial class CreateIconPack : DialogForm
    {
        public CreateIconPack()
        {
            InitializeComponent();
            lblName.Text = LanguageManager.Strings.Name;
            lblAuthor.Text = LanguageManager.Strings.Author;
            lblVersion.Text = LanguageManager.Strings.Version;
            author.Text = Environment.UserName;
            version.Text = "1.0.0";
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

        public string IconPackName => _iconPackName;
        public string Author => _author;
        public string Version => _version;

        private void BtnOk_Click(object sender, EventArgs e)
        {
            if (iconPackName.Text.Length < 2) return;
            if (IconManager.GetIconPackByName(iconPackName.Text) != null)
            {
                using (var messageBox = new MessageBox())
                {
                    messageBox.ShowDialog(LanguageManager.Strings.CantCreateIconPack, string.Format(LanguageManager.Strings.IconPackCalledXAlreadyExists, iconPackName.Text), MessageBoxButtons.OK);
                    messageBox.Dispose();
                    return;
                }
            }

            _iconPackName = iconPackName.Text;
            _author = author.Text;
            _version = version.Text;
            IconManager.CreateIconPack(_iconPackName, _author, _version);
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
