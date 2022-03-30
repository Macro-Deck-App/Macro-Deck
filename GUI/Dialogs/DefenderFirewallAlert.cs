using SuchByte.MacroDeck.GUI.CustomControls;
using SuchByte.MacroDeck.Language;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace SuchByte.MacroDeck.GUI.Dialogs
{
    public partial class DefenderFirewallAlert : DialogForm
    {
        public DefenderFirewallAlert()
        {
            InitializeComponent();
            this.SetCloseIconVisible(false);

            this.lblImportant.Text = LanguageManager.Strings.Important;
            this.lblInfo.Text = LanguageManager.Strings.FirewallAlertInfo;
            this.btnGotIt.Text = LanguageManager.Strings.GotIt;
        }

        private void BtnGotIt_Click(object sender, EventArgs e)
        {
            this.Close();
        }
    }
}
