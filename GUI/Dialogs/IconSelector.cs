﻿using ImageMagick;
using SuchByte.MacroDeck.GUI.CustomControls;
using SuchByte.MacroDeck.GUI.Dialogs;
using SuchByte.MacroDeck.Icons;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
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

        public IconSelector()
        {
            InitializeComponent();
            this.btnImportIconPack.Text = Language.LanguageManager.Strings.ImportIconPack;
            this.btnExportIconPack.Text = Language.LanguageManager.Strings.ExportIconPack;
            this.btnCreateIcon.Text = Language.LanguageManager.Strings.CreateIcon;
            this.btnImport.Text = Language.LanguageManager.Strings.ImportIcon;
            this.btnDeleteIcon.Text = Language.LanguageManager.Strings.DeleteIcon;
            this.lblManaged.Text = Language.LanguageManager.Strings.IconSelectorManagedInfo;
            this.lblSizeLabel.Text = Language.LanguageManager.Strings.Size;
            this.lblTypeLabel.Text = Language.LanguageManager.Strings.Type;
            btnPreview.Radius = MacroDeck.ProfileManager.CurrentProfile.ButtonRadius;
        }

        private void LoadIcons(Icons.IconPack iconPack)
        {
            this.iconList.Controls.Clear();

            foreach (Icons.Icon icon in iconPack.Icons)
            {
                RoundedButton button = new RoundedButton();
                button.Width = 100;
                button.Height = 100;
                button.BackColor = Color.FromArgb(108,117,125);
                button.Radius = MacroDeck.ProfileManager.CurrentProfile.ButtonRadius;
                button.BackgroundImageLayout = ImageLayout.Stretch;
                button.BackgroundImage = Utils.Base64.GetImageFromBase64(icon.IconBase64);
                if (button.BackgroundImage.RawFormat.ToString().ToLower() == "gif")
                {
                    button.ShowGIFIndicator = true;
                }
                button.Tag = icon;
                button.Click += new EventHandler(this.IconButton_Click);
                button.Cursor = Cursors.Hand;
                this.iconList.Controls.Add(button);
            }
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        private void IconButton_Click(object sender, EventArgs e)
        {
            RoundedButton button = (RoundedButton)sender;
            Icons.Icon icon = button.Tag as Icons.Icon;
            Image previewImage = Utils.Base64.GetImageFromBase64(icon.IconBase64);

            btnPreview.BackgroundImage = previewImage;
            lblSize.Text =  String.Format("{0:n0}", ASCIIEncoding.Unicode.GetByteCount(icon.IconBase64) / 1000) + " kb";
            lblType.Text = previewImage.RawFormat.ToString().ToUpper();

            this.SelectedIconPack = IconManager.GetIconPackByName(icon.IconPack);
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
                    using (var iconImportQuality = new Dialogs.IconImportQuality())
                    {
                        if (iconImportQuality.ShowDialog() == DialogResult.OK)
                        {
                            List<Image> icons = new List<Image>();

                            Cursor.Current = Cursors.WaitCursor;
                            
                            if (iconImportQuality.Pixels == -1)
                            {
                                foreach (var file in openFileDialog.FileNames)
                                {
                                    try
                                    {
                                        icons.Add(Image.FromFile(file));
                                    } catch { }
                                }
                            } else
                            {
                                foreach (var file in openFileDialog.FileNames)
                                {
                                    try
                                    {
                                        using (var collection = new MagickImageCollection(new FileInfo(file)))
                                        {
                                            collection.Coalesce();
                                            foreach (var image in collection)
                                            {
                                                image.Resize(iconImportQuality.Pixels, iconImportQuality.Pixels);
                                                image.Quality = 50;
                                                image.Crop(iconImportQuality.Pixels, iconImportQuality.Pixels);
                                            }
                                            collection.Write(MacroDeck.TempDirectoryPath + new FileInfo(file).Name + ".resized");
                                            byte[] imageBytes = File.ReadAllBytes(MacroDeck.TempDirectoryPath + new FileInfo(file).Name + ".resized");
                                            using (var ms = new MemoryStream(imageBytes))
                                            {
                                                icons.Add(Image.FromStream(ms));
                                            }
                                        }
                                    }
                                    catch { }
                                }
                                
                            }
                            Icons.IconPack iconPack = IconManager.GetIconPackByName(this.iconPacksBox.Text);
                            foreach (Image icon in icons)
                            {
                                IconManager.AddIconImage(iconPack, icon);
                            }
                            
                            this.LoadIcons(iconPack);
                            Cursor.Current = Cursors.Default;
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
            foreach (Icons.IconPack iconPack in IconManager.IconPacks)
            {
                this.Invoke(new Action(() => this.iconPacksBox.Items.Add(iconPack.Name)));
            }
            if (Properties.Settings.Default.IconSelectorLastIconPack != null)
            {
                this.Invoke(new Action(() =>
                {
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
            Icons.IconPack iconPack = IconManager.GetIconPackByName(this.iconPacksBox.Text);
            Properties.Settings.Default.IconSelectorLastIconPack = iconPack.Name;
            Properties.Settings.Default.Save();
            this.SelectedIconPack = iconPack;
            this.btnImport.Visible = !iconPack.PackageManagerManaged;
            this.btnCreateIcon.Visible = !iconPack.PackageManagerManaged;
            this.btnGiphy.Visible = !iconPack.PackageManagerManaged;
            //this.btnDownloadIcon.Visible = !iconPack.Protected;
            this.btnDeleteIcon.Visible = !iconPack.PackageManagerManaged;
            this.btnExportIconPack.Visible = !iconPack.PackageManagerManaged;
            this.lblManaged.Visible = iconPack.PackageManagerManaged;

            this.LoadIcons(iconPack);

        }

        private void btnDeleteIconPack_Click(object sender, EventArgs e)
        {
            using (var messageBox = new CustomControls.MessageBox())
            {
                Icons.IconPack iconPack = IconManager.GetIconPackByName(this.iconPacksBox.Text);
                if (messageBox.ShowDialog("Are you sure?", "The selected icon pack " + iconPack.Name + " with all its icons will be deleted.", MessageBoxButtons.YesNo) == DialogResult.Yes)
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

        private void btnCreateIconPack_Click(object sender, EventArgs e)
        {
            using (var createIconPackDialog = new Dialogs.CreateIconPack())
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
                DefaultExt = "db",
                Filter = "Database files (*.db;)|*.db"
            }) {
                if (openFileDialog.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        File.Copy(Path.GetFullPath(openFileDialog.FileName), MacroDeck.IconPackDirectoryPath + "\\" + openFileDialog.SafeFileName);
                        IconManager.LoadIconPacks();
                        Task.Run(() =>
                        {
                            this.LoadIconPacks();
                        });
                        using (var messageBox = new GUI.CustomControls.MessageBox())
                        {
                            messageBox.ShowDialog("Imported icon pack", "Successfully imported the icon pack " + openFileDialog.SafeFileName, MessageBoxButtons.OK);
                            messageBox.Dispose();
                        }
                    }
                    catch (Exception ex)
                    {
                        using (var messageBox = new GUI.CustomControls.MessageBox())
                        {
                            messageBox.ShowDialog("Error while importing icon pack", ex.Message, MessageBoxButtons.OK);
                            messageBox.Dispose();
                        }
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
                    IconManager.SaveIconPacks();
                    using (FolderBrowserDialog folderBrowserDialog = new FolderBrowserDialog())
                    {
                        if (folderBrowserDialog.ShowDialog() == DialogResult.OK)
                        {
                            Cursor.Current = Cursors.WaitCursor;
                            IconManager.SaveIconPack(this.SelectedIconPack, folderBrowserDialog.SelectedPath);
                            Cursor.Current = Cursors.Default;
                            using (var msgBox = new CustomControls.MessageBox())
                            {
                                msgBox.ShowDialog("Export successful", String.Format("{0} was successfully exported to {1}", this.SelectedIconPack.Name, folderBrowserDialog.SelectedPath), MessageBoxButtons.OK);
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
                if (messageBox.ShowDialog("Are you sure?", "The selected icon will be deleted from the icon pack " + this.SelectedIconPack.Name, MessageBoxButtons.YesNo) == DialogResult.Yes)
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
                    using (var iconImportQuality = new Dialogs.IconImportQuality())
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
                                iconCreator.Image.Save(MacroDeck.TempDirectoryPath + "iconcreator");

                                using (var collection = new MagickImageCollection(new FileInfo(Path.GetFullPath(MacroDeck.TempDirectoryPath + "iconcreator"))))
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
                                        collection.Write(new FileInfo(Path.GetFullPath(MacroDeck.TempDirectoryPath + "iconcreator.resized")));
                                        byte[] imageBytes = File.ReadAllBytes(MacroDeck.TempDirectoryPath + "iconcreator.resized");
                                        using (var ms = new MemoryStream(imageBytes))
                                        {
                                            icon = Image.FromStream(ms);
                                        }
                                    } catch {}
                                    Cursor.Current = Cursors.Default;
                                }
                            }
                            Icons.IconPack iconPack = IconManager.GetIconPackByName(this.iconPacksBox.Text);
                            IconManager.AddIconImage(iconPack, icon);
                            this.LoadIcons(iconPack);
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
                    using (var iconImportQuality = new IconImportQuality())
                    {
                        if (iconImportQuality.ShowDialog() == DialogResult.OK)
                        {
                            Image icon = null;
                            if (iconImportQuality.Pixels == -1)
                            {
                                icon = Image.FromFile(Path.GetFullPath(MacroDeck.TempDirectoryPath + "giphy"));
                            }
                            else
                            {
                                using (var collection = new MagickImageCollection(new FileInfo(Path.GetFullPath(MacroDeck.TempDirectoryPath + "giphy"))))
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
                                        collection.Write(new FileInfo(Path.GetFullPath(MacroDeck.TempDirectoryPath + "giphy.resized")));
                                        byte[] imageBytes = File.ReadAllBytes(MacroDeck.TempDirectoryPath + "giphy.resized");
                                        using (var ms = new MemoryStream(imageBytes))
                                        {
                                            icon = Image.FromStream(ms);
                                        }
                                    } catch { }
                                    Cursor.Current = Cursors.Default;
                                }
                            }
                            Icons.IconPack iconPack = IconManager.GetIconPackByName(this.iconPacksBox.Text);
                            IconManager.AddIconImage(iconPack, icon);
                            this.LoadIcons(iconPack);
                        }
                    }
                }
            }
        }

        private void btnDownloadIcon_Click(object sender, EventArgs e)
        {

        }
    }
}
