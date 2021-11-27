using Newtonsoft.Json;
using SQLite;
using SuchByte.MacroDeck.Device;
using SuchByte.MacroDeck.Events;
using SuchByte.MacroDeck.Folders;
using SuchByte.MacroDeck.JSON;
using SuchByte.MacroDeck.Plugins;
using SuchByte.MacroDeck.Server;
using SuchByte.MacroDeck.Variables;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Text;
using System.Threading.Tasks;

namespace SuchByte.MacroDeck.Profiles
{
    public static class ProfileManager
    {
        public static event EventHandler ProfilesSaved;

        public static MacroDeckProfile CurrentProfile;

        public static List<MacroDeckProfile> Profiles { get { return _profiles; } }

        private static List<MacroDeckProfile> _profiles = new List<MacroDeckProfile>();

        public static void Initialize()
        {
            Load();
        }

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
                    //int variablesCount = 0;

                    labelOffText = VariableManager.RenderTemplate(labelOffText);
                    labelOnText = VariableManager.RenderTemplate(labelOnText);

                    /*foreach (Variables.Variable variable in Variables.VariableManager.Variables)
                    {
                        if (labelOffText.ToLower().Contains("{" + variable.Name.ToLower() + "}") || labelOffText.ToLower().Contains("{" + variable.Name.ToLower() + "}"))
                        {
                            variablesCount++;
                            labelOffText = labelOffText.Replace("{" + variable.Name + "}", variable.Value.ToString(), StringComparison.OrdinalIgnoreCase);
                            labelOnText = labelOnText.Replace("{" + variable.Name + "}", variable.Value.ToString(), StringComparison.OrdinalIgnoreCase);
                        }
                    }
                    if (variablesCount == 0) return;*/

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

        private static void Load()
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
                    Error = (sender, args) => { args.ErrorContext.Handled = true; }
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
                    FolderId = 0,
                    DisplayName = "*Root*",
                    Childs = new List<long>(),
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
                        foreach (PluginAction pluginAction in actionButton.Actions)
                        {
                            pluginAction.SetActionButton(actionButton);
                        }
                    }
                }
            }
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
                    Error = (sender, args) => { args.ErrorContext.Handled = true; }
                });

                ProfileJson profileJson = new ProfileJson();
                profileJson.JsonString = jsonString;

                db.InsertOrReplace(profileJson);
            }

            db.Close();
            if (ProfilesSaved != null)
            {
                ProfilesSaved(Profiles, EventArgs.Empty);
            }
        }
        public static MacroDeckFolder CreateFolder(String displayName, MacroDeckFolder parent, MacroDeckProfile macroDeckProfile)
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

            foreach (ActionButton.ActionButton actionButton in folder.ActionButtons)
            {
                foreach (var eventListener in actionButton.EventListeners)
                {
                    IMacroDeckEvent macroDeckEvent = EventManager.GetEventByName(eventListener.EventToListen);
                   /* if (macroDeckEvent == null) continue;
                    macroDeckEvent.OnEvent -= OnActionButtonEventTrigger;*/
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
            Save();
        }


        public static MacroDeckProfile CreateProfile(string displayName)
        {
            if (_profiles.FindAll(macroDeckProfile => macroDeckProfile.DisplayName.Equals(displayName)).Count > 0)
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
                ProfileId = _profiles.Count.ToString(),
            };

            MacroDeckFolder rootFolder = new MacroDeckFolder
            {
                FolderId = 0,
                DisplayName = "*Root*",
                Childs = new List<long>(),
                ActionButtons = new List<ActionButton.ActionButton>()
            };

            newProfile.Folders.Add(rootFolder);

            _profiles.Add(newProfile);

            Save();

            return newProfile;
        }

        public static void DeleteProfile(MacroDeckProfile macroDeckProfile)
        {
            if (!_profiles.Contains(macroDeckProfile)) return;
            if (_profiles.Count < 2) return;

            RemoveEventHandlers(macroDeckProfile);

            foreach (MacroDeckFolder macroDeckFolder in macroDeckProfile.Folders)
            {
                macroDeckFolder.Dispose();
            }

            foreach (MacroDeckDevice macroDeckDevice in DeviceManager.GetKnownDevices().FindAll(macroDeckDevice => macroDeckDevice.ProfileId.Equals(macroDeckProfile.ProfileId)))
            {
                DeviceManager.SetProfile(macroDeckDevice, _profiles[0]);
            }

            macroDeckProfile.Dispose();

            _profiles.Remove(macroDeckProfile);

            Save();

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        public static void AddAllEventHandlers()
        {
            /*foreach (MacroDeckProfile macroDeckProfile in _profiles)
            {
                foreach (MacroDeckFolder folder in macroDeckProfile.Folders)
                {
                    foreach (ActionButton.ActionButton actionButton in folder.ActionButtons.FindAll(actionButton => actionButton.EventListeners != null))
                    {
                        AddEventHandler(actionButton);
                    }
                }
            }*/
            
        }

        public static void RemoveEventHandlers(MacroDeckProfile macroDeckProfile)
        {
            /*foreach (MacroDeckFolder folder in macroDeckProfile.Folders)
            {
                foreach (ActionButton.ActionButton actionButton in folder.ActionButtons.FindAll(actionButton => actionButton.EventListeners != null))
                {
                    RemoveEventHandler(actionButton);
                }
            }*/
        }

    /*    public static void AddEventHandler(ActionButton.ActionButton actionButton)
        {
            if (actionButton.EventListeners == null) return;
            foreach (var eventListener in actionButton.EventListeners)
            {
                IMacroDeckEvent macroDeckEvent = EventManager.GetEventByName(eventListener.EventToListen);
                if (macroDeckEvent == null) continue;
                macroDeckEvent.OnEvent += new EventHandler<MacroDeckEventArgs>(OnActionButtonEventTrigger);
            }
        }

        public static void RemoveEventHandler(ActionButton.ActionButton actionButton)
        {
            if (actionButton.EventListeners == null) return;
            foreach (var eventListener in actionButton.EventListeners)
            {
                IMacroDeckEvent macroDeckEvent = EventManager.GetEventByName(eventListener.EventToListen);
                if (macroDeckEvent == null) continue;
                macroDeckEvent.OnEvent -= OnActionButtonEventTrigger;
            }
        }

        private static void OnActionButtonEventTrigger(object sender, MacroDeckEventArgs e)
        {
            Task.Run(() =>
            {
                IMacroDeckEvent macroDeckEvent = (IMacroDeckEvent)sender;
                ActionButton.ActionButton actionButton = e.ActionButton;
                Debug.WriteLine(sender.ToString());
                Debug.WriteLine(e.Parameter);
                
                foreach (EventListener eventListener in actionButton.EventListeners.FindAll(x => x.EventToListen.Equals(macroDeckEvent.Name) && x.Parameter.ToLower().Equals(e.Parameter.ToString().ToLower())))
                {
                    foreach (PluginAction action in eventListener.Actions)
                    {
                        action.Trigger("-1", e.ActionButton);
                    }
                }
            });
        }
    */

        public static MacroDeckFolder FindFolderById(long Id, MacroDeckProfile macroDeckProfile)
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
