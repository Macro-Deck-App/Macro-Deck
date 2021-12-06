using SuchByte.MacroDeck.Device;
using SuchByte.MacroDeck.Folders;
using SuchByte.MacroDeck.GUI.CustomControls;
using SuchByte.MacroDeck.Profiles;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SuchByte.MacroDeck.GUI
{
    public partial class AddFolder : DialogForm
    {
        MacroDeckFolder ParentFolder;
        MacroDeckFolder Folder;

        public AddFolder(MacroDeckFolder parentFolder)
        {
            this.ParentFolder = parentFolder;
            InitializeComponent();
            this.UpdateTranslation();
            this.btnCreateFolder.Text = Language.LanguageManager.Strings.Create;
        }

        public AddFolder(MacroDeckFolder folder, bool rename)
        {
            this.Folder = folder;
            InitializeComponent();
            this.UpdateTranslation();
            this.btnCreateFolder.Text = Language.LanguageManager.Strings.Save;
        }

        private void UpdateTranslation()
        {
            this.lblFolderName.Text = Language.LanguageManager.Strings.FolderName;
            this.lblApplication.Text = Language.LanguageManager.Strings.Application;
            this.lblDevices.Text = Language.LanguageManager.Strings.Devices;
            this.groupAutomaticallySwitchFolder.Text = Language.LanguageManager.Strings.AutomaticallySwitchToFolder;
            this.radioNever.Text = Language.LanguageManager.Strings.Never;
            this.radioOnFocus.Text = Language.LanguageManager.Strings.OnApplicationFocus;
        }

        private void BtnCreateFolder_Click(object sender, EventArgs e)
        {
            if (folderName.Text.Length < 1) return;

            if (this.Folder == null)
            {
                if (ProfileManager.CurrentProfile.Folders.Find(x => x.DisplayName.Equals(folderName.Text, StringComparison.OrdinalIgnoreCase)) != null)
                {
                    using (var msgBox = new CustomControls.MessageBox())
                    {
                        msgBox.ShowDialog(Language.LanguageManager.Strings.CantCreateFolder, String.Format(Language.LanguageManager.Strings.FolderCalledXAlreadyExists, folderName.Text), MessageBoxButtons.OK);
                        msgBox.Dispose();
                    }
                    return;
                }

                if (ParentFolder == null || !ProfileManager.CurrentProfile.Folders.Contains(ParentFolder))
                {
                    ParentFolder = ProfileManager.CurrentProfile.Folders[0];
                }

                this.Folder = ProfileManager.CreateFolder(folderName.Text, ParentFolder, ProfileManager.CurrentProfile);
            }
            else
            {
                if (ProfileManager.CurrentProfile.Folders.Find(x => x.DisplayName.Equals(folderName.Text, StringComparison.OrdinalIgnoreCase)) != null && ProfileManager.CurrentProfile.Folders.Find(x => x.DisplayName.Equals(folderName.Text, StringComparison.OrdinalIgnoreCase)) != this.Folder)
                {
                    using (var msgBox = new CustomControls.MessageBox())
                    {
                        msgBox.ShowDialog(Language.LanguageManager.Strings.CantCreateFolder, String.Format(Language.LanguageManager.Strings.FolderCalledXAlreadyExists, folderName.Text), MessageBoxButtons.OK);
                        msgBox.Dispose();
                    }
                    return;
                }
            }

            this.Folder.DisplayName = this.folderName.Text;
            this.Folder.ApplicationToTrigger = this.applicationList.Text;

            if (radioOnFocus.Checked && this.applicationList.Text.Length > 0 && this.devicesList.CheckedItems.Count > 0)
            {
                this.Folder.ApplicationToTrigger = this.applicationList.Text;
                this.Folder.ApplicationsFocusDevices = new List<string>();
                foreach (int i in this.devicesList.CheckedIndices)
                {
                    this.Folder.ApplicationsFocusDevices.Add(DeviceManager.GetMacroDeckDeviceByDisplayName(this.devicesList.Items[i].ToString()).ClientId);
                }
            }
            else
            {
                this.Folder.ApplicationToTrigger = "";
                this.Folder.ApplicationsFocusDevices = new List<string>();
            }
            ProfileManager.Save();

            this.DialogResult = DialogResult.OK;
            this.Close();
        }

        private void AddFolder_Load(object sender, EventArgs e)
        {
            if (this.Folder != null)
            {
                this.folderName.Text = Folder.DisplayName;
                if (this.Folder.ApplicationToTrigger.Length > 0 && this.Folder.ApplicationsFocusDevices.Count > 0)
                {
                    this.radioOnFocus.CheckedChanged -= RadioOnFocus_CheckedChanged;
                    this.radioOnFocus.Checked = true;
                    this.radioOnFocus.CheckedChanged += RadioOnFocus_CheckedChanged;
                }
            }
            
        }

        private void RadioOnFocus_CheckedChanged(object sender, EventArgs e)
        {
            if (radioOnFocus.Checked && !radioNever.Checked)
            {
                this.applicationDeviceSettings.Enabled = true;
                Task.Run(() =>
                {
                    LoadApplications();
                });
                LoadDevices();

            } else
            {
                this.applicationDeviceSettings.Enabled = false;
                this.devicesList.Items.Clear();
            }
        }

        private void LoadApplications()
        {
            Process[] processCollection = Process.GetProcesses();
            foreach (Process p in processCollection)
            {
                this.Invoke(new Action(() =>
                {
                    if (!this.applicationList.Items.Contains(p.ProcessName) && !String.IsNullOrEmpty(p.MainWindowTitle))
                    {
                        this.applicationList.Items.Add(p.ProcessName);
                    }
                }));
            }
           
            if (this.Folder != null) {
                this.Invoke(new Action(() =>
                {
                    if (this.Folder.ApplicationToTrigger.Length > 0 && !this.applicationList.Items.Contains(this.Folder.ApplicationToTrigger))
                    {
                        this.applicationList.Items.Add(this.Folder.ApplicationToTrigger);
                    }
                    this.applicationList.Text = this.Folder.ApplicationToTrigger;
                }));
            }
        }

        private void LoadDevices()
        {
            this.devicesList.Items.Clear();

            foreach (MacroDeckDevice macroDeckDevice in DeviceManager.GetKnownDevices())
            {
                this.devicesList.Items.Add(macroDeckDevice.DisplayName, (this.Folder != null && this.Folder.ApplicationsFocusDevices != null && this.Folder.ApplicationsFocusDevices.Contains(macroDeckDevice.ClientId)));
            }
           
        }

        private void BtnReloadApplications_Click(object sender, EventArgs e)
        {
            Task.Run(() =>
            {
                LoadApplications();
            });
        }
    }
}
