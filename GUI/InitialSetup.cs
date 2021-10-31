using Newtonsoft.Json.Linq;
using SuchByte.MacroDeck.GUI.Dialogs;
using SuchByte.MacroDeck.GUI.InitialSetupPages;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Net;
using System.Text;
using System.Windows.Forms;

namespace SuchByte.MacroDeck.GUI
{
    public partial class InitialSetup : GUI.CustomControls.DialogForm
    {

        public Configuration.Configuration configuration;

        //public int Rows = 3, Columns = 5;

        int currentPage = 0;
        List<Control> pages;

        List<JObject> objectsToInstall = new List<JObject>();


        public InitialSetup()
        {
            InitializeComponent();

            this.UpdateLanguage();

            this.LoadSetupPages();

            this.configuration = new Configuration.Configuration
            {
                AutoUpdates = true,
                Host_Address = "0.0.0.0",
                Host_Port = 8191,
            };

            Language.LanguageManager.LanguageChanged += OnLanguageChanged;
        }


        private void LoadSetupPages()
        {
            if (this.pages != null)
            {
                this.pages.Clear();
            }
            this.pages = new List<Control>
            {
                new SetupPage1(),
                new SetupPage2(this),
                //new SetupPage3(this),
                new SetupPage4(),
                new SetupPage5(),
                new SetupPage6(this),
            };
            this.SetPage(0);
        }

        private void OnLanguageChanged(object sender, EventArgs e)
        {
            this.configuration.Language = ((Language.Strings)sender).__Language__;
            this.UpdateLanguage();
            this.LoadSetupPages();
        }

        private void UpdateLanguage()
        {
            this.btnBack.Text = Language.LanguageManager.Strings.InitialSetupButtonBack;
            this.btnNext.Text = Language.LanguageManager.Strings.InitialSetupButtonNext;
        }

        private void BtnNext_Click(object sender, EventArgs e)
        {
            // Get all the selected plugins for installation
            if (this.currentPage == pages.IndexOf(pages.OfType<SetupPage4>().FirstOrDefault()))
            {
                SetupPage4 setupPage4 = pages.OfType<SetupPage4>().FirstOrDefault();
                this.objectsToInstall = setupPage4.GetObjectsToInstall();
            } else if (this.currentPage == pages.IndexOf(pages.OfType<SetupPage5>().FirstOrDefault()))
            {
                SetupPage5 setupPage5 = pages.OfType<SetupPage5>().FirstOrDefault();
                this.objectsToInstall.AddRange(setupPage5.GetObjectsToInstall());
            }

            // Next page
            if (this.currentPage == this.pages.Count - 1)
            {
                if (this.objectsToInstall.Count == 0)
                {
                    this.DialogResult = DialogResult.OK;
                    this.Close();
                } else
                {
                    using (var pluginDownloader = new PluginDownloader())
                    {
                        pluginDownloader.InstallAll(this.objectsToInstall);
                        if (pluginDownloader.ShowDialog() == DialogResult.OK)
                        {
                            this.DialogResult = DialogResult.OK;
                            this.Close();
                        }

                    }
                }
                
            } else
            {
                this.currentPage++;
                if (this.currentPage > this.pages.Count - 1)
                {
                    this.currentPage = this.pages.Count - 1;
                }
                this.SetPage(currentPage);
            }
            
        }

        private void BtnBack_Click(object sender, EventArgs e)
        {
            this.currentPage--;
            if (this.currentPage < 0)
            {
                this.currentPage = 0;
            }
            this.SetPage(currentPage);
        }


        void SetPage(int page)
        {
            this.pagePanel.Controls.Clear();
            if (page <= this.pages.Count - 1 && page >= 0)
            {
                this.pagePanel.Controls.Add(this.pages[page]);
            }
            if (page == 0)
            {
                this.btnBack.Visible = false;
            } else
            {
                this.btnBack.Visible = true;
            }
            if (page == this.pages.Count - 1)
            {
                this.btnNext.Text = Language.LanguageManager.Strings.InitialSetupButtonFinish;
            } else
            {
                this.btnNext.Text = Language.LanguageManager.Strings.InitialSetupButtonNext;
            }
            this.lblPage.Text = String.Format(Language.LanguageManager.Strings.InitialSetupPage, this.currentPage + 1, this.pages.Count);
        }

        private void InitialSetup_Load(object sender, EventArgs e)
        {
            
        }
    }
}
