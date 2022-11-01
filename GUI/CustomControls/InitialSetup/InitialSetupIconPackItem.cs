using Newtonsoft.Json.Linq;
using SuchByte.MacroDeck.Utils;

namespace SuchByte.MacroDeck.GUI.CustomControls;

public partial class InitialSetupIconPackItem : RoundedUserControl
{
    private JObject _jsonObject;

    public JObject JsonObject => _jsonObject;

    public bool Install => checkInstall.Checked;

    public void SetInstall(bool install)
    {
        checkInstall.Checked = install;
        checkInstall.Enabled = !install;
    }

    public InitialSetupIconPackItem(JObject jsonObject)
    {
        InitializeComponent();

        _jsonObject = jsonObject;

        lblName.Text = jsonObject["name"].ToString();
        lblDescription.Text = jsonObject["description"].ToString();
        preview.BackgroundImage = Base64.GetImageFromBase64(jsonObject["preview"].ToString());
    }
}