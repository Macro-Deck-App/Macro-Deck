using System.Windows.Forms;
using SuchByte.MacroDeck.GUI.CustomControls;
using SuchByte.MacroDeck.Icons;
using SuchByte.MacroDeck.Language;

namespace SuchByte.MacroDeck.GUI.Dialogs;

public partial class ExportIconPackDialog : DialogForm
{

    public IconPack IconPack;

    public ExportIconPackDialog(IconPack iconPack)
    {
        InitializeComponent();
        lblVersion.Text = LanguageManager.Strings.Version;
        btnOk.Text = LanguageManager.Strings.Ok;
        IconPack = iconPack;
    }

    private void ExportIconPackDialog_Load(object sender, EventArgs e)
    {
        version.Text = IconPack.Version;
    }

    private void BtnOk_Click(object sender, EventArgs e)
    {
        if (version.Text.Length < 2) return;

        IconPack.Version = version.Text;

        DialogResult = DialogResult.OK;
    }
}