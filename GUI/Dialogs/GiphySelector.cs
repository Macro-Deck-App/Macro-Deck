using GiphyDotNet.Manager;
using GiphyDotNet.Model.Parameters;
using SuchByte.MacroDeck.GUI.CustomControls;
using SuchByte.MacroDeck.Logging;
using SuchByte.MacroDeck.Plugins;
using SuchByte.MacroDeck.Profiles;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SuchByte.MacroDeck.GUI.Dialogs
{
    public partial class GiphySelector : DialogForm
    {
        Giphy giphy;
        int limit = 100;

        public MemoryStream DownloadedGifStream;

        Dictionary<WebClient, string> _webClients = new Dictionary<WebClient, string>();

        public GiphySelector()
        {
            InitializeComponent();
            this.btnTrending.Text = Language.LanguageManager.Strings.Trending;
            this.lblDownloading.Text = Language.LanguageManager.Strings.Downloading;
            this.btnOk.Text = Language.LanguageManager.Strings.Ok;
            btnPreview.Radius = ProfileManager.CurrentProfile.ButtonRadius;
            giphy = new Giphy(Credentials.Credentials.GiphyToken);
        }

        private void GiphySelector_Load(object sender, EventArgs e)
        {
            Task.Run(() =>
            {
                LoadTrendingAsync();
            });
        }

        private void BtnSearch_Click(object sender, EventArgs e)
        {
            Task.Run(() =>
            {
                SearchAsync(searchBox.Text);
            });
        }

        private void BtnTrending_Click(object sender, EventArgs e)
        {
            Task.Run(() =>
            {
                LoadTrendingAsync();
            });
        }

        private async void LoadTrendingAsync()
        {
            try
            {
                var gifResult = await giphy.TrendingGifs(new TrendingParameter()
                {
                    Limit = this.limit
                });

                this.Invoke(new Action(() =>
                {
                    try
                    {
                        foreach (RoundedButton roundedButton in imagesPanel.Controls)
                        {
                            roundedButton.Click -= ButtonClicked;
                            roundedButton.Dispose();
                        }
                        this.imagesPanel.Controls.Clear();
                    } catch { }
                }));

                AddGifs(gifResult);
            } catch { }
        }

        private async void SearchAsync(string query)
        {
            try
            {
                if (query.Length < 1) return;
                var searchParameter = new SearchParameter()
                {
                    Query = searchBox.Text,
                    Limit = this.limit,
                };

                var gifResult = await giphy.GifSearch(searchParameter);

                this.Invoke(new Action(() =>
                {
                    try
                    {
                        foreach (RoundedButton roundedButton in imagesPanel.Controls)
                        {
                            roundedButton.Click -= ButtonClicked;
                            roundedButton.Dispose();
                        }
                        this.imagesPanel.Controls.Clear();
                    } catch { }
                }));

                AddGifs(gifResult);
            } catch { }
        }

        private void AddGifs(GiphyDotNet.Model.Results.GiphySearchResult gifResult)
        {
            if (this.IsDisposed) return;
            for (int i = 0; i < gifResult.Data.Length; i++)
            {
                WebClient client = new WebClient();
                client.OpenReadCompleted += OnOpenReadGifResultComplete;
                client.OpenReadAsync(new Uri(gifResult.Data[i].Images.FixedWidthDownsampled.Url));
                this._webClients.Add(client, (string)gifResult.Data[i].Images.Original.Url.ToString());
            }
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        private void OnOpenReadGifResultComplete(object sender, OpenReadCompletedEventArgs e)
        {
            WebClient client = sender as WebClient;   

            Image image = null;

            using (var stream = e.Result)
            {
                image = Image.FromStream(stream);
            }

            if (this.IsDisposed)
            {
                return;
            }
            this.Invoke(new Action(() => {
                RoundedButton roundedButton = new RoundedButton
                {
                    Size = new Size(100, 100),
                    BackColor = Color.FromArgb(35, 35, 35),
                    BackgroundImage = image,
                    BackgroundImageLayout = ImageLayout.Stretch
                };
                if (roundedButton.BackgroundImage.RawFormat.ToString().ToLower() == "gif")
                {
                    roundedButton.ShowGIFIndicator = true;
                }
                roundedButton.Click += ButtonClicked;
                roundedButton.Cursor = Cursors.Hand;
                roundedButton.Radius = ProfileManager.CurrentProfile.ButtonRadius;
                roundedButton.Tag = _webClients[client];
            
                client.OpenReadCompleted -= OnOpenReadGifResultComplete;
                client.Dispose();
                this._webClients.Remove(client);
            
                this.imagesPanel.Controls.Add(roundedButton);
            }));
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        

        private void ButtonClicked(object sender, EventArgs e)
        {
            RoundedButton selected = (RoundedButton)sender;
            this.btnPreview.BackgroundImage = selected.Image;
            this.btnPreview.Tag = selected.Tag;
        }

        private void OnDownloadProgressChanged(object sender, DownloadProgressChangedEventArgs e)
        {
            if (this.IsDisposed) return;
            this.Invoke(new Action(() =>
            {
                this.btnOk.Progress = e.ProgressPercentage;
            }));
        }

        private void OnOpenReadComplete(object sender, OpenReadCompletedEventArgs e)
        {
            if (this.IsDisposed) return;
            Cursor.Current = Cursors.Default;
            try
            {
                this.DownloadedGifStream = new MemoryStream();
                e.Result.CopyTo(DownloadedGifStream);
                /*using (FileStream fs = File.Create(Path.Combine(MacroDeck.TempDirectoryPath, "giphy")))
                {
                    e.Result.CopyTo(fs);
                }*/
                e.Result.Flush();
                e.Result.Close();
                Task.Run(() => SpinnerDialog.SetVisisble(false, this));
                DialogResult = DialogResult.OK;
                MacroDeckLogger.Info("Successfully downloaded GIPHY icon");
            }
            catch (Exception ex)
            {
                MacroDeckLogger.Error("Error while opening GIPHY icon: " + ex.Message + Environment.NewLine + ex.StackTrace);
            }

            this.Close();
        }

        private void BtnOk_Click(object sender, EventArgs e)
        {
            Task.Run(() => SpinnerDialog.SetVisisble(true, this));
            this.lblDownloading.Visible = true;
            this.btnOk.Enabled = false;
            if (this.btnPreview.Tag != null)
            {
                MacroDeckLogger.Info("Downloading GIPHY icon...");
                try
                {
                    WebClient client = new WebClient();
                    client.OpenReadAsync(new Uri(btnPreview.Tag.ToString()));
                    client.DownloadProgressChanged += OnDownloadProgressChanged;
                    client.OpenReadCompleted += OnOpenReadComplete;
                }
                catch (Exception ex)
                {
                    MacroDeckLogger.Error("Error while downloading GIPHY icon: " + ex.Message + Environment.NewLine + ex.StackTrace);
                }
            }
        }
    }
}
