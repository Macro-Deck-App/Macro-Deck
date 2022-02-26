using SuchByte.MacroDeck.Logging;
using SuchByte.MacroDeck.Plugins;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace SuchByte.MacroDeck.GUI.CustomControls.ExtensionsView
{
    public partial class ExtensionZipInstallerView : UserControl
    {
        public event EventHandler RequestClose;

        public ExtensionZipInstallerView()
        {
            InitializeComponent();
        }

        private void BtnSelectFile_Click(object sender, EventArgs e)
        {
            dlgSelectPluginFile.FileOk += DlgSelectPluginFile_FileOk;
            dlgSelectPluginFile.ShowDialog();
        }

        private void DlgSelectPluginFile_FileOk(object sender, CancelEventArgs e)
        {
            var dialog = sender as OpenFileDialog;
            try
            {
                var manifest = PluginManifest.FromZipFilePath(dialog.FileName);
                txtZipPath.Text = dialog.FileName;
                txtPackageId.Text = manifest.PackageId;
                txtAuthor.Text = manifest.Author;
                txtMainFile.Text = manifest.MainFile;
                btnInstall.Enabled = true;
            } catch (Exception)
            {
                btnInstall.Enabled = false;
                MacroDeckLogger.Error(GetType(), $"Invalid or corrupt zip archive provided for installation");
            }
        }

        private void BtnInstall_Click(object sender, EventArgs e)
        {
            try
            {
                PluginManager.InstallPluginFromZip(txtZipPath.Text);
                RequestClose(this, EventArgs.Empty);
            }
            catch (Exception)
            {
                MacroDeckLogger.Error(GetType(), $"Failed to install archived plugin {txtZipPath.Text}.");
            }
        }

        private void btnClose_Click(object sender, EventArgs e)
        {
            RequestClose(this, EventArgs.Empty);
        }
    }
}
