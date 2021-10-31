using Newtonsoft.Json.Linq;
using SuchByte.MacroDeck.GUI.CustomControls;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SuchByte.MacroDeck.GUI.InitialSetupPages
{
    public partial class SetupPage5 : UserControl
    {
        public SetupPage5()
        {
            InitializeComponent();
            this.lblWantIcons.Text = Language.LanguageManager.Strings.InitialSetupWantSomeIcons;
            this.lblInstallLaterPackageManager.Text = Language.LanguageManager.Strings.InitialSetupInstallIconPacksPackageManager;
        }

        private void LoadAvailableIconPacks(bool autoInstall = false)
        {
            this.Invoke(new Action(() => {
                this.iconPacks.Controls.Clear();
                progressBar.Visible = true;
            }));

            try
            {
                using (WebClient wc = new WebClient())
                {
                    var jsonString = wc.DownloadString("https://macrodeck.org/files/packagemanager/iconpacks.php?target-api=" + MacroDeck.PluginApiVersion);
                    JArray jsonArray = JArray.Parse(jsonString);
                    foreach (JObject jsonObject in jsonArray)
                    {
                        jsonObject["type"] = "icon-pack";
                        InitialSetupIconPackItem initialSetupIconPackItem = new InitialSetupIconPackItem(jsonObject);

                        this.Invoke(new Action(() =>
                            this.iconPacks.Controls.Add(initialSetupIconPackItem)
                        ));

                        if (autoInstall && (int)jsonObject["auto-install"] != 0)
                        {
                            this.Invoke(new Action(() =>
                                initialSetupIconPackItem.SetInstall(true)
                            ));
                        }
                    }
                    this.Invoke(new Action(() =>
                    {
                        progressBar.Visible = false;
                    }));
                }
            } catch
            {
                this.Invoke(new Action(() =>
                {
                    this.progressBar.Visible = false;
                    this.iconPacks.Controls.Clear();
                    Label error = new Label
                    {
                        AutoSize = true,
                        Text = Language.LanguageManager.Strings.ErrorWhileLoadingPackageManager
                    };
                    this.iconPacks.Controls.Add(error);
                }));
              
            }
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        public List<JObject> GetObjectsToInstall()
        {
            List<JObject> objectsToInstall = new List<JObject>();
            foreach (Control initialSetupIconPackItem in this.iconPacks.Controls)
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
                this.LoadAvailableIconPacks(true);
            });
        }
    }
}
