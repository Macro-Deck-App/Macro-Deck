using SuchByte.MacroDeck.Extension;
using SuchByte.MacroDeck.ExtensionStore;
using SuchByte.MacroDeck.Icons;
using SuchByte.MacroDeck.Language;
using SuchByte.MacroDeck.Notifications;
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
    public partial class ExtensionItemView : RoundedUserControl
    {
        private IMacroDeckExtension macroDeckExtension;
        public IMacroDeckExtension MacroDeckExtension { get => macroDeckExtension; }

        public event EventHandler ExtensionRemoved;


        public ExtensionItemView(IMacroDeckExtension macroDeckExtension, bool updateAvailable)
        {
            this.macroDeckExtension = macroDeckExtension;
            InitializeComponent();

            this.btnUpdate.Text = LanguageManager.Strings.Update;
            this.btnConfigure.Text = LanguageManager.Strings.Configure;
            this.btnUninstall.Text = LanguageManager.Strings.Uninstall;

            this.lblExtensionType.Text = macroDeckExtension.ExtensionTypeDisplayName;
            this.btnConfigure.Visible = macroDeckExtension.Configurable || macroDeckExtension.GetType() == typeof(IconPackExtension);

            this.btnUpdate.Visible = updateAvailable;
            this.btnUpdate.BackColor = Color.FromArgb(20, 153, 0);
            this.BackColor = updateAvailable ? Color.FromArgb(222, 170, 27) : Color.FromArgb(65,65,65);

            switch (macroDeckExtension.ExtensionType)
            {
                case ExtensionType.Plugin:
                    this.lblExtensionType.BackColor = Color.FromArgb(0, 95, 173);
                    MacroDeckPlugin macroDeckPlugin = macroDeckExtension.ExtensionObject as MacroDeckPlugin;
                    if (PluginManager.PluginsNotLoaded.ContainsValue(macroDeckPlugin) && !PluginManager.UpdatedPlugins.Contains(macroDeckPlugin) && !updateAvailable)
                    {
                        this.lblStatus.Text = LanguageManager.Strings.Disabled;
                        this.lblStatus.ForeColor = Color.White;
                        this.lblStatus.BackColor = Color.Firebrick;
                    } else if (PluginManager.UpdatedPlugins.Contains(macroDeckPlugin))
                    {
                        this.lblStatus.Text = LanguageManager.Strings.PendingRestart;
                        this.lblStatus.ForeColor = Color.Yellow;
                        this.lblStatus.BackColor = Color.Transparent;
                    } else if (PluginManager.PluginsUpdateAvailable.Contains(macroDeckPlugin) && !PluginManager.UpdatedPlugins.Contains(macroDeckPlugin))
                    {
                        this.lblStatus.Text = LanguageManager.Strings.UpdateAvailable;
                        this.lblStatus.ForeColor = Color.White;
                        this.lblStatus.BackColor = Color.Transparent;
                    } else
                    {
                        this.lblStatus.Text = LanguageManager.Strings.Enabled;
                        this.lblStatus.ForeColor = Color.FromArgb(0,192,0);
                        this.lblStatus.BackColor = Color.Transparent;
                    }
                    
                    this.extensionIcon.BackgroundImage = macroDeckPlugin.PluginIcon == null ? Properties.Resources.Icon : macroDeckPlugin.PluginIcon;
                    this.lblExtensionName.Text = macroDeckPlugin.Name;
                    this.lblVersion.Text = $"{LanguageManager.Strings.InstalledVersion}: {macroDeckPlugin.Version}";
                    break;
                case ExtensionType.IconPack:
                    this.lblExtensionType.BackColor = Color.FromArgb(0, 173, 14);
                    IconPack iconPack = macroDeckExtension.ExtensionObject as IconPack;
                    if (updateAvailable)
                    {
                        this.lblStatus.Text = LanguageManager.Strings.UpdateAvailable;
                        this.lblStatus.ForeColor = Color.White;
                        this.lblStatus.BackColor = Color.Transparent;
                    } else
                    {
                        this.lblStatus.Text = LanguageManager.Strings.Enabled;
                        this.lblStatus.ForeColor = Color.FromArgb(0, 192, 0);
                        this.lblStatus.BackColor = Color.Transparent;
                    }
                    this.extensionIcon.BackgroundImage = iconPack.IconPackIcon == null ? Utils.IconPackPreview.GeneratePreviewImage(iconPack) : iconPack.IconPackIcon;
                    this.lblExtensionName.Text = iconPack.Name;
                    this.lblVersion.Text = $"{LanguageManager.Strings.InstalledVersion}: {iconPack.Version}";
                    break;
            }
        }

        private void BtnConfigure_Click(object sender, EventArgs e)
        {
            switch (this.macroDeckExtension.ExtensionType)
            {
                case ExtensionType.Plugin:
                    MacroDeckPlugin macroDeckPlugin = macroDeckExtension.ExtensionObject as MacroDeckPlugin;
                    if (!macroDeckPlugin.CanConfigure) return;
                    macroDeckPlugin.OpenConfigurator();
                    break;
                case ExtensionType.IconPack:
                    IconPack iconPack = macroDeckExtension.ExtensionObject as IconPack;
                    using (var iconSelector = new IconSelector(iconPack))
                    {
                        iconSelector.ShowDialog();
                    }
                    break;
            }
        }

        private void BtnUninstall_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {

            switch (this.macroDeckExtension.ExtensionType)
            {
                case ExtensionType.Plugin:
                    MacroDeckPlugin macroDeckPlugin = macroDeckExtension.ExtensionObject as MacroDeckPlugin;
                    using (var msgBox = new MessageBox())
                    {
                        if (msgBox.ShowDialog(LanguageManager.Strings.AreYouSure, String.Format(LanguageManager.Strings.XWillBeUninstalled, macroDeckPlugin.Name), MessageBoxButtons.YesNo) == DialogResult.Yes)
                        {
                            PluginManager.DeletePlugin(macroDeckPlugin.Name);
                            if (ExtensionRemoved != null)
                            {
                                ExtensionRemoved(this, EventArgs.Empty);
                            }

                            if (msgBox.ShowDialog("", string.Format(LanguageManager.Strings.XSuccessfullyUninstalled, macroDeckPlugin.Name), MessageBoxButtons.YesNo) == DialogResult.Yes)
                            {
                                MacroDeck.RestartMacroDeck("--show");
                            }

                            var btnRestart = new ButtonPrimary()
                            {
                                AutoSize = true,
                                Text = LanguageManager.Strings.Restart,
                            };
                            btnRestart.Click += (sender, e) =>
                            {
                                MacroDeck.RestartMacroDeck("--show");
                            };
                            NotificationManager.SystemNotification("Extension Manager", string.Format(LanguageManager.Strings.XSuccessfullyUninstalled, macroDeckPlugin.Name), true, controls: new List<Control>() { btnRestart });
                        }
                    }
                    break;
                case ExtensionType.IconPack:
                    IconPack iconPack = macroDeckExtension.ExtensionObject as IconPack;
                    using (var msgBox = new MessageBox())
                    {
                        if (msgBox.ShowDialog(LanguageManager.Strings.AreYouSure, String.Format(LanguageManager.Strings.XWillBeUninstalled, iconPack.Name), MessageBoxButtons.YesNo) == DialogResult.Yes)
                        {
                            IconManager.DeleteIconPack(iconPack);
                            if (ExtensionRemoved != null)
                            {
                                ExtensionRemoved(this, EventArgs.Empty);
                            }
                        }
                    }
                    break;
            }
        }

        private void BtnUpdate_Click(object sender, EventArgs e)
        {
            this.btnUpdate.Spinner = true;
            this.btnUpdate.Enabled = false;
            switch (this.macroDeckExtension.ExtensionType)
            {
                case ExtensionType.Plugin:
                    MacroDeckPlugin macroDeckPlugin = macroDeckExtension.ExtensionObject as MacroDeckPlugin;
                    ExtensionStoreHelper.InstallPluginById(ExtensionStoreHelper.GetPackageId(macroDeckPlugin));
                    break;
                case ExtensionType.IconPack:
                    IconPack iconPack = macroDeckExtension.ExtensionObject as IconPack;
                    ExtensionStoreHelper.InstallPluginById(iconPack.PackageId);
                    break;
            }
        }
    }

    
}
