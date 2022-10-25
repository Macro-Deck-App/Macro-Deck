using System.Drawing;
using System.Windows.Forms;
using SuchByte.MacroDeck.Language;

namespace SuchByte.MacroDeck.GUI.CustomControls
{
    public partial class MessageBox : DialogForm
    {
        public MessageBox()
        {
            InitializeComponent();
            SetCloseIconVisible(false);
            if (Parent == null)
            {
                StartPosition = FormStartPosition.CenterScreen;
            }
        }

        public DialogResult ShowDialog(string title, string message, MessageBoxButtons messageBoxButtons)
        {
            lblTitle.Text = title;
            lblMessage.Text = message;
            buttonMessageBoxPanel.Controls.Clear();
            switch (messageBoxButtons)
            {
                default:
                case MessageBoxButtons.OK:
                    var btnOK = new ButtonPrimary();
                    btnOK.Text = LanguageManager.Strings.Ok;
                    btnOK.AutoSize = true;
                    btnOK.AutoSizeMode = AutoSizeMode.GrowAndShrink;
                    btnOK.MinimumSize = new Size(75, 0);
                    btnOK.Select();
                    btnOK.Click += (sender, args) =>
                    {
                        DialogResult = DialogResult.OK;
                        Close();
                    };
                    buttonMessageBoxPanel.Controls.Add(btnOK);
                    break;
                case MessageBoxButtons.YesNo:
                    var btnNo = new ButtonPrimary();
                    btnNo.Text = LanguageManager.Strings.No;
                    btnNo.AutoSize = true;
                    btnNo.AutoSizeMode = AutoSizeMode.GrowAndShrink;
                    btnNo.MinimumSize = new Size(75, 0);
                    btnNo.Click += (sender, args) =>
                    {
                        DialogResult = DialogResult.No;
                        Close();
                    };
                    var btnYes = new ButtonPrimary();
                    btnYes.Text = LanguageManager.Strings.Yes;
                    btnYes.AutoSize = true;
                    btnYes.AutoSizeMode = AutoSizeMode.GrowAndShrink;
                    btnYes.MinimumSize = new Size(75, 0);
                    btnYes.Select();
                    btnYes.Click += (sender, args) =>
                    {
                        DialogResult = DialogResult.Yes;
                        Close();
                    };
                    
                    buttonMessageBoxPanel.Controls.Add(btnNo);
                    buttonMessageBoxPanel.Controls.Add(btnYes);
                    break;
            }
            return ShowDialog();
        }
    }
}
