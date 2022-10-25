using System;
using System.Windows.Forms;
using SuchByte.MacroDeck.Language;

namespace SuchByte.MacroDeck.GUI.InitialSetupPages
{
    public partial class SetupPage6 : UserControl
    {
        private InitialSetup initialSetup;
        public SetupPage6(InitialSetup initialSetup)
        {
            this.initialSetup = initialSetup;
            InitializeComponent();
            lblAlmostDone.Text = LanguageManager.Strings.InitialSetupAlmostDone;
            checkAutoStart.Text = LanguageManager.Strings.AutomaticallyStartWithWindows;
            checkAutoUpdates.Text = LanguageManager.Strings.AutomaticallyCheckUpdates;
        }

        private void CheckAutoUpdates_CheckedChanged(object sender, EventArgs e)
        {
            initialSetup.configuration.AutoUpdates = checkAutoUpdates.Checked;
        }

        private void CheckAutoStart_CheckedChanged(object sender, EventArgs e)
        {
            initialSetup.configuration.AutoStart = checkAutoStart.Checked;
        }
    }
}
