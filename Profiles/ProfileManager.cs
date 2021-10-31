using Newtonsoft.Json;
using SQLite;
using SuchByte.MacroDeck.Device;
using SuchByte.MacroDeck.Events;
using SuchByte.MacroDeck.Folders;
using SuchByte.MacroDeck.JSON;
using SuchByte.MacroDeck.Plugins;
using SuchByte.MacroDeck.Server;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Text;
using System.Threading.Tasks;

namespace SuchByte.MacroDeck.Profiles
{
    public class ProfileManager
    {

        public MacroDeckProfile CurrentProfile;

        public List<MacroDeckProfile> Profiles { get { return this._profiles; } }

        private List<MacroDeckProfile> _profiles = new List<MacroDeckProfile>();

        public ProfileManager()
        {
            this.Load();
        }

        public void AddVariableChangedListener()
        {
            Variables.VariableManager.OnVariableChanged += this.VariableChanged;
        }

        public void AddWindowFocusChangedListener()
        {
            WindowsFocus.WindowFocusDetection windowsFocusDetection = new WindowsFocus.WindowFocusDetection();
            windowsFocusDetection.OnWindowFocusChanged += new EventHandler(this.OnWindowFocusChanged);
        }

        private void OnWindowFocusChanged(object sender, EventArgs e)
        {
            string process = (string)sender;

            List<MacroDeckProfile> affectedMacroDeckProfiles = this._profiles.FindAll(delegate (MacroDeckProfile macroDeckProfile)
            {
                return macroDeckProfile.Folders.FindAll(macroDeckFolder => macroDeckFolder.ApplicationToTrigger.Equals(process)).Count > 0;
            });

            foreach (MacroDeckProfile macroDeckProfile in affectedMacroDeckProfiles)
            {
                foreach (MacroDeckFolder macroDeckFolder in macroDeckProfile.Folders.FindAll(macroDeckFolder => macroDeckFolder.ApplicationToTrigger.Equals(process)))
                {
                    foreach (string macroDeckDevice in macroDeckFolder.ApplicationsFocusDevices.FindAll(delegate (string macroDeckDeviceClientId)
                    {
                        if (DeviceManager.GetMacroDeckDevice(macroDeckDeviceClientId) == null) return false;
                        return DeviceManager.GetMacroDeckDevice(macroDeckDeviceClientId).Available && DeviceManager.GetMacroDeckDevice(macroDeckDeviceClientId).ClientId.Length > 0;
                    }))
                    {
                        MacroDeckClient macrodeckClient = MacroDeckServer.GetMacroDeckClient(DeviceManager.GetMacroDeckDevice(macroDeckDevice).ClientId);
                        if (!macrodeckClient.Folder.Equals(macroDeckFolder))
                        {
                            MacroDeckServer.SetFolder(macrodeckClient, macroDeckFolder);
                        }
                    }
                }
            }
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        private void VariableChanged(object sender, EventArgs e)
        {
            this.UpdateAllVariableLabels();
        }

        public void UpdateAllVariableLabels()
        {
            Task.Run(() =>
            {
                foreach (MacroDeckProfile macroDeckProfile in this._profiles)
                {
                    foreach (MacroDeckFolder macroDeckFolder in macroDeckProfile.Folders)
                    {
                        foreach (ActionButton.ActionButton actionButton in macroDeckFolder.ActionButtons.ToArray())
                        {
                            UpdateVariableLabels(actionButton);
                        }
                    }
                }
            });
        }

        public void UpdateVariableLabels(ActionButton.ActionButton actionButton)
        {
            if (actionButton.LabelOff == null || actionButton.LabelOn == null) return;
            Task.Run(() =>
            {
                try
                {
                    string labelOffText = actionButton.LabelOff.LabelText;
                    string labelOnText = actionButton.LabelOn.LabelText;
                    int variablesCount = 0;
                    foreach (Variables.Variable variable in Variables.VariableManager.Variables)
                    {
                        if (labelOffText.ToLower().Contains("{" + variable.Name.ToLower() + "}") || labelOffText.ToLower().Contains("{" + variable.Name.ToLower() + "}"))
                        {
                            variablesCount++;
                            labelOffText = labelOffText.Replace("{" + variable.Name + "}", variable.Value.ToString(), StringComparison.OrdinalIgnoreCase);
                            labelOnText = labelOnText.Replace("{" + variable.Name + "}", variable.Value.ToString(), StringComparison.OrdinalIgnoreCase);
                        }
                    }
                    if (variablesCount == 0) return;
                    actionButton.LabelOff.LabelBase64 = Utils.Base64.GetBase64FromBitmap(Utils.LabelGenerator.GetLabel(new Bitmap(250, 250), labelOffText, actionButton.LabelOff.LabelPosition, new Font(actionButton.LabelOff.FontFamily, actionButton.LabelOff.Size), actionButton.LabelOff.LabelColor, Color.Black, new SizeF(2.0f, 2.0f)));
                    actionButton.LabelOn.LabelBase64 = Utils.Base64.GetBase64FromBitmap(Utils.LabelGenerator.GetLabel(new Bitmap(250, 250), labelOnText, actionButton.LabelOn.LabelPosition, new Font(actionButton.LabelOn.FontFamily, actionButton.LabelOn.Size), actionButton.LabelOn.LabelColor, Color.Black, new SizeF(2.0f, 2.0f)));
                    foreach (MacroDeckClient macroDeckClient in MacroDeckServer.Clients)
                    {
                        MacroDeckServer.SendButton(macroDeckClient, actionButton);
                    }
                }
                catch { }
            });
        }

        private void Load(int rows = -1, int columns = -1)
        {
            this._profiles = new List<MacroDeckProfile>();
            var databasePath = MacroDeck.ProfilesFilePath;

            var db = new SQLiteConnection(databasePath);
            db.CreateTable<ProfileJson>();

            var query = db.Table<ProfileJson>();


            foreach (var folderJson in query)
            {
                string jsonString = folderJson.JsonString;
                MacroDeckProfile profile = JsonConvert.DeserializeObject<MacroDeckProfile>(jsonString, new JsonSerializerSettings
                {
                    TypeNameHandling = TypeNameHandling.Auto,
                    NullValueHandling = NullValueHandling.Ignore,
                    Error = (sender, args) => { args.ErrorContext.Handled = true; }
                });
                if (profile.ProfileId == "")
                {
                    profile.ProfileId = this._profiles.Count.ToString();
                }
                this._profiles.Add(profile);
            }

            db.Close();

            if (this._profiles.Count == 0)
            {
                MacroDeckProfile defaultProfile = new MacroDeckProfile
                {
                    DisplayName = Language.LanguageManager.Strings.Profile + " 1",
                    Columns = (columns > -1 ? columns : 5),
                    Rows = (rows > -1 ? rows : 3),
                    Folders = new List<MacroDeckFolder>()
                };

                this._profiles.Add(defaultProfile);

                this.Save();
            }

            this.CurrentProfile = _profiles[0];
            
            if (this.CurrentProfile.Folders.Count < 1)
            {
                MacroDeckFolder root = new MacroDeckFolder
                {
                    FolderId = 0,
                    DisplayName = "*Root*",
                    Childs = new List<long>(),
                    ActionButtons = new List<ActionButton.ActionButton>()
                };

                this.CurrentProfile.Folders.Add(root);

                this.Save();
            }
        }

        public void Save()
        {
            if (MacroDeck.SafeMode)
            {
                return;
            }
            var databasePath = MacroDeck.ProfilesFilePath;

            var db = new SQLiteConnection(databasePath);
            db.CreateTable<ProfileJson>();
            db.DeleteAll<ProfileJson>();

            foreach (MacroDeckProfile profile in this._profiles)
            {
                string jsonString = JsonConvert.SerializeObject(profile, new JsonSerializerSettings
                {
                    TypeNameHandling = TypeNameHandling.Auto,
                    NullValueHandling = NullValueHandling.Ignore,
                    Error = (sender, args) => { args.ErrorContext.Handled = true; }
                });

                ProfileJson profileJson = new ProfileJson();
                profileJson.JsonString = jsonString;

                db.InsertOrReplace(profileJson);
            }

            db.Close();
        }
        public MacroDeckFolder CreateFolder(String displayName, MacroDeckFolder parent, MacroDeckProfile macroDeckProfile)
        {
            if (macroDeckProfile.Folders.FindAll(macroDeckFolder => macroDeckFolder.DisplayName.Equals(displayName)).Count > 0)
            {
                return null;
            }

            MacroDeckFolder newFolder = new MacroDeckFolder
            {
                DisplayName = displayName,
                Childs = new List<long>(),
                ActionButtons = new List<ActionButton.ActionButton>(),
                FolderId = macroDeckProfile.Folders.Count
            };

            parent.Childs.Add(newFolder.FolderId);

            macroDeckProfile.Folders.Add(newFolder);

            this.Save();

            return newFolder;
        }



        public MacroDeckFolder FindParentFolder(MacroDeckFolder macroDeckFolder, MacroDeckProfile macroDeckProfile)
        {
            MacroDeckFolder parentFolder = null;
            parentFolder = macroDeckProfile.Folders.Find(folder => folder.Childs.Contains(macroDeckFolder.FolderId));
            return parentFolder;
        }

        public void DeleteFolder(MacroDeckFolder folder, MacroDeckProfile macroDeckProfile)
        {
            if (!macroDeckProfile.Folders.Contains(folder)) return;
            if (macroDeckProfile.Folders[0].Equals(folder)) return;

            foreach (ActionButton.ActionButton actionButton in folder.ActionButtons)
            {
                foreach (string macroDeckEventString in actionButton.EventActions.Keys)
                {
                    IMacroDeckEvent macroDeckEvent = MacroDeck.EventManager.GetEventByName(macroDeckEventString);
                    if (macroDeckEvent == null) continue;
                    macroDeckEvent.OnEvent -= this.OnActionButtonEventTrigger;
                }
            }

            foreach (Server.MacroDeckClient macroDeckClient in MacroDeckServer.Clients.FindAll(macroDeckClient => macroDeckClient.Folder.FolderId.Equals(folder.FolderId)))
            {
                MacroDeckServer.SetFolder(macroDeckClient, macroDeckProfile.Folders[0]);
            }

            foreach (MacroDeckFolder folders in macroDeckProfile.Folders.FindAll(macroDeckFolder => macroDeckFolder.Childs.Contains(folder.FolderId)))
            {
                folders.Childs.Remove(folder.FolderId);
            }

            foreach (int childId in folder.Childs)
            {
                MacroDeckFolder child = FindFolderById(childId, macroDeckProfile);
                macroDeckProfile.Folders.Remove(child);
            }

            folder.Dispose();
            macroDeckProfile.Folders.Remove(folder);
            this.Save();
        }


        public MacroDeckProfile CreateProfile(string displayName)
        {
            if (this._profiles.FindAll(macroDeckProfile => macroDeckProfile.DisplayName.Equals(displayName)).Count > 0)
            {
                return null;
            }

            MacroDeckProfile newProfile = new MacroDeckProfile
            {
                DisplayName = displayName,
                Rows = 3,
                Columns = 5,
                ButtonRadius = 40,
                ButtonSpacing = 15,
                ButtonBackground = true,
                Folders = new List<MacroDeckFolder>(),
                ProfileId = this._profiles.Count.ToString(),
            };

            MacroDeckFolder rootFolder = new MacroDeckFolder
            {
                FolderId = 0,
                DisplayName = "*Root*",
                Childs = new List<long>(),
                ActionButtons = new List<ActionButton.ActionButton>()
            };

            newProfile.Folders.Add(rootFolder);

            this._profiles.Add(newProfile);

            this.Save();

            return newProfile;
        }

        public void DeleteProfile(MacroDeckProfile macroDeckProfile)
        {
            if (!this._profiles.Contains(macroDeckProfile)) return;
            if (this._profiles.Count < 2) return;

            this.RemoveEventHandlers(macroDeckProfile);

            foreach (MacroDeckFolder macroDeckFolder in macroDeckProfile.Folders)
            {
                macroDeckFolder.Dispose();
            }

            foreach (MacroDeckDevice macroDeckDevice in DeviceManager.GetKnownDevices().FindAll(macroDeckDevice => macroDeckDevice.ProfileId.Equals(macroDeckProfile.ProfileId)))
            {
                DeviceManager.SetProfile(macroDeckDevice, this._profiles[0]);
            }

            macroDeckProfile.Dispose();

            this._profiles.Remove(macroDeckProfile);

            this.Save();

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        public void AddAllEventHandlers()
        {
            foreach (MacroDeckProfile macroDeckProfile in this._profiles)
            {
                foreach (MacroDeckFolder folder in macroDeckProfile.Folders)
                {
                    foreach (ActionButton.ActionButton actionButton in folder.ActionButtons.FindAll(actionButton => actionButton.EventActions != null))
                    {
                        this.AddEventHandler(actionButton);
                    }
                }
            }
        }

        public void RemoveEventHandlers(MacroDeckProfile macroDeckProfile)
        {
            foreach (MacroDeckFolder folder in macroDeckProfile.Folders)
            {
                foreach (ActionButton.ActionButton actionButton in folder.ActionButtons.FindAll(actionButton => actionButton.EventActions != null))
                {
                    this.RemoveEventHandler(actionButton);
                }
            }
        }

        public void AddEventHandler(ActionButton.ActionButton actionButton)
        {
            if (actionButton.EventActions == null) return;
            foreach (string macroDeckEventString in actionButton.EventActions.Keys)
            {
                IMacroDeckEvent macroDeckEvent = MacroDeck.EventManager.GetEventByName(macroDeckEventString);
                if (macroDeckEvent == null) continue;
                macroDeckEvent.OnEvent += new EventHandler<MacroDeckEventArgs>(this.OnActionButtonEventTrigger);
            }
        }

        public void RemoveEventHandler(ActionButton.ActionButton actionButton)
        {
            if (actionButton.EventActions == null) return;
            foreach (string macroDeckEventString in actionButton.EventActions.Keys)
            {
                IMacroDeckEvent macroDeckEvent = MacroDeck.EventManager.GetEventByName(macroDeckEventString);
                if (macroDeckEvent == null) continue;
                macroDeckEvent.OnEvent -= this.OnActionButtonEventTrigger;
            }
        }

        private void OnActionButtonEventTrigger(object sender, MacroDeckEventArgs e)
        {
            Task.Run(() =>
            {
                IMacroDeckEvent macroDeckEvent = (IMacroDeckEvent)sender;
                if (!e.ActionButton.EventActions.ContainsKey(macroDeckEvent.Name)) return;
                foreach (PluginAction action in e.ActionButton.EventActions[macroDeckEvent.Name])
                {
                    action.Trigger("-1", e.ActionButton);
                }
            });
        }

        public MacroDeckFolder FindFolderById(long Id, MacroDeckProfile macroDeckProfile)
        {
            return macroDeckProfile.Folders.Find(macroDeckFolder => macroDeckFolder.FolderId.Equals(Id));
        }

        public MacroDeckFolder FindFolderByDisplayName(String displayName, MacroDeckProfile macroDeckProfile)
        {
            return macroDeckProfile.Folders.Find(macroDeckFolder => macroDeckFolder.DisplayName.Equals(displayName));
        }

        public ActionButton.ActionButton FindActionButton(MacroDeckFolder folder, int row, int col)
        {
            return folder.ActionButtons.Find(actionButton => actionButton.Position_X == col && actionButton.Position_Y == row);
        }

        public MacroDeckProfile FindProfileById(string id)
        {
            return this._profiles.Find(macroDeckProfile => macroDeckProfile.ProfileId.Equals(id));
        }

        public MacroDeckProfile FindProfileByDisplayName(string displayName)
        {
            return this._profiles.Find(macroDeckProfile => macroDeckProfile.DisplayName.Equals(displayName));
        }

    }
}
