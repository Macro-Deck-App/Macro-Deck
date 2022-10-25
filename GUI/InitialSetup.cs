using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Forms;
using SuchByte.MacroDeck.GUI.CustomControls;
using SuchByte.MacroDeck.GUI.InitialSetupPages;
using SuchByte.MacroDeck.Language;
using SuchByte.MacroDeck.Logging;

namespace SuchByte.MacroDeck.GUI
{
    public partial class InitialSetup : DialogForm
    {

        public Configuration.Configuration configuration;

        int currentPage;
        List<Control> pages;

        //List<JObject> objectsToInstall = new List<JObject>();


        public InitialSetup()
        {
            InitializeComponent();

            UpdateLanguage();

            LoadSetupPages();

            configuration = new Configuration.Configuration
            {
                AutoUpdates = true,
                Host_Address = "0.0.0.0",
                Host_Port = 8191,
            };

            LanguageManager.LanguageChanged += OnLanguageChanged;
            SetSystemLanguage();
        }


        private void LoadSetupPages()
        {
            pages?.Clear();
            pages = new List<Control>
            {
                new SetupPage1(),
                new SetupPage2(this),
                //new SetupPage3(this),
                //new SetupPage4(),
                //new SetupPage5(),
                new SetupPage6(this),
            };
            SetPage(0);
        }

        private void OnLanguageChanged(object sender, EventArgs e)
        {
            configuration.Language = ((Strings)sender).__Language__;
            UpdateLanguage();
            LoadSetupPages();
        }

        private void UpdateLanguage()
        {
            btnBack.Text = LanguageManager.Strings.InitialSetupButtonBack;
            btnNext.Text = LanguageManager.Strings.InitialSetupButtonNext;
        }

        private void BtnNext_Click(object sender, EventArgs e)
        {
            // Get all the selected plugins for installation
            /*if (this.currentPage == pages.IndexOf(pages.OfType<SetupPage4>().FirstOrDefault()))
            {
                SetupPage4 setupPage4 = pages.OfType<SetupPage4>().FirstOrDefault();
                this.objectsToInstall = setupPage4.GetObjectsToInstall();
            } else if (this.currentPage == pages.IndexOf(pages.OfType<SetupPage5>().FirstOrDefault()))
            {
                SetupPage5 setupPage5 = pages.OfType<SetupPage5>().FirstOrDefault();
                this.objectsToInstall.AddRange(setupPage5.GetObjectsToInstall());
            }*/

            // Next page
            if (currentPage == pages.Count - 1)
            {
                DialogResult = DialogResult.OK;
                Close();
                /*if (this.objectsToInstall.Count == 0)
                {
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
                }*/

            } else
            {
                currentPage++;
                if (currentPage > pages.Count - 1)
                {
                    currentPage = pages.Count - 1;
                }
                SetPage(currentPage);
            }
            
        }

        private void BtnBack_Click(object sender, EventArgs e)
        {
            currentPage--;
            if (currentPage < 0)
            {
                currentPage = 0;
            }
            SetPage(currentPage);
        }


        void SetPage(int page)
        {
            pagePanel.Controls.Clear();
            if (page <= pages.Count - 1 && page >= 0)
            {
                pagePanel.Controls.Add(pages[page]);
            }
            if (page == 0)
            {
                btnBack.Visible = false;
            } else
            {
                btnBack.Visible = true;
            }
            if (page == pages.Count - 1)
            {
                btnNext.Text = LanguageManager.Strings.InitialSetupButtonFinish;
            } else
            {
                btnNext.Text = LanguageManager.Strings.InitialSetupButtonNext;
            }
            lblPage.Text = string.Format(LanguageManager.Strings.InitialSetupPage, currentPage + 1, pages.Count);
        }

        private void SetSystemLanguage()
        {
            try
            {
                var cultureInfo = CultureInfo.InstalledUICulture;
                var languageIso = cultureInfo.TwoLetterISOLanguageName;
                var language = LanguageManager.Languages.Find(l => l.__LanguageCode__.ToLower().Equals(languageIso.ToLower()));
                if (language != null)
                {
                    LanguageManager.SetLanguage(language);
                }

            }
            catch (Exception ex)
            {
                MacroDeckLogger.Warning("Not able to set system language: " + ex.Message + Environment.NewLine + ex.StackTrace);
            }
        }
        private void InitialSetup_Load(object sender, EventArgs e)
        {
            
        }
    }
}
