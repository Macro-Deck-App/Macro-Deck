using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using SuchByte.MacroDeck.Extension;
using SuchByte.MacroDeck.ExtensionStore;
using SuchByte.MacroDeck.Icons;
using SuchByte.MacroDeck.Language;
using SuchByte.MacroDeck.Notifications;
using SuchByte.MacroDeck.Plugins;
using SuchByte.MacroDeck.Properties;
using SuchByte.MacroDeck.Utils;

namespace SuchByte.MacroDeck.GUI.CustomControls.ExtensionsView;

public partial class ExtensionItemView : RoundedUserControl
{
    private IMacroDeckExtension macroDeckExtension;
    public IMacroDeckExtension MacroDeckExtension => macroDeckExtension;

    public event EventHandler ExtensionRemoved;


    public ExtensionItemView(IMacroDeckExtension macroDeckExtension, bool updateAvailable)
    {
        this.macroDeckExtension = macroDeckExtension;
        InitializeComponent();

        btnUpdate.Text = LanguageManager.Strings.Update;
        btnConfigure.Text = LanguageManager.Strings.Configure;
        btnUninstall.Text = LanguageManager.Strings.Uninstall;

        lblExtensionType.Text = macroDeckExtension.ExtensionTypeDisplayName;
        btnConfigure.Visible = macroDeckExtension.Configurable || macroDeckExtension.GetType() == typeof(IconPackExtension);

        btnUpdate.Visible = updateAvailable;
        btnUpdate.BackColor = Color.FromArgb(20, 153, 0);
        BackColor = updateAvailable ? Color.FromArgb(222, 170, 27) : Color.FromArgb(65,65,65);

        switch (macroDeckExtension.ExtensionType)
        {
            case ExtensionType.Plugin:
                lblExtensionType.BackColor = Color.FromArgb(0, 95, 173);
                var macroDeckPlugin = macroDeckExtension.ExtensionObject as MacroDeckPlugin;
                if (PluginManager.PluginsNotLoaded.ContainsValue(macroDeckPlugin) && !PluginManager.UpdatedPlugins.Contains(macroDeckPlugin) && !updateAvailable)
                {
                    lblStatus.Text = LanguageManager.Strings.Disabled;
                    lblStatus.ForeColor = Color.White;
                    lblStatus.BackColor = Color.Firebrick;
                } else if (PluginManager.UpdatedPlugins.Contains(macroDeckPlugin))
                {
                    lblStatus.Text = LanguageManager.Strings.PendingRestart;
                    lblStatus.ForeColor = Color.Yellow;
                    lblStatus.BackColor = Color.Transparent;
                } else if (PluginManager.PluginsUpdateAvailable.Contains(macroDeckPlugin) && !PluginManager.UpdatedPlugins.Contains(macroDeckPlugin))
                {
                    lblStatus.Text = LanguageManager.Strings.UpdateAvailable;
                    lblStatus.ForeColor = Color.White;
                    lblStatus.BackColor = Color.Transparent;
                } else
                {
                    lblStatus.Text = LanguageManager.Strings.Enabled;
                    lblStatus.ForeColor = Color.FromArgb(0,192,0);
                    lblStatus.BackColor = Color.Transparent;
                }
                    
                extensionIcon.BackgroundImage = macroDeckPlugin.PluginIcon == null ? Resources.Icon : macroDeckPlugin.PluginIcon;
                lblExtensionName.Text = macroDeckPlugin.Name;
                lblVersion.Text = $"{LanguageManager.Strings.InstalledVersion}: {macroDeckPlugin.Version}";
                break;
            case ExtensionType.IconPack:
                lblExtensionType.BackColor = Color.FromArgb(0, 173, 14);
                var iconPack = macroDeckExtension.ExtensionObject as IconPack;
                if (updateAvailable)
                {
                    lblStatus.Text = LanguageManager.Strings.UpdateAvailable;
                    lblStatus.ForeColor = Color.White;
                    lblStatus.BackColor = Color.Transparent;
                } else
                {
                    lblStatus.Text = LanguageManager.Strings.Enabled;
                    lblStatus.ForeColor = Color.FromArgb(0, 192, 0);
                    lblStatus.BackColor = Color.Transparent;
                }
                extensionIcon.BackgroundImage = iconPack.IconPackIcon == null ? IconPackPreview.GeneratePreviewImage(iconPack) : iconPack.IconPackIcon;
                lblExtensionName.Text = iconPack.Name;
                lblVersion.Text = $"{LanguageManager.Strings.InstalledVersion}: {iconPack.Version}";
                break;
        }
    }

    private void BtnConfigure_Click(object sender, EventArgs e)
    {
        switch (macroDeckExtension.ExtensionType)
        {
            case ExtensionType.Plugin:
                var macroDeckPlugin = macroDeckExtension.ExtensionObject as MacroDeckPlugin;
                if (!macroDeckPlugin.CanConfigure) return;
                macroDeckPlugin.OpenConfigurator();
                break;
            case ExtensionType.IconPack:
                var iconPack = macroDeckExtension.ExtensionObject as IconPack;
                using (var iconSelector = new IconSelector(iconPack))
                {
                    iconSelector.ShowDialog();
                }
                break;
        }
    }

    private void BtnUninstall_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
    {

        switch (macroDeckExtension.ExtensionType)
        {
            case ExtensionType.Plugin:
                var macroDeckPlugin = macroDeckExtension.ExtensionObject as MacroDeckPlugin;
                using (var msgBox = new MessageBox())
                {
                    if (msgBox.ShowDialog(LanguageManager.Strings.AreYouSure, string.Format(LanguageManager.Strings.XWillBeUninstalled, macroDeckPlugin.Name), MessageBoxButtons.YesNo) == DialogResult.Yes)
                    {
                        PluginManager.DeletePlugin(macroDeckPlugin.Name);
                        ExtensionRemoved?.Invoke(this, EventArgs.Empty);

                        if (msgBox.ShowDialog("", string.Format(LanguageManager.Strings.XSuccessfullyUninstalled, macroDeckPlugin.Name), MessageBoxButtons.YesNo) == DialogResult.Yes)
                        {
                            MacroDeck.RestartMacroDeck("--show");
                        }

                        var btnRestart = new ButtonPrimary
                        {
                            AutoSize = true,
                            Text = LanguageManager.Strings.Restart,
                        };
                        btnRestart.Click += (sender, e) =>
                        {
                            MacroDeck.RestartMacroDeck("--show");
                        };
                        NotificationManager.SystemNotification("Extension Manager", string.Format(LanguageManager.Strings.XSuccessfullyUninstalled, macroDeckPlugin.Name), true, controls: new List<Control> { btnRestart });
                    }
                }
                break;
            case ExtensionType.IconPack:
                var iconPack = macroDeckExtension.ExtensionObject as IconPack;
                using (var msgBox = new MessageBox())
                {
                    if (msgBox.ShowDialog(LanguageManager.Strings.AreYouSure, string.Format(LanguageManager.Strings.XWillBeUninstalled, iconPack.Name), MessageBoxButtons.YesNo) == DialogResult.Yes)
                    {
                        IconManager.DeleteIconPack(iconPack);
                        ExtensionRemoved?.Invoke(this, EventArgs.Empty);
                    }
                }
                break;
        }
    }

    private void BtnUpdate_Click(object sender, EventArgs e)
    {
        btnUpdate.Spinner = true;
        btnUpdate.Enabled = false;
        switch (macroDeckExtension.ExtensionType)
        {
            case ExtensionType.Plugin:
                var macroDeckPlugin = macroDeckExtension.ExtensionObject as MacroDeckPlugin;
                ExtensionStoreHelper.InstallPluginById(ExtensionStoreHelper.GetPackageId(macroDeckPlugin));
                break;
            case ExtensionType.IconPack:
                var iconPack = macroDeckExtension.ExtensionObject as IconPack;
                ExtensionStoreHelper.InstallPluginById(iconPack.PackageId);
                break;
        }
    }
}