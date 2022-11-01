using System;
using System.Windows.Forms;
using SuchByte.MacroDeck.Language;

namespace SuchByte.MacroDeck.GUI.InitialSetupPages;

public partial class SetupPage3 : UserControl
{

    InitialSetup initialSetup;
    public SetupPage3(InitialSetup initialSetup)
    {
        InitializeComponent();
        this.initialSetup = initialSetup;
        lblConfigureGridPreferences.Text = LanguageManager.Strings.InitialSetupConfigureGridPreferences;
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