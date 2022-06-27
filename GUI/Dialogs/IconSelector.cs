using ImageMagick;
using Newtonsoft.Json;
using SQLite;
using SuchByte.MacroDeck.GUI.CustomControls;
using SuchByte.MacroDeck.GUI.Dialogs;
using SuchByte.MacroDeck.Icons;
using SuchByte.MacroDeck.JSON;
using SuchByte.MacroDeck.Logging;
using SuchByte.MacroDeck.Plugins;
using SuchByte.MacroDeck.Profiles;
using SuchByte.MacroDeck.Server;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;

namespace SuchByte.MacroDeck.GUI
{
    public partial class IconSelector : DialogForm
    {
        public Icons.Icon SelectedIcon;
        public Icons.IconPack SelectedIconPack;

        private IconPack _selectIconPack;

        public IconSelector(IconPack iconPack = null)
        {
            this._selectIconPack = iconPack;
            InitializeComponent();
            this.btnImportIconPack.Text = Language.LanguageManager.Strings.ImportIconPack;
            this.btnExportIconPack.Text = Language.LanguageManager.Strings.ExportIconPack;
            this.btnCreateIcon.Text = Language.LanguageManager.Strings.CreateIcon;
            this.btnImport.Text = Language.LanguageManager.Strings.ImportIcon;
            this.btnDeleteIcon.Text = Language.LanguageManager.Strings.DeleteIcon;
            this.lblManaged.Text = Language.LanguageManager.Strings.IconSelectorManagedInfo;
            this.lblSizeLabel.Text = Language.LanguageManager.Strings.Size;
            this.lblTypeLabel.Text = Language.LanguageManager.Strings.Type;
            this.btnPreview.Radius = ProfileManager.CurrentProfile.ButtonRadius;
            this.btnGenerateStatic.Text = Language.LanguageManager.Strings.GenerateStatic;
        }

        private void LoadIcons(IconPack iconPack, bool scrollDown = false)
        {
            this.iconList.Controls.Clear();
            this.SuspendLayout();
            foreach (Icons.Icon icon in iconPack.Icons)
            {
                RoundedButton button = new RoundedButton
                {
                    Width = 100,
                    Height = 100,
                    BackColor = Color.FromArgb(35,35,35),
                    Radius = ProfileManager.CurrentProfile.ButtonRadius,
                    BackgroundImageLayout = ImageLayout.Stretch,
                    BackgroundImage = icon.IconImage
                };
                if (button.BackgroundImage.RawFormat.ToString().ToLower() == "gif")
                {
                    button.ShowGIFIndicator = true;
                }
                button.Tag = icon;
                button.Click += new EventHandler(this.IconButton_Click);
                button.Cursor = Cursors.Hand;
                this.iconList.Controls.Add(button);
            }
            this.ResumeLayout();
            if (scrollDown)
            {
                this.iconList.ScrollControlIntoView(this.iconList.Controls[this.iconList.Controls.Count - 1]);
            }
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        private void IconButton_Click(object sender, EventArgs e)
        {
            RoundedButton button = (RoundedButton)sender;
            Icons.Icon icon = button.Tag as Icons.Icon;
            this.SelectIcon(icon);
        }

        private void SelectIcon(Icons.Icon icon)
        {
            Image previewImage = icon.IconImage;

            this.btnGenerateStatic.Visible = icon.IconImage.RawFormat.ToString().ToLower() == "gif";

            btnPreview.BackgroundImage = previewImage;
            lblSize.Text = string.Format("{0:n0}", ASCIIEncoding.Unicode.GetByteCount(icon.IconBase64) / 1000) + " kByte";
            lblType.Text = previewImage.RawFormat.ToString().ToUpper();

            this.SelectedIconPack = IconManager.GetIconPackByName(this.iconPacksBox.Text);
            this.SelectedIcon = icon;
        }

        private void BtnImport_Click(object sender, EventArgs e)
        {
            using (var openFileDialog = new OpenFileDialog
            {
                Title = "Import icon",
                CheckFileExists = true,
                CheckPathExists = true,
                DefaultExt = "png",
                Multiselect = true,
                Filter = "Image files (*.png;*.jpg;*.bmp;*.gif)|*.png;*.jpg;*.bmp;*.gif"
            })
            {
                if (openFileDialog.ShowDialog() == DialogResult.OK)
                {
                    IconPack iconPack = IconManager.GetIconPackByName(this.iconPacksBox.Text);
                    bool gif = (Path.GetExtension(openFileDialog.FileNames.FirstOrDefault()).ToLower() == "gif");
                    using (var iconImportQuality = new IconImportQuality(gif))
                    {
                        if (iconImportQuality.ShowDialog() == DialogResult.OK)
                        {
                            Task.Run(() => SpinnerDialog.SetVisisble(true, this));
                            List<Image> icons = new List<Image>();

                            Task.Run(() =>
                            {
                                MacroDeckLogger.Info(GetType(), string.Format("Starting importing {0} icon(s)", openFileDialog.FileNames.Length));
                                if (iconImportQuality.Pixels == -1)
                                {
                                    foreach (var file in openFileDialog.FileNames)
                                    {
                                        try
                                        {
                                            icons.Add(Image.FromFile(file));
                                            MacroDeckLogger.Trace(GetType(), "Original image loaded");
                                        }
                                        catch (Exception ex)
                                        {
                                            MacroDeckLogger.Error(GetType(), "Error while loading original image: " + ex.Message + Environment.NewLine + ex.StackTrace);
                                        }
                                    }
                                }
                                else
                                {
                                    foreach (var file in openFileDialog.FileNames)
                                    {
                                        try
                                        {
                                            MacroDeckLogger.Trace(GetType(), "Using Magick to resize image");
                                            using (var collection = new MagickImageCollection(new FileInfo(file)))
                                            {
                                                collection.Coalesce();
                                                foreach (var image in collection)
                                                {
                                                    image.Resize(iconImportQuality.Pixels, iconImportQuality.Pixels);
                                                    image.Crop(iconImportQuality.Pixels, iconImportQuality.Pixels);
                                                }
                                                
                                                using (var ms = new MemoryStream())
                                                {
                                                    collection.Write(ms);
                                                    icons.Add(Image.FromStream(ms));
                                                }
                                            }
                                            MacroDeckLogger.Trace(GetType(), "Image successfully resized");
                                        }
                                        catch (Exception ex) 
                                        {
                                            MacroDeckLogger.Error(GetType(), "Failed to resize image: " + ex.Message + Environment.NewLine + ex.StackTrace);
                                        }
                                    }
                                }

                                if (iconPack == null)
                                {
                                    MacroDeckLogger.Error(GetType(), "Icon pack was null");
                                    SpinnerDialog.SetVisisble(false, this);
                                    return;
                                }
                                MacroDeckLogger.Info(GetType(), string.Format("Adding {0} icons to {1}", icons.Count, iconPack.Name));
                                List<Image> gifIcons = new List<Image>();
                                gifIcons.AddRange(icons.FindAll(x => x.RawFormat.ToString().ToLower() == "gif").ToArray());
                                bool convertGifToStatic = false;
                                if (gifIcons.Count > 0)
                                {
                                    this.Invoke(new Action(() =>
                                    {
                                        using (var msgBox = new CustomControls.MessageBox())
                                        {
                                            convertGifToStatic = msgBox.ShowDialog(Language.LanguageManager.Strings.AnimatedGifImported, Language.LanguageManager.Strings.GenerateStaticIcon, MessageBoxButtons.YesNo) == DialogResult.Yes;
                                        }
                                    }));
                                    MacroDeckLogger.Info(GetType(), "Convert gif to static? " + convertGifToStatic);
                                }


                                foreach (Image icon in icons)
                                {
                                    IconManager.AddIconImage(iconPack, icon);
                                    if (convertGifToStatic)
                                    {
                                        using (var ms = new MemoryStream())
                                        {
                                            icon.Save(ms, ImageFormat.Png);
                                            Image iconStatic = Image.FromStream(ms);
                                            IconManager.AddIconImage(iconPack, iconStatic);
                                        }
                                    }
                                    icon.Dispose();
                                    GC.Collect();
                                    GC.WaitForPendingFinalizers();
                                    GC.Collect();
                                }

                                this.Invoke(new Action(() => this.LoadIcons(iconPack, true)));
                                MacroDeckLogger.Info(GetType(), "Icons successfully imported");
                                SpinnerDialog.SetVisisble(false, this);
                            });
                        }
                        
                    }
                }
            }
        }

        private void BtnGiphy_Click(object sender, EventArgs e)
        {
            using (var giphySelector = new GiphySelector())
            {
                if (giphySelector.ShowDialog() == DialogResult.OK)
                {
                    IconPack iconPack = IconManager.GetIconPackByName(this.iconPacksBox.Text);
                    using (var iconImportQuality = new IconImportQuality(true))
                    {
                        if (iconImportQuality.ShowDialog() == DialogResult.OK)
                        {
                            Image icon = null;
                            if (giphySelector.DownloadedGifStream == null) return;
                            if (iconImportQuality.Pixels == -1)
                            {
                                icon = Image.FromStream(giphySelector.DownloadedGifStream);
                                MacroDeckLogger.Trace("Original GIPHY image loaded");
                            }
                            else
                            {
                                MacroDeckLogger.Trace("Using Magick to resize GIPHY image");
                                giphySelector.DownloadedGifStream.Seek(0, SeekOrigin.Begin);
                                using (var collection = new MagickImageCollection(giphySelector.DownloadedGifStream))
                                {
                                    collection.Coalesce();
                                    foreach (var image in collection)
                                    {
                                        image.Resize(iconImportQuality.Pixels, iconImportQuality.Pixels);
                                        image.Crop(iconImportQuality.Pixels, iconImportQuality.Pixels);
                                    }
                                    try
                                    {
                                        using (var ms = new MemoryStream())
                                        {
                                            collection.Write(ms);
                                            icon = Image.FromStream(ms);
                                        }
                                        MacroDeckLogger.Trace("GIPHY Image successfully resized");
                                    }
                                    catch (Exception ex)
                                    {
                                        MacroDeckLogger.Error("Failed to resize GIPHY image: " + ex.Message + Environment.NewLine + ex.StackTrace);
                                    }
                                }
                            }
                            IconManager.AddIconImage(iconPack, icon);
                            using (var msgBox = new CustomControls.MessageBox())
                            {
                                if (msgBox.ShowDialog(Language.LanguageManager.Strings.AnimatedGifImported, Language.LanguageManager.Strings.GenerateStaticIcon, MessageBoxButtons.YesNo) == DialogResult.Yes)
                                {
                                    if (icon.RawFormat.ToString().ToLower() == "gif")
                                    {
                                        using (var ms = new MemoryStream())
                                        {
                                            icon.Save(ms, ImageFormat.Png);
                                            Image iconStatic = Image.FromStream(ms);
                                            IconManager.AddIconImage(iconPack, iconStatic);
                                        }
                                    }
                                }
                            }
                            icon.Dispose();
                            giphySelector.DownloadedGifStream.Close();
                            giphySelector.DownloadedGifStream.Dispose();

                            this.Invoke(new Action(() => this.LoadIcons(iconPack, true)));
                            MacroDeckLogger.Info("GIPHY icon successfully imported");
                        }
                    }
                }
            }
        }

        private void BtnOk_Click(object sender, EventArgs e)
        {
            if (this.SelectedIcon != null)
            {
                this.DialogResult = DialogResult.OK;
            }
            this.Close();
        }

        private void IconSelector_Load(object sender, EventArgs e)
        {
            Task.Run(() =>
            {
                this.LoadIconPacks();
            });
        }

        private void LoadIconPacks()
        {
            this.Invoke(new Action(() => this.iconPacksBox.Items.Clear()));
            foreach (IconPack iconPack in IconManager.IconPacks)
            {
                this.Invoke(new Action(() => this.iconPacksBox.Items.Add(iconPack.Name)));
            }
            if (Properties.Settings.Default.IconSelectorLastIconPack != null)
            {
                this.Invoke(new Action(() =>
                {
                    if (this._selectIconPack != null)
                    {
                        if (this.iconPacksBox.Items.Contains(this._selectIconPack.Name))
                        {
                            this.iconPacksBox.SelectedIndex = this.iconPacksBox.FindStringExact(this._selectIconPack.Name);
                            return;
                        }
                    }
                    if (this.iconPacksBox.Items.Contains(Properties.Settings.Default.IconSelectorLastIconPack))
                    {
                        this.iconPacksBox.SelectedIndex = this.iconPacksBox.FindStringExact(Properties.Settings.Default.IconSelectorLastIconPack);
                    }
                    else
                    {
                        this.iconPacksBox.SelectedIndex = 0;
                    }
                }));
            }
        }

        private void IconPacksBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            IconPack iconPack = IconManager.GetIconPackByName(this.iconPacksBox.Text);
            Properties.Settings.Default.IconSelectorLastIconPack = iconPack.Name;
            Properties.Settings.Default.Save();
            this.SelectedIconPack = iconPack;
            this.btnImport.Visible = !iconPack.ExtensionStoreManaged;
            this.btnCreateIcon.Visible = !iconPack.ExtensionStoreManaged;
            //this.btnGiphy.Visible = !iconPack.ExtensionStoreManaged;
            this.btnDeleteIcon.Visible = !iconPack.ExtensionStoreManaged;
            this.btnExportIconPack.Visible = !iconPack.ExtensionStoreManaged;
            this.lblManaged.Visible = iconPack.ExtensionStoreManaged;

            this.LoadIcons(iconPack);
        }

        private void BtnDeleteIconPack_Click(object sender, EventArgs e)
        {
            using (var messageBox = new CustomControls.MessageBox())
            {
                IconPack iconPack = IconManager.GetIconPackByName(this.iconPacksBox.Text);
                if (messageBox.ShowDialog(Language.LanguageManager.Strings.AreYouSure, String.Format(Language.LanguageManager.Strings.SelectedIconPackWillBeDeleted, iconPack.Name), MessageBoxButtons.YesNo) == DialogResult.Yes)
                {
                    IconManager.DeleteIconPack(iconPack);
                    this.btnPreview.BackgroundImage = null;
                    this.SelectedIcon = null;
                    this.lblSize.Text = "";
                    this.lblType.Text = "";
                    Task.Run(() =>
                    {
                        this.LoadIconPacks();
                    });
                }
            }
        }

        private void BtnCreateIconPack_Click(object sender, EventArgs e)
        {
            using (var createIconPackDialog = new CreateIconPack())
            {
                if (createIconPackDialog.ShowDialog() == DialogResult.OK)
                {
                    
                    Task.Run(() =>
                    {
                        this.LoadIconPacks();
                    });
                    this.iconPacksBox.SelectedItem = createIconPackDialog.IconPackName;
                }
            }
        }

        private void BtnImportIconPack_Click(object sender, EventArgs e)
        {
            using (var openFileDialog = new OpenFileDialog
            {
                Title = "Import icon pack",
                CheckFileExists = true,
                CheckPathExists = true,
                DefaultExt = ".macroDeckIconPack",
                Filter = "Macro Deck Icon Pack (*.macroDeckIconPack)|*.macroDeckIconPack|Zip Archive (*.zip)|*.zip",
            })
            {
                if (openFileDialog.ShowDialog() == DialogResult.OK)
                {
                    var importedIconPack = IconManager.InstallIconPackZip(openFileDialog.FileName);
                    if (importedIconPack != null)
                    {
                        this._selectIconPack = importedIconPack;
                        this.LoadIconPacks();
                    }
                }
            }
            
        }

        private void BtnExportIconPack_Click(object sender, EventArgs e)
        {
            using (var exportIconPackDialog = new ExportIconPackDialog(this.SelectedIconPack))
            {
                if (exportIconPackDialog.ShowDialog() == DialogResult.OK)
                {
                    using (FolderBrowserDialog folderBrowserDialog = new FolderBrowserDialog())
                    {
                        if (folderBrowserDialog.ShowDialog() == DialogResult.OK)
                        {
                            Cursor.Current = Cursors.WaitCursor;
                            IconManager.SaveIconPack(this.SelectedIconPack);
                            IconManager.ExportIconPack(this.SelectedIconPack, folderBrowserDialog.SelectedPath);
                            Cursor.Current = Cursors.Default;
                            using (var msgBox = new CustomControls.MessageBox())
                            {
                                msgBox.ShowDialog(Language.LanguageManager.Strings.ExportIconPack, String.Format(Language.LanguageManager.Strings.XSuccessfullyExportedToX, this.SelectedIconPack.Name, folderBrowserDialog.SelectedPath), MessageBoxButtons.OK);
                            }
                        }
                    }
                }
            }
        }

        private void BtnDeleteIcon_Click(object sender, EventArgs e)
        {
            if (this.SelectedIcon == null || this.SelectedIconPack == null) return;
            using (var messageBox = new CustomControls.MessageBox())
            {
                if (messageBox.ShowDialog(Language.LanguageManager.Strings.AreYouSure, String.Format(Language.LanguageManager.Strings.SelectedIconWillBeDeleted, this.SelectedIconPack.Name), MessageBoxButtons.YesNo) == DialogResult.Yes)
                {
                    IconManager.DeleteIcon(this.SelectedIconPack, this.SelectedIcon);
                    this.btnPreview.BackgroundImage = null;
                    this.btnPreview.Image = null;
                    this.SelectedIcon = null;
                    this.lblSize.Text = "";
                    this.lblType.Text = "";
                    this.LoadIcons(this.SelectedIconPack);
                }
            }
        }

        private void BtnCreateIcon_Click(object sender, EventArgs e)
        {
            using (var iconCreator = new Dialogs.IconCreator())
            {
                if (iconCreator.ShowDialog() == DialogResult.OK)
                {
                    using (var iconImportQuality = new IconImportQuality())
                    {
                        if (iconImportQuality.ShowDialog() == DialogResult.OK)
                        {
                            Image icon = null;
                            if (iconImportQuality.Pixels == -1)
                            {
                                icon = iconCreator.Image;
                            }
                            else
                            {
                                iconCreator.Image.Save(Path.Combine(MacroDeck.TempDirectoryPath, "iconcreator"));

                                using (var collection = new MagickImageCollection(new FileInfo(Path.GetFullPath(Path.Combine(MacroDeck.TempDirectoryPath, "iconcreator")))))
                                {
                                    Cursor.Current = Cursors.WaitCursor;
                                    collection.Coalesce();
                                    foreach (var image in collection)
                                    {
                                        image.Resize(iconImportQuality.Pixels, iconImportQuality.Pixels);
                                        image.Quality = 50;
                                        image.Crop(iconImportQuality.Pixels, iconImportQuality.Pixels);
                                    }
                                    try
                                    {
                                        collection.Write(new FileInfo(Path.GetFullPath(Path.Combine(MacroDeck.TempDirectoryPath, "iconcreator.resized"))));
                                        byte[] imageBytes = File.ReadAllBytes(Path.Combine(MacroDeck.TempDirectoryPath, "iconcreator.resized"));
                                        using (var ms = new MemoryStream(imageBytes))
                                        {
                                            icon = Image.FromStream(ms);
                                        }
                                    } catch {}
                                    Cursor.Current = Cursors.Default;
                                }
                            }
                            IconPack iconPack = IconManager.GetIconPackByName(this.iconPacksBox.Text);
                            IconManager.AddIconImage(iconPack, icon);
                            this.LoadIcons(iconPack, true);
                        }
                    }
                }
            }
        }

        

        private void btnDownloadIcon_Click(object sender, EventArgs e)
        {

        }

        private void BtnGenerateStatic_Click(object sender, EventArgs e)
        {
            IconPack iconPack = IconManager.GetIconPackByName(this.iconPacksBox.Text);
            Image icon = this.SelectedIcon.IconImage;
            MemoryStream ms = new MemoryStream();
            icon.Save(ms, ImageFormat.Png);
            byte[] bmpBytes = ms.GetBuffer();
            ms = new MemoryStream(bmpBytes);
            Image iconStatic = Image.FromStream(ms);
            ms.Close();
            ms.Dispose();
            Icons.Icon addedIcon = IconManager.AddIconImage(iconPack, iconStatic);
            this.SelectIcon(addedIcon);
            this.LoadIcons(iconPack, true);

        }
    }
}
