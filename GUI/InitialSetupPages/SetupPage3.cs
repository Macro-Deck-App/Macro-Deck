using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace SuchByte.MacroDeck.GUI.InitialSetupPages
{
    public partial class SetupPage3 : UserControl
    {

        InitialSetup initialSetup;
        public SetupPage3(InitialSetup initialSetup)
        {
            InitializeComponent();
            this.initialSetup = initialSetup;
            this.lblConfigureGridPreferences.Text = Language.LanguageManager.Strings.InitialSetupConfigureGridPreferences;
        }

        private void SetupPage3_Load(object sender, EventArgs e)
        {

        }

        private void Columns_ValueChanged(object sender, EventArgs e)
        {
            //this.initialSetup.Columns = (int)columns.Value;
        }

        private void Rows_ValueChanged(object sender, EventArgs e)
        {
            //this.initialSetup.Rows = (int)rows.Value;
        }
    }
}
