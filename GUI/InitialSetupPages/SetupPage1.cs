using System;
using System.Windows.Forms;
using SuchByte.MacroDeck.Language;

namespace SuchByte.MacroDeck.GUI.InitialSetupPages
{
    public partial class SetupPage1 : UserControl
    {
        public event EventHandler LanguageChanged;

        public SetupPage1()
        {
            InitializeComponent();
            lblWelcome.Text = LanguageManager.Strings.InitialSetupWelcome;
            lblLetsConfigure.Text = LanguageManager.Strings.InitialSetupLetsConfigure;
            lblSelectLanguage.Text = LanguageManager.Strings.InitialSetupSelectLanguage;
        }

       

        private void Languages_SelectedIndexChanged(object sender, EventArgs e)
        {
            LanguageManager.SetLanguage(languages.SelectedItem.ToString());
            LanguageChanged?.Invoke(null, EventArgs.Empty);
        }

        private void SetupPage1_Load(object sender, EventArgs e)
        {
            languages.SelectedIndexChanged -= Languages_SelectedIndexChanged;
            foreach (var languageStrings in LanguageManager.Languages)
            {
                languages.Items.Add(languageStrings.__Language__);
            }
            languages.SelectedItem = LanguageManager.Strings.__Language__;
            languages.SelectedIndexChanged += Languages_SelectedIndexChanged;
            
        }
    }
}
