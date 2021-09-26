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
    public partial class SetupPage4 : UserControl
    {
        public SetupPage4()
        {
            InitializeComponent();
            this.lblPickAllPlugins.Text = Language.LanguageManager.Strings.InitialSetupPickAllPluginsYouNeed;
            this.lblDontWorry.Text = Language.LanguageManager.Strings.InitialSetupDontWorryInstallUninstallPlugins;
        }

        

        private void LoadAvailablePlugins(bool autoInstall = false)
        {
            this.Invoke(new Action(() => {
                this.plugins.Controls.Clear();
                progressBar.Visible = true;
            }));

            using (WebClient wc = new WebClient())
            {
                var jsonString = wc.DownloadString("https://macrodeck.org/files/packagemanager/plugins.php?target-api=" + MacroDeck.PluginApiVersion);
                JArray jsonArray = JArray.Parse(jsonString);
                foreach (JObject jsonObject in jsonArray)
                {
                    jsonObject["type"] = "plugin";
                    InitialSetupPluginItem initialSetupPluginItem = new InitialSetupPluginItem(jsonObject);

                    this.Invoke(new Action(() =>
                        this.plugins.Controls.Add(initialSetupPluginItem)
                    ));

                    if (autoInstall && (int)jsonObject["auto-install"] != 0)
                    {
                        this.Invoke(new Action(() =>
                            initialSetupPluginItem.SetInstall(true)
                        ));
                    }
                }
                this.Invoke(new Action(() => {
                    progressBar.Visible = false;
                }));
            }
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        public List<JObject> GetObjectsToInstall()
        {
            List<JObject> objectsToInstall = new List<JObject>();
            foreach (InitialSetupPluginItem initialSetupPluginItem in this.plugins.Controls)
            {
                if (initialSetupPluginItem.Install)
                {
                    objectsToInstall.Add(initialSetupPluginItem.JsonObject);
                }
            }
            return objectsToInstall;
        }

        private void SetupPage4_Load(object sender, EventArgs e)
        {
            Task.Run(() =>
            {
                this.LoadAvailablePlugins(true);
            });
        }
    }
}
