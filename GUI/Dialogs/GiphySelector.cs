using GiphyDotNet.Manager;
using GiphyDotNet.Model.Parameters;
using SuchByte.MacroDeck.GUI.CustomControls;
using SuchByte.MacroDeck.Plugins;
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

        Dictionary<WebClient, string> _webClients = new Dictionary<WebClient, string>();

        public GiphySelector()
        {
            InitializeComponent();
            this.btnTrending.Text = Language.LanguageManager.Strings.Trending;
            this.lblDownloading.Text = Language.LanguageManager.Strings.Downloading;
            this.btnOk.Text = Language.LanguageManager.Strings.Ok;
            btnPreview.Radius = MacroDeck.ProfileManager.CurrentProfile.ButtonRadius;
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
            if (this == null || this.IsDisposed || this.imagesPanel.IsDisposed || this.imagesPanel == null)
            {
                return;
            }

            WebClient client = sender as WebClient;   
            Stream stream = e.Result;

            RoundedButton roundedButton = new RoundedButton();
            roundedButton.Size = new Size(100, 100);
            roundedButton.BackColor = Color.FromArgb(35,35,35);
            roundedButton.BackgroundImage = Image.FromStream(stream);
            roundedButton.BackgroundImageLayout = ImageLayout.Stretch;
            if (roundedButton.BackgroundImage.RawFormat.ToString().ToLower() == "gif")
            {
                roundedButton.ShowGIFIndicator = true;
            }
            roundedButton.Click += ButtonClicked;
            roundedButton.Cursor = Cursors.Hand;
            roundedButton.Radius = MacroDeck.ProfileManager.CurrentProfile.ButtonRadius;
            roundedButton.Tag = _webClients[client];
           
            stream.Flush();
            stream.Close();
            client.OpenReadCompleted -= OnOpenReadGifResultComplete;
            client.Dispose();
            this._webClients.Remove(client);
            
            try
            {
                this.Invoke(new Action(() => {
                    this.imagesPanel.Controls.Add(roundedButton);
                }));
            } catch { }
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
            this.Invoke(new Action(() =>
            {
                this.progressBarDownload.Value = e.ProgressPercentage;
            }));
        }

        private void OnOpenReadComplete(object sender, OpenReadCompletedEventArgs e)
        {
            Cursor.Current = Cursors.Default;
            try
            {
                using (FileStream fs = File.Create(MacroDeck.TempDirectoryPath + "giphy"))
                {
                    e.Result.CopyTo(fs);
                }
                e.Result.Flush();
                e.Result.Close();
                DialogResult = DialogResult.OK;
            } catch { }
           
            this.Close();
        }

        private void BtnOk_Click(object sender, EventArgs e)
        {
            Cursor.Current = Cursors.WaitCursor;
            this.lblDownloading.Visible = true;
            this.progressBarDownload.Visible = true;
            this.btnOk.Enabled = false;
            if (this.btnPreview.Tag != null)
            {
                this.btnOk.Text = "...";
                try
                {
                    WebClient client = new WebClient();
                    client.OpenReadAsync(new Uri(btnPreview.Tag.ToString()));
                    client.DownloadProgressChanged += OnDownloadProgressChanged;
                    client.OpenReadCompleted += OnOpenReadComplete;
                }
                catch { }
            }
        }
    }
}
