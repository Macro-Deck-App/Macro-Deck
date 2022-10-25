using System;
using SuchByte.MacroDeck.GUI.CustomControls;
using SuchByte.MacroDeck.Language;

namespace SuchByte.MacroDeck.GUI.Dialogs
{
    public partial class DefenderFirewallAlert : DialogForm
    {
        public DefenderFirewallAlert()
        {
            InitializeComponent();
            SetCloseIconVisible(false);

            lblImportant.Text = LanguageManager.Strings.Important;
            lblInfo.Text = LanguageManager.Strings.FirewallAlertInfo;
            btnGotIt.Text = LanguageManager.Strings.GotIt;
        }

        private void BtnGotIt_Click(object sender, EventArgs e)
        {
            Close();
        }
    }
}
