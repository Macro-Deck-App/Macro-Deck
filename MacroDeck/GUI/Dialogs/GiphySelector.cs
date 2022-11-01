using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using System.Windows.Forms;
using GiphyDotNet.Manager;
using GiphyDotNet.Model.Parameters;
using GiphyDotNet.Model.Results;
using SuchByte.MacroDeck.GUI.CustomControls;
using SuchByte.MacroDeck.Language;
using SuchByte.MacroDeck.Logging;
using SuchByte.MacroDeck.Profiles;

namespace SuchByte.MacroDeck.GUI.Dialogs;

public partial class GiphySelector : DialogForm
{
    Giphy giphy;
    int limit = 100;

    public MemoryStream DownloadedGifStream;

    Dictionary<WebClient, string> _webClients = new();

    public GiphySelector()
    {
        InitializeComponent();
        btnTrending.Text = LanguageManager.Strings.Trending;
        lblDownloading.Text = LanguageManager.Strings.Downloading;
        btnOk.Text = LanguageManager.Strings.Ok;
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
            var gifResult = await giphy.TrendingGifs(new TrendingParameter
            {
                Limit = limit
            });

            Invoke(() =>
            {
                try
                {
                    foreach (RoundedButton roundedButton in imagesPanel.Controls)
                    {
                        roundedButton.Click -= ButtonClicked;
                        roundedButton.Dispose();
                    }
                    imagesPanel.Controls.Clear();
                } catch { }
            });

            AddGifs(gifResult);
        } catch { }
    }

    private async void SearchAsync(string query)
    {
        try
        {
            if (query.Length < 1) return;
            var searchParameter = new SearchParameter
            {
                Query = searchBox.Text,
                Limit = limit,
            };

            var gifResult = await giphy.GifSearch(searchParameter);

            Invoke(() =>
            {
                try
                {
                    foreach (RoundedButton roundedButton in imagesPanel.Controls)
                    {
                        roundedButton.Click -= ButtonClicked;
                        roundedButton.Dispose();
                    }
                    imagesPanel.Controls.Clear();
                } catch { }
            });

            AddGifs(gifResult);
        } catch { }
    }

    private void AddGifs(GiphySearchResult gifResult)
    {
        if (IsDisposed) return;
        for (var i = 0; i < gifResult.Data.Length; i++)
        {
            var client = new WebClient();
            client.OpenReadCompleted += OnOpenReadGifResultComplete;
            client.OpenReadAsync(new Uri(gifResult.Data[i].Images.FixedWidthDownsampled.Url));
            _webClients.Add(client, gifResult.Data[i].Images.Original.Url);
        }
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private void OnOpenReadGifResultComplete(object sender, OpenReadCompletedEventArgs e)
    {
        var client = sender as WebClient;   

        Image image = null;

        using (var stream = e.Result)
        {
            image = Image.FromStream(stream);
        }

        if (IsDisposed)
        {
            return;
        }
        Invoke(() => {
            var roundedButton = new RoundedButton
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
            _webClients.Remove(client);
            
            imagesPanel.Controls.Add(roundedButton);
        });
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

        

    private void ButtonClicked(object sender, EventArgs e)
    {
        var selected = (RoundedButton)sender;
        btnPreview.BackgroundImage = selected.Image;
        btnPreview.Tag = selected.Tag;
    }

    private void OnDownloadProgressChanged(object sender, DownloadProgressChangedEventArgs e)
    {
        if (IsDisposed) return;
        Invoke(() =>
        {
            btnOk.Progress = e.ProgressPercentage;
        });
    }

    private void OnOpenReadComplete(object sender, OpenReadCompletedEventArgs e)
    {
        if (IsDisposed) return;
        Cursor.Current = Cursors.Default;
        try
        {
            DownloadedGifStream = new MemoryStream();
            e.Result.CopyTo(DownloadedGifStream);
            /*using (FileStream fs = File.Create(Path.Combine(MacroDeck.ApplicationPaths.TempDirectoryPath, "giphy")))
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

        Close();
    }

    private void BtnOk_Click(object sender, EventArgs e)
    {
        Task.Run(() => SpinnerDialog.SetVisisble(true, this));
        lblDownloading.Visible = true;
        btnOk.Enabled = false;
        if (btnPreview.Tag != null)
        {
            MacroDeckLogger.Info("Downloading GIPHY icon...");
            try
            {
                var client = new WebClient();
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