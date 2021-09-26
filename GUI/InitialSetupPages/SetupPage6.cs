using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace SuchByte.MacroDeck.GUI.InitialSetupPages
{
    public partial class SetupPage6 : UserControl
    {
        private InitialSetup initialSetup;
        public SetupPage6(InitialSetup initialSetup)
        {
            this.initialSetup = initialSetup;
            InitializeComponent();
            this.lblAlmostDone.Text = Language.LanguageManager.Strings.InitialSetupAlmostDone;
            this.checkAutoStart.Text = Language.LanguageManager.Strings.AutomaticallyStartWithWindows;
            this.checkAutoUpdates.Text = Language.LanguageManager.Strings.AutomaticallyCheckUpdates;
        }

        private void CheckAutoUpdates_CheckedChanged(object sender, EventArgs e)
        {
            initialSetup.configuration.AutoUpdates = this.checkAutoUpdates.Checked;
        }

        private void CheckAutoStart_CheckedChanged(object sender, EventArgs e)
        {
            initialSetup.configuration.AutoStart = this.checkAutoStart.Checked;
        }
    }
}
