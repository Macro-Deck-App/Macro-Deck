using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows.Forms;
using SuchByte.MacroDeck.Device;
using SuchByte.MacroDeck.Folders;
using SuchByte.MacroDeck.GUI.CustomControls;
using SuchByte.MacroDeck.Language;
using SuchByte.MacroDeck.Profiles;
using MessageBox = SuchByte.MacroDeck.GUI.CustomControls.MessageBox;

namespace SuchByte.MacroDeck.GUI
{
    public partial class AddFolder : DialogForm
    {
        MacroDeckFolder ParentFolder;
        MacroDeckFolder Folder;

        public AddFolder(MacroDeckFolder parentFolder)
        {
            ParentFolder = parentFolder;
            InitializeComponent();
            UpdateTranslation();
            btnCreateFolder.Text = LanguageManager.Strings.Create;
        }

        public AddFolder(MacroDeckFolder folder, bool rename)
        {
            Folder = folder;
            InitializeComponent();
            UpdateTranslation();
            btnCreateFolder.Text = LanguageManager.Strings.Save;
            folderName.Enabled = !folder.IsRootFolder;
        }

        private void UpdateTranslation()
        {
            lblFolderName.Text = LanguageManager.Strings.FolderName;
            lblApplication.Text = LanguageManager.Strings.Application;
            lblDevices.Text = LanguageManager.Strings.Devices;
            groupAutomaticallySwitchFolder.Text = LanguageManager.Strings.AutomaticallySwitchToFolder;
            radioNever.Text = LanguageManager.Strings.Never;
            radioOnFocus.Text = LanguageManager.Strings.OnApplicationFocus;
        }

        private void BtnCreateFolder_Click(object sender, EventArgs e)
        {
            if (folderName.Text.Length < 1) return;

            if (Folder == null)
            {
                if (ProfileManager.CurrentProfile.Folders.Find(x => x.DisplayName.Equals(folderName.Text, StringComparison.OrdinalIgnoreCase)) != null)
                {
                    using (var msgBox = new MessageBox())
                    {
                        msgBox.ShowDialog(LanguageManager.Strings.CantCreateFolder, string.Format(LanguageManager.Strings.FolderCalledXAlreadyExists, folderName.Text), MessageBoxButtons.OK);
                        msgBox.Dispose();
                    }
                    return;
                }

                if (ParentFolder == null || !ProfileManager.CurrentProfile.Folders.Contains(ParentFolder))
                {
                    ParentFolder = ProfileManager.CurrentProfile.Folders[0];
                }

                Folder = ProfileManager.CreateFolder(folderName.Text, ParentFolder, ProfileManager.CurrentProfile);
            }
            else
            {
                if (ProfileManager.CurrentProfile.Folders.Find(x => x.DisplayName.Equals(folderName.Text, StringComparison.OrdinalIgnoreCase)) != null && ProfileManager.CurrentProfile.Folders.Find(x => x.DisplayName.Equals(folderName.Text, StringComparison.OrdinalIgnoreCase)) != Folder)
                {
                    using (var msgBox = new MessageBox())
                    {
                        msgBox.ShowDialog(LanguageManager.Strings.CantCreateFolder, string.Format(LanguageManager.Strings.FolderCalledXAlreadyExists, folderName.Text), MessageBoxButtons.OK);
                        msgBox.Dispose();
                    }
                    return;
                }
            }

            Folder.DisplayName = folderName.Text;
            Folder.ApplicationToTrigger = applicationList.Text;

            if (radioOnFocus.Checked && applicationList.Text.Length > 0 && devicesList.CheckedItems.Count > 0)
            {
                Folder.ApplicationToTrigger = applicationList.Text;
                Folder.ApplicationsFocusDevices = new List<string>();
                foreach (int i in devicesList.CheckedIndices)
                {
                    Folder.ApplicationsFocusDevices.Add(DeviceManager.GetMacroDeckDeviceByDisplayName(devicesList.Items[i].ToString()).ClientId);
                }
            }
            else
            {
                Folder.ApplicationToTrigger = "";
                Folder.ApplicationsFocusDevices = new List<string>();
            }
            ProfileManager.Save();

            DialogResult = DialogResult.OK;
            Close();
        }

        private void AddFolder_Load(object sender, EventArgs e)
        {
            if (Folder != null)
            {
                folderName.Text = Folder.DisplayName;
                if (Folder.ApplicationToTrigger.Length > 0 && Folder.ApplicationsFocusDevices.Count > 0)
                {
                    radioOnFocus.CheckedChanged -= RadioOnFocus_CheckedChanged;
                    radioOnFocus.Checked = true;
                    radioOnFocus.CheckedChanged += RadioOnFocus_CheckedChanged;
                }
            }
            
        }

        private void RadioOnFocus_CheckedChanged(object sender, EventArgs e)
        {
            if (radioOnFocus.Checked && !radioNever.Checked)
            {
                applicationDeviceSettings.Enabled = true;
                Task.Run(() =>
                {
                    LoadApplications();
                });
                LoadDevices();

            } else
            {
                applicationDeviceSettings.Enabled = false;
                devicesList.Items.Clear();
            }
        }

        private void LoadApplications()
        {
            var processCollection = Process.GetProcesses();
            foreach (var p in processCollection)
            {
                Invoke(() =>
                {
                    if (!applicationList.Items.Contains(p.ProcessName) && !string.IsNullOrEmpty(p.MainWindowTitle))
                    {
                        applicationList.Items.Add(p.ProcessName);
                    }
                });
            }
           
            if (Folder != null) {
                Invoke(() =>
                {
                    if (Folder.ApplicationToTrigger.Length > 0 && !applicationList.Items.Contains(Folder.ApplicationToTrigger))
                    {
                        applicationList.Items.Add(Folder.ApplicationToTrigger);
                    }
                    applicationList.Text = Folder.ApplicationToTrigger;
                });
            }
        }

        private void LoadDevices()
        {
            devicesList.Items.Clear();

            foreach (var macroDeckDevice in DeviceManager.GetKnownDevices())
            {
                devicesList.Items.Add(macroDeckDevice.DisplayName, (Folder != null && Folder.ApplicationsFocusDevices != null && Folder.ApplicationsFocusDevices.Contains(macroDeckDevice.ClientId)));
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
