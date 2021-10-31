using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
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
            this.lblWelcome.Text = Language.LanguageManager.Strings.InitialSetupWelcome;
            this.lblLetsConfigure.Text = Language.LanguageManager.Strings.InitialSetupLetsConfigure;
            this.lblSelectLanguage.Text = Language.LanguageManager.Strings.InitialSetupSelectLanguage;
        }

        private void Languages_SelectedIndexChanged(object sender, EventArgs e)
        {
            Language.LanguageManager.SetLanguage(languages.SelectedItem.ToString());
            if (LanguageChanged != null)
            {
                LanguageChanged(null, EventArgs.Empty);
            }
        }

        private void SetupPage1_Load(object sender, EventArgs e)
        {
            this.languages.SelectedIndexChanged -= this.Languages_SelectedIndexChanged;
            foreach(Language.Strings languageStrings in Language.LanguageManager.Languages)
            {
                this.languages.Items.Add(languageStrings.__Language__);
            }
            this.languages.SelectedItem = Language.LanguageManager.Strings.__Language__;
            this.languages.SelectedIndexChanged += this.Languages_SelectedIndexChanged;
        }
    }
}
