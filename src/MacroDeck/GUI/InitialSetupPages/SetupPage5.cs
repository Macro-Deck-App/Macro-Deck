using System.Net;
using System.Windows.Forms;
using Newtonsoft.Json.Linq;
using SuchByte.MacroDeck.GUI.CustomControls;
using SuchByte.MacroDeck.Language;

namespace SuchByte.MacroDeck.GUI.InitialSetupPages;

public partial class SetupPage5 : UserControl
{
    public SetupPage5()
    {
        InitializeComponent();
        lblWantIcons.Text = LanguageManager.Strings.InitialSetupWantSomeIcons;
        lblInstallLaterPackageManager.Text = LanguageManager.Strings.InitialSetupInstallIconPacksPackageManager;
    }

    private void LoadAvailableIconPacks(bool autoInstall = false)
    {
        Invoke(() => {
            iconPacks.Controls.Clear();
            progressBar.Visible = true;
        });

        try
        {
            using var wc = new WebClient();
            var jsonString = wc.DownloadString("https://macrodeck.org/files/packagemanager/iconpacks.php?target-api=" + MacroDeck.PluginApiVersion);
            var jsonArray = JArray.Parse(jsonString);
            foreach (JObject jsonObject in jsonArray)
            {
                jsonObject["type"] = "icon-pack";
                var initialSetupIconPackItem = new InitialSetupIconPackItem(jsonObject);

                Invoke(() =>
                    iconPacks.Controls.Add(initialSetupIconPackItem));

                if (autoInstall && (int)jsonObject["auto-install"] != 0)
                {
                    Invoke(() =>
                        initialSetupIconPackItem.SetInstall(true));
                }
            }
            Invoke(() =>
            {
                progressBar.Visible = false;
            });
        } catch
        {
            Invoke(() =>
            {
                progressBar.Visible = false;
                iconPacks.Controls.Clear();
                var error = new Label
                {
                    AutoSize = true,
                    Text = LanguageManager.Strings.ErrorWhileLoadingPackageManager
                };
                iconPacks.Controls.Add(error);
            });
              
        }
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    public List<JObject> GetObjectsToInstall()
    {
        var objectsToInstall = new List<JObject>();
        foreach (Control initialSetupIconPackItem in iconPacks.Controls)
        {
            if (initialSetupIconPackItem.GetType() != typeof(InitialSetupIconPackItem)) continue;
            if (((InitialSetupIconPackItem)initialSetupIconPackItem).Install)
            {
                objectsToInstall.Add(((InitialSetupIconPackItem)initialSetupIconPackItem).JsonObject);
            }
        }
        return objectsToInstall;
    }

    private void SetupPage5_Load(object sender, EventArgs e)
    {
        Task.Run(() =>
        {
            LoadAvailableIconPacks(true);
        });
    }
}