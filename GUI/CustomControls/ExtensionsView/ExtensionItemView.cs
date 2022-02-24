﻿using SuchByte.MacroDeck.Extension;
using SuchByte.MacroDeck.ExtensionStore;
using SuchByte.MacroDeck.Icons;
using SuchByte.MacroDeck.Language;
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

            this.lblExtensionType.Text = macroDeckExtension.ExtensionTypeDisplayName;
            this.btnConfigure.Visible = macroDeckExtension.Configurable;

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
                        this.lblStatus.Text = "Disabled";
                        this.lblStatus.ForeColor = Color.White;
                        this.lblStatus.BackColor = Color.Firebrick;
                    } else if (PluginManager.UpdatedPlugins.Contains(macroDeckPlugin))
                    {
                        this.lblStatus.Text = "Pending restart";
                        this.lblStatus.ForeColor = Color.Yellow;
                        this.lblStatus.BackColor = Color.Transparent;
                    } else if (PluginManager.PluginsUpdateAvailable.Contains(macroDeckPlugin) && !PluginManager.UpdatedPlugins.Contains(macroDeckPlugin))
                    {
                        this.lblStatus.Text = "Update available";
                        this.lblStatus.ForeColor = Color.White;
                        this.lblStatus.BackColor = Color.Transparent;
                    } else
                    {
                        this.lblStatus.Text = "Active";
                        this.lblStatus.ForeColor = Color.FromArgb(0,192,0);
                        this.lblStatus.BackColor = Color.Transparent;
                    }
                    
                    this.extensionIcon.BackgroundImage = macroDeckPlugin.Icon == null ? Properties.Resources.Icon : macroDeckPlugin.Icon;
                    this.lblExtensionName.Text = macroDeckPlugin.Name;
                    this.lblVersion.Text = $"{LanguageManager.Strings.InstalledVersion}: {macroDeckPlugin.Version}";
                    break;
                case ExtensionType.IconPack:
                    this.lblExtensionType.BackColor = Color.FromArgb(0, 173, 14);
                    IconPack iconPack = macroDeckExtension.ExtensionObject as IconPack;
                    if (updateAvailable)
                    {
                        this.lblStatus.Text = "Update available";
                        this.lblStatus.ForeColor = Color.White;
                        this.lblStatus.BackColor = Color.Transparent;
                    } else
                    {
                        this.lblStatus.Text = "Active";
                        this.lblStatus.ForeColor = Color.FromArgb(0, 192, 0);
                        this.lblStatus.BackColor = Color.Transparent;
                    }
                    this.extensionIcon.BackgroundImage = Utils.IconPackPreview.GeneratePreviewImage(iconPack);
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

                            if (msgBox.ShowDialog("", String.Format(LanguageManager.Strings.XSuccessfullyUninstalled, macroDeckPlugin.Name), MessageBoxButtons.YesNo) == DialogResult.Yes)
                            {
                                MacroDeck.RestartMacroDeck("--show");
                            }
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
                    ExtensionStoreHelper.InstallPluginById(ExtensionStoreHelper.GetPackageId(iconPack));
                    break;
            }
        }
    }

    
}
