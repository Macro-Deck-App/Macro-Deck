using SuchByte.MacroDeck.Language;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Globalization;
using System.Text;
using System.Windows.Forms;

namespace SuchByte.MacroDeck.GUI.InitialSetupPages
{
    public partial class SetupPage1 : UserControl
    {
        public event EventHandler LanguageChanged;

        public SetupPage1()
        {
            InitializeComponent();
            this.lblWelcome.Text = LanguageManager.Strings.InitialSetupWelcome;
            this.lblLetsConfigure.Text = LanguageManager.Strings.InitialSetupLetsConfigure;
            this.lblSelectLanguage.Text = LanguageManager.Strings.InitialSetupSelectLanguage;
        }

       

        private void Languages_SelectedIndexChanged(object sender, EventArgs e)
        {
            LanguageManager.SetLanguage(languages.SelectedItem.ToString());
            if (LanguageChanged != null)
            {
                LanguageChanged(null, EventArgs.Empty);
            }
        }

        private void SetupPage1_Load(object sender, EventArgs e)
        {
            this.languages.SelectedIndexChanged -= this.Languages_SelectedIndexChanged;
            foreach (Strings languageStrings in LanguageManager.Languages)
            {
                this.languages.Items.Add(languageStrings.__Language__);
            }
            this.languages.SelectedItem = LanguageManager.Strings.__Language__;
            this.languages.SelectedIndexChanged += this.Languages_SelectedIndexChanged;
            
        }
    }
}
