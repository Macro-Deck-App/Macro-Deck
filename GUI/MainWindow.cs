﻿using Fleck;
using SuchByte.MacroDeck.GUI.CustomControls;
using SuchByte.MacroDeck.GUI.Dialogs;
using SuchByte.MacroDeck.GUI.MainWindowContents;
using SuchByte.MacroDeck.Icons;
using SuchByte.MacroDeck.Plugins;
using SuchByte.MacroDeck.Server;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SuchByte.MacroDeck.GUI
{
    public partial class MainWindow : CustomControls.Form
    {
        private DeckView DeckView { get; set; }
        private PackageManagerView PackageManagerView { get; set; }
        private VariablesView VariablesView { get; set; }
      
        public MainWindow()
        {
            this.InitializeComponent();
            this.UpdateTranslation();
            Language.LanguageManager.LanguageChanged += LanguageChanged;
        }

        private void UpdateTranslation()
        {
            this.lblIpAddressHostname.Text = Language.LanguageManager.Strings.IpAddressHostNamePort;
        }

        private void LanguageChanged(object sender, EventArgs e)
        {
            this.UpdateTranslation();
            if (this.DeckView != null)
            {
                this.DeckView.UpdateTranslation();
            }
            if (this.PackageManagerView != null)
            {
                this.PackageManagerView.UpdateTranslation();
            }
            if (this.VariablesView != null)
            {
                this.VariablesView.UpdateTranslation();
            }
        }

        public void SelectContentButton(Control control)
        {
            this.Invoke(new Action(() => 
            {
                if (!control.GetType().Equals(typeof(ContentSelectorButton))) return;
                foreach (Control contentButton in this.contentButtonPanel.Controls.OfType<ContentSelectorButton>())
                {
                    ((ContentSelectorButton)contentButton).Selected = false;
                }
                btnSettings.Selected = false;
                ((ContentSelectorButton)control).Selected = true;
            }));
        }

        public void SetView(Control view, bool clearAll = false)
        {
            if (this.contentPanel.Controls.Contains(view)) return;
            this.Invoke(new Action(() => 
            { 
                this.contentPanel.Controls.Add(view);
                this.lblTitle.Text = view.Name;
            }));
            foreach (Control control in this.contentPanel.Controls)
            {
                this.Invoke(new Action(() =>
                {
                    if (control != this.DeckView && (clearAll && (control != this.PackageManagerView && control != this.VariablesView)) && control != view)
                    {
                        control.Dispose();
                    }
                    if (control != view)
                    {
                        this.contentPanel.Controls.Remove(control);
                    }
                }));
            }

            if (view.GetType().Equals(typeof(DeckView)))
            {
                SelectContentButton(btnDeck);
            } else if (view.GetType().Equals(typeof(DeviceManagerView)))
            {
                SelectContentButton(btnDeviceManager);
            } else if (view.GetType().Equals(typeof(PackageManagerView)))
            {
                SelectContentButton(btnPackageManager);
            }
            else if (view.GetType().Equals(typeof(SettingsView)))
            {
                SelectContentButton(btnSettings);
            }
            else if (view.GetType().Equals(typeof(VariablesView)))
            {
                SelectContentButton(btnVariables);
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }


        private void MainWindow_Load(object sender, EventArgs e)
        {
            this.SetView(new LoadingView());
            this.lblVersion.Text = "Macro Deck " + MacroDeck.VersionString + (Debugger.IsAttached  ? " (debug)" : "");
            
            PluginManager.OnPluginsChange += new EventHandler(this.OnPluginsChanged);
            PluginManager.OnUpdateCheckFinished += OnPackageManagerUpdateCheckFinished;
            IconManager.OnUpdateCheckFinished += OnPackageManagerUpdateCheckFinished;
            MacroDeckServer.OnDeviceConnectionStateChanged += new EventHandler(this.OnClientsConnectedChanged);
            MacroDeckServer.OnServerStateChanged += new EventHandler(this.OnServerStateChanged);
            this.OnClientsConnectedChanged(null, EventArgs.Empty);
            this.OnServerStateChanged(null, EventArgs.Empty);
            this.RefreshPluginsLabels();

            if (MacroDeck.SafeMode)
            {
                this.BackColor = Color.FromArgb(99, 0, 0);
                this.lblSafeMode.Visible = true;
                using (var msgBox = new GUI.CustomControls.MessageBox())
                {
                    msgBox.ShowDialog("Safe mode", "Macro Deck was started in safe mode! This means no changes on the action buttons will be saved to prevent damage.", MessageBoxButtons.OK);

                }
            }

            if (PluginManager.PluginsUpdateAvailable.Count > 0 || IconManager.IconPacksUpdateAvailable.Count > 0)
            {
                btnPackageManager.SetNotification(true);
            }

            Task.Run(() =>
            {
                this.DeckView = new DeckView();
                Thread.Sleep(500);
                this.SetView(this.DeckView);
                this.Invoke(new Action(() =>
                {
                    this.navigation.Visible = true;
                    if (MacroDeck.Updater.UpdateAvailable)
                    {
                        using (var downloadUpdateDialog = new DownloadUpdateDialog(MacroDeck.Updater.UpdateObject))
                        {
                            downloadUpdateDialog.ShowDialog();
                        }
                    }
            
                }));
                
            });

            PluginManager.ScanUpdatesAsync();
            IconManager.ScanUpdatesAsync();
            
        }

        private void OnPackageManagerUpdateCheckFinished(object sender, EventArgs e)
        {
            if (PluginManager.PluginsUpdateAvailable.Count > 0 || IconManager.IconPacksUpdateAvailable.Count > 0)
            {
                btnPackageManager.SetNotification(true);
            } else
            {
                btnPackageManager.SetNotification(false);
            }
        }

        private void OnPluginsChanged(object sender, EventArgs e)
        {
            this.RefreshPluginsLabels();
            
        }

        private void RefreshPluginsLabels()
        {
            this.lblPluginsLoaded.Text = String.Format(Language.LanguageManager.Strings.XPluginsLoaded, PluginManager.Plugins.Values.Count);
            if (PluginManager.PluginsNotLoaded.Values.Count > 0)
            {
                this.lblPluginsNotLoaded.Visible = true;
                this.lblPluginsNotLoaded.Text = String.Format(Language.LanguageManager.Strings.XPluginsDisabled, PluginManager.PluginsNotLoaded.Values.Count);
            }
            else
            {
                this.lblPluginsNotLoaded.Visible = false;
            }
        }

        private void OnServerStateChanged(object sender, EventArgs e)
        {
            if (MacroDeckServer.WebSocketServer.ListenerSocket == null)
            {
                lblServerStatus.Text = Language.LanguageManager.Strings.ServerOffline;
            } else
            {
                lblServerStatus.Text = Language.LanguageManager.Strings.ServerRunning;
                lblIPAddress.Text = MacroDeck.Configuration.Host_Address;
                lblPort.Text = MacroDeckServer.WebSocketServer.Port.ToString();
            }
        }

        private void OnClientsConnectedChanged(object sender, EventArgs e)
        {
            this.Invoke(new Action(() =>
                this.lblNumClientsConnected.Text = String.Format(Language.LanguageManager.Strings.XClientsConnected, MacroDeckServer.Clients.Count)
            ));
        }

        private void BtnDeck_Click(object sender, EventArgs e)
        {
            this.SetView(this.DeckView);
        }

        private void BtnPackageManager_Click(object sender, EventArgs e)
        {
            if (this.PackageManagerView == null)
            {
                this.PackageManagerView = new PackageManagerView();
            }
            this.SetView(this.PackageManagerView);
        }

        private void BtnSettings_Click(object sender, EventArgs e)
        {
            this.SetView(new SettingsView());
        }

        private void BtnDeviceManager_Click(object sender, EventArgs e)
        {
            this.SetView(new DeviceManagerView());
        }

        public void OnFormClosing(object sender, EventArgs e)
        {
            foreach (Control control in this.contentPanel.Controls)
            {
                control.Dispose();
            }
        }

        private void BtnVariables_Click(object sender, EventArgs e)
        {
            if (this.VariablesView == null)
            {
                this.VariablesView = new VariablesView();
            }
            this.SetView(this.VariablesView);
        }
    }
}
