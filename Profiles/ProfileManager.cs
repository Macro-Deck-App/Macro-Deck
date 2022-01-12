using Newtonsoft.Json;
using SQLite;
using SuchByte.MacroDeck.Device;
using SuchByte.MacroDeck.Events;
using SuchByte.MacroDeck.Folders;
using SuchByte.MacroDeck.JSON;
using SuchByte.MacroDeck.Logging;
using SuchByte.MacroDeck.Plugins;
using SuchByte.MacroDeck.Server;
using SuchByte.MacroDeck.Variables;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace SuchByte.MacroDeck.Profiles
{
    public static class ProfileManager
    {
        public static event EventHandler ProfilesSaved;
        public static event EventHandler ProfileCreated;
        
        public static MacroDeckProfile CurrentProfile;
        
        

        public static List<MacroDeckProfile> Profiles { get { return _profiles; } }

        private static List<MacroDeckProfile> _profiles = new List<MacroDeckProfile>();


        public static void AddVariableChangedListener()
        {
            Variables.VariableManager.OnVariableChanged += VariableChanged;
        }

        

        public static void AddWindowFocusChangedListener()
        {
            WindowsFocus.WindowFocusDetection windowsFocusDetection = new WindowsFocus.WindowFocusDetection();
            windowsFocusDetection.OnWindowFocusChanged += new EventHandler(OnWindowFocusChanged);
        }

        private static void OnWindowFocusChanged(object sender, EventArgs e)
        {
            string process = (string)sender;

            List<MacroDeckProfile> affectedMacroDeckProfiles = _profiles.FindAll(delegate (MacroDeckProfile macroDeckProfile)
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
                        return DeviceManager.GetMacroDeckDevice(macroDeckDeviceClientId).Available && !String.IsNullOrWhiteSpace(DeviceManager.GetMacroDeckDevice(macroDeckDeviceClientId).ClientId);
                    }))
                    {
                        MacroDeckClient macroDeckClient = MacroDeckServer.GetMacroDeckClient(DeviceManager.GetMacroDeckDevice(macroDeckDevice).ClientId);
                        if (macroDeckClient.Profile.Equals(macroDeckProfile) && !macroDeckClient.Folder.Equals(macroDeckFolder))
                        {
                            MacroDeckServer.SetFolder(macroDeckClient, macroDeckFolder);
                        }
                    }
                }
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        private static void VariableChanged(object sender, EventArgs e)
        {
            Variable variable = sender as Variable;
            if (variable != null)
            {
                UpdateAllVariableLabels(variable);
            }
        }

        public static void UpdateAllVariableLabels(Variable variable)
        {
            Task.Run(() =>
            {
                foreach (MacroDeckProfile macroDeckProfile in _profiles)
                {
                    foreach (MacroDeckFolder macroDeckFolder in macroDeckProfile.Folders)
                    {
                        foreach (ActionButton.ActionButton actionButton in macroDeckFolder.ActionButtons.FindAll(x => (x.LabelOff != null && !String.IsNullOrWhiteSpace(x.LabelOff.LabelText) && x.LabelOff.LabelText.Contains(variable.Name.ToLower(), StringComparison.OrdinalIgnoreCase)) ||
                                                                                                                    (x.LabelOn != null && !String.IsNullOrWhiteSpace(x.LabelOn.LabelText) && x.LabelOn.LabelText.Contains(variable.Name.ToLower(), StringComparison.OrdinalIgnoreCase))).ToArray())
                        {
                            UpdateVariableLabels(actionButton);
                        }
                    }
                }
            });
        }

        public static void UpdateVariableLabels(ActionButton.ActionButton actionButton)
        {
            if (actionButton.LabelOff == null || actionButton.LabelOn == null) return;
            Task.Run(() =>
            {
                try
                {
                    string labelOffText = actionButton.LabelOff.LabelText.ToString();
                    string labelOnText = actionButton.LabelOn.LabelText.ToString();

                    labelOffText = VariableManager.RenderTemplate(labelOffText);
                    labelOnText = VariableManager.RenderTemplate(labelOnText);

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

        internal static void Load()
        {
            _profiles = new List<MacroDeckProfile>();
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
                    Error = (sender, args) => {
                        MacroDeckLogger.Error("Error while deserializing the profiles file: " + args.ErrorContext.Error.Message);
                        args.ErrorContext.Handled = true; 
                    },
                    ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                });
                if (profile.ProfileId == "")
                {
                    profile.ProfileId = _profiles.Count.ToString();
                }
                _profiles.Add(profile);
            }

            db.Close();

            if (_profiles.Count == 0)
            {
                MacroDeckProfile defaultProfile = new MacroDeckProfile
                {
                    DisplayName = Language.LanguageManager.Strings.Profile + " 1",
                    Columns = 5,
                    Rows = 3,
                    Folders = new List<MacroDeckFolder>()
                };

                _profiles.Add(defaultProfile);

                Save();
            }

            CurrentProfile = _profiles[0];
            
            if (CurrentProfile.Folders.Count < 1)
            {
                MacroDeckFolder root = new MacroDeckFolder
                {
                    FolderId = GenerateFolderId("*Root*"),
                    DisplayName = "*Root*",
                    Childs = new List<string>(),
                    ActionButtons = new List<ActionButton.ActionButton>()
                };

                CurrentProfile.Folders.Add(root);

                Save();
            }

            // Set the action button instances to the plugin actions
            foreach (MacroDeckProfile macroDeckProfile in _profiles)
            {
                foreach (MacroDeckFolder macroDeckFolder in macroDeckProfile.Folders)
                {
                    foreach (ActionButton.ActionButton actionButton in macroDeckFolder.ActionButtons)
                    {
                        actionButton.UpdateBindingState();
                        actionButton.UpdateHotkey();
                        UpdateVariableLabels(actionButton);
                        foreach (PluginAction pluginAction in actionButton.Actions)
                        {
                            pluginAction.SetActionButton(actionButton);
                        }
                    }
                }
            }

            MacroDeckLogger.Info("Loaded " + _profiles.Count + " profiles");
        }

        public static void Save()
        {
            if (MacroDeck.SafeMode)
            {
                return;
            }
            var databasePath = MacroDeck.ProfilesFilePath;

            var db = new SQLiteConnection(databasePath);
            db.CreateTable<ProfileJson>();
            db.DeleteAll<ProfileJson>();

            foreach (MacroDeckProfile profile in _profiles)
            {
                string jsonString = JsonConvert.SerializeObject(profile, new JsonSerializerSettings
                {
                    TypeNameHandling = TypeNameHandling.Auto,
                    NullValueHandling = NullValueHandling.Ignore,
                    Error = (sender, args) => {
                        MacroDeckLogger.Error("Error while serializing the profiles: " + args.ErrorContext.Error.Message);
                        args.ErrorContext.Handled = true;
                    },
                    ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                });

                ProfileJson profileJson = new ProfileJson();
                profileJson.JsonString = jsonString;

                db.InsertOrReplace(profileJson);
            }

            db.Close();
            MacroDeckLogger.Info("Saved " + _profiles.Count + " profiles");
            if (ProfilesSaved != null)
            {
                ProfilesSaved(Profiles, EventArgs.Empty);
            }
        }
        public static MacroDeckFolder CreateFolder(string displayName, MacroDeckFolder parent, MacroDeckProfile macroDeckProfile)
        {
            if (macroDeckProfile.Folders.FindAll(macroDeckFolder => macroDeckFolder.DisplayName.Equals(displayName)).Count > 0)
            {
                return null;
            }

            MacroDeckFolder newFolder = new MacroDeckFolder
            {
                DisplayName = displayName,
                Childs = new List<string>(),
                ActionButtons = new List<ActionButton.ActionButton>(),
                FolderId = GenerateFolderId(displayName)
            };

            parent.Childs.Add(newFolder.FolderId);

            macroDeckProfile.Folders.Add(newFolder);

            MacroDeckLogger.Info("Created folder " + displayName + " in " + macroDeckProfile.DisplayName);

            Save();

            return newFolder;
        }



        public static MacroDeckFolder FindParentFolder(MacroDeckFolder macroDeckFolder, MacroDeckProfile macroDeckProfile)
        {
            MacroDeckFolder parentFolder = null;
            parentFolder = macroDeckProfile.Folders.Find(folder => folder.Childs.Contains(macroDeckFolder.FolderId));
            return parentFolder;
        }

        public static void DeleteFolder(MacroDeckFolder folder, MacroDeckProfile macroDeckProfile)
        {
            if (!macroDeckProfile.Folders.Contains(folder)) return;
            if (macroDeckProfile.Folders[0].Equals(folder)) return;

            if (folder.ActionButtons != null)
            {
                foreach (ActionButton.ActionButton actionButton in folder.ActionButtons)
                {
                    actionButton.Dispose();
                }
            }

            foreach (MacroDeckClient macroDeckClient in MacroDeckServer.Clients.FindAll(macroDeckClient => macroDeckClient.Folder.FolderId.Equals(folder.FolderId)))
            {
                MacroDeckServer.SetFolder(macroDeckClient, macroDeckProfile.Folders[0]);
            }

            foreach (MacroDeckFolder folders in macroDeckProfile.Folders.FindAll(macroDeckFolder => macroDeckFolder.Childs.Contains(folder.FolderId)))
            {
                folders.Childs.Remove(folder.FolderId);
            }

            foreach (var childId in folder.Childs)
            {
                MacroDeckFolder child = FindFolderById(childId, macroDeckProfile);
                macroDeckProfile.Folders.Remove(child);
            }

            MacroDeckLogger.Info("Delete " + folder.DisplayName + " in " + macroDeckProfile.DisplayName);

            folder.Dispose();
            macroDeckProfile.Folders.Remove(folder);
            Save();
        }


        public static MacroDeckProfile CreateProfile(string displayName, DeviceClass deviceClass = DeviceClass.SoftwareClient)
        {
            if (_profiles.FindAll(macroDeckProfile => macroDeckProfile.DisplayName.Equals(displayName)).Count > 0)
            {
                return _profiles.Find(macroDeckProfile => macroDeckProfile.DisplayName.Equals(displayName));
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
                ProfileId = GenerateFolderId(displayName),
            };

            MacroDeckFolder rootFolder = new MacroDeckFolder
            {
                DisplayName = "*Root*",
                FolderId = GenerateFolderId("*Root*"),
                Childs = new List<string>(),
                ActionButtons = new List<ActionButton.ActionButton>()
            };

            newProfile.Folders.Add(rootFolder);

            _profiles.Add(newProfile);

            Save();

            if (ProfileCreated != null)
            {
                ProfileCreated(newProfile, EventArgs.Empty);
            }

            MacroDeckLogger.Info("Created profile " + displayName);

            return newProfile;
        }

        public static void DeleteProfile(MacroDeckProfile macroDeckProfile)
        {
            if (!_profiles.Contains(macroDeckProfile)) return;
            if (_profiles.Count < 2) return;

            foreach (MacroDeckFolder macroDeckFolder in macroDeckProfile.Folders)
            {
                macroDeckFolder.Dispose();
            }

            foreach (MacroDeckDevice macroDeckDevice in DeviceManager.GetKnownDevices().FindAll(macroDeckDevice => macroDeckDevice.ProfileId != null && macroDeckDevice.ProfileId.Equals(macroDeckProfile.ProfileId)))
            {
                DeviceManager.SetProfile(macroDeckDevice, _profiles[0]);
            }



            MacroDeckLogger.Info("Delete profile " + macroDeckProfile.DisplayName);

            macroDeckProfile.Dispose();

            _profiles.Remove(macroDeckProfile);

            Save();

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        private static string GenerateFolderId(string folderName)
        {
            Regex rgx = new Regex("[^a-zA-Z0-9 -]");
            return DateTimeOffset.Now.ToUnixTimeMilliseconds().ToString() + rgx.Replace(folderName.ToLower(), "");
        }

        public static MacroDeckFolder FindFolderById(string Id, MacroDeckProfile macroDeckProfile)
        {
            return macroDeckProfile.Folders.Find(macroDeckFolder => macroDeckFolder.FolderId.Equals(Id));
        }



        public static MacroDeckFolder FindFolderByDisplayName(String displayName, MacroDeckProfile macroDeckProfile)
        {
            return macroDeckProfile.Folders.Find(macroDeckFolder => macroDeckFolder.DisplayName.Equals(displayName));
        }

        public static ActionButton.ActionButton FindActionButton(MacroDeckFolder folder, int row, int col)
        {
            return folder.ActionButtons.Find(actionButton => actionButton.Position_X == col && actionButton.Position_Y == row);
        }

        public static MacroDeckProfile FindProfileById(string id)
        {
            return _profiles.Find(macroDeckProfile => macroDeckProfile.ProfileId.Equals(id));
        }

        public static MacroDeckProfile FindProfileByDisplayName(string displayName)
        {
            return _profiles.Find(macroDeckProfile => macroDeckProfile.DisplayName.Equals(displayName));
        }

    }
}
