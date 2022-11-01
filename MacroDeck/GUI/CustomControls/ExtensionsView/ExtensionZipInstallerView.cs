using System;
using System.ComponentModel;
using System.Windows.Forms;
using SuchByte.MacroDeck.ExtensionStore;
using SuchByte.MacroDeck.Icons;
using SuchByte.MacroDeck.Logging;
using SuchByte.MacroDeck.Model;
using SuchByte.MacroDeck.Models;
using SuchByte.MacroDeck.Plugins;

namespace SuchByte.MacroDeck.GUI.CustomControls.ExtensionsView;

public partial class ExtensionZipInstallerView : UserControl
{
    public event EventHandler RequestClose;

    public ExtensionManifestModel ExtensionManifestModel;

        

    public ExtensionZipInstallerView()
    {
        InitializeComponent();
        Dock = DockStyle.Fill;
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
            ExtensionManifestModel = ExtensionManifestModel.FromZipFilePath(dialog.FileName);
            txtZipPath.Text = dialog.FileName;
            txtPackageId.Text = ExtensionManifestModel.PackageId;
            txtAuthor.Text = ExtensionManifestModel.Author;
    
            btnInstall.Enabled = true;
        } catch (Exception)
        {
            btnInstall.Enabled = false;
            MacroDeckLogger.Error(GetType(), "Invalid or corrupt zip archive provided for installation");
        }
    }

    private void BtnInstall_Click(object sender, EventArgs e)
    {
        try
        {
            if (ExtensionManifestModel != null)
            {
                switch (ExtensionManifestModel.Type)
                {
                    case ExtensionType.Plugin:
                        PluginManager.InstallPluginFromZip(txtZipPath.Text);
                        break;
                    case ExtensionType.IconPack:
                        IconManager.InstallIconPackZip(txtZipPath.Text);
                        break;
                }
            }
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