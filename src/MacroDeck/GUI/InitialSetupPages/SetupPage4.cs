using System.Net;
using System.Windows.Forms;
using Newtonsoft.Json.Linq;
using SuchByte.MacroDeck.GUI.CustomControls;
using SuchByte.MacroDeck.Language;

namespace SuchByte.MacroDeck.GUI.InitialSetupPages;

public partial class SetupPage4 : UserControl
{
    public SetupPage4()
    {
        InitializeComponent();
        lblPickAllPlugins.Text = LanguageManager.Strings.InitialSetupPickAllPluginsYouNeed;
        lblDontWorry.Text = LanguageManager.Strings.InitialSetupDontWorryInstallUninstallPlugins;
    }

        

    private void LoadAvailablePlugins(bool autoInstall = false)
    {
        Invoke(() => {
            plugins.Controls.Clear();
            progressBar.Visible = true;
        });

        try
        {
            using var wc = new WebClient();
            var jsonString = wc.DownloadString("https://macrodeck.org/files/packagemanager/plugins.php?target-api=" + MacroDeck.PluginApiVersion);
            var jsonArray = JArray.Parse(jsonString);
            foreach (JObject jsonObject in jsonArray)
            {
                jsonObject["type"] = "plugin";
                var initialSetupPluginItem = new InitialSetupPluginItem(jsonObject);

                Invoke(() =>
                    plugins.Controls.Add(initialSetupPluginItem));

                if (autoInstall && (int)jsonObject["auto-install"] != 0)
                {
                    Invoke(() =>
                        initialSetupPluginItem.SetInstall(true));
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
                plugins.Controls.Clear();
                var error = new Label
                {
                    AutoSize = true,
                    Text = LanguageManager.Strings.ErrorWhileLoadingPackageManager
                };
                plugins.Controls.Add(error);
            });
        }
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    public List<JObject> GetObjectsToInstall()
    {
        var objectsToInstall = new List<JObject>();
        foreach (Control initialSetupPluginItem in plugins.Controls)
        {
            if (initialSetupPluginItem.GetType() != typeof(InitialSetupPluginItem)) continue;
            if (((InitialSetupPluginItem)initialSetupPluginItem).Install)
            {
                objectsToInstall.Add(((InitialSetupPluginItem)initialSetupPluginItem).JsonObject);
            }
        }
        return objectsToInstall;
    }

    private void SetupPage4_Load(object sender, EventArgs e)
    {
        Task.Run(() =>
        {
            LoadAvailablePlugins(true);
        });
    }
}