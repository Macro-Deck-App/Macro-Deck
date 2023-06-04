using System.Drawing;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using SQLite;
using SuchByte.MacroDeck.CottleIntegration;
using SuchByte.MacroDeck.Device;
using SuchByte.MacroDeck.Folders;
using SuchByte.MacroDeck.JSON;
using SuchByte.MacroDeck.Language;
using SuchByte.MacroDeck.Logging;
using SuchByte.MacroDeck.Properties;
using SuchByte.MacroDeck.Server;
using SuchByte.MacroDeck.Startup;
using SuchByte.MacroDeck.Utils;
using SuchByte.MacroDeck.Variables;
using SuchByte.MacroDeck.WindowsFocus;

namespace SuchByte.MacroDeck.Profiles;

public static class ProfileManager
{
    public static event EventHandler? ProfilesSaved;
    public static event EventHandler? ProfileCreated;

    public static MacroDeckProfile? CurrentProfile { get; set; }
        
    public static List<MacroDeckProfile> Profiles { get; private set; } = new();
    
    private static Dictionary<MacroDeckClient, (MacroDeckFolder PreviousFolder, string ProcessName)> history = new ();

    public static void AddVariableChangedListener()
    {
        VariableManager.OnVariableChanged += VariableChanged;
    }
        
    public static void AddWindowFocusChangedListener()
    {
        var windowsFocusDetection = new WindowFocusDetection();
        windowsFocusDetection.OnWindowFocusChanged += OnWindowFocusChanged;
    }

    private static void OnWindowFocusChanged(object sender, WindowChangedEventArgs e)
    {
        Task.Run(() => UpdateSetFolderForProcess(e.NewProcess, e.PreviousProcess));
    }

    private static void UpdateSetFolderForProcess(string newProcess, string oldProcess)
    {
        var affectedMacroDeckProfiles = Profiles
            .Where(profile => profile.Folders.Any(folder => folder.ApplicationToTrigger.Equals(newProcess)))
            .ToList();

        var switchBack = history.Where(x => x.Value.ProcessName.Equals(oldProcess) 
                                            && !x.Key.Folder.ApplicationToTrigger.Equals(newProcess)).ToList();

        foreach (var pair in switchBack)
        {
            MacroDeckServer.SetFolder(pair.Key, pair.Value.PreviousFolder);
        }

        foreach (var macroDeckProfile in affectedMacroDeckProfiles)
        {
            var folders = macroDeckProfile.Folders
                .Where(folder => folder.ApplicationToTrigger.Equals(newProcess));

            foreach (var macroDeckFolder in folders)
            {
                var devices = macroDeckFolder.ApplicationsFocusDevices
                    .Select(DeviceManager.GetMacroDeckDevice)
                    .Where(device => device != null && device.Available && !string.IsNullOrWhiteSpace(device.ClientId))
                    .ToList();

                var clients = devices
                    .Select(device => MacroDeckServer.GetMacroDeckClient(device.ClientId))
                    .Where(client => client != null && client.Profile.Equals(macroDeckProfile) && !client.Folder.Equals(macroDeckFolder));

                foreach (var macroDeckClient in clients)
                {
                    history[macroDeckClient] = (macroDeckClient.Folder, newProcess);
                    MacroDeckServer.SetFolder(macroDeckClient, macroDeckFolder);
                }
            }
        }
    }

    private static void VariableChanged(object sender, EventArgs e)
    {
        if (sender is Variable variable)
        {
            UpdateAllVariableLabels(variable);
        }
    }

    public static void UpdateAllVariableLabels(Variable variable)
    {
        Parallel.ForEach(Profiles, macroDeckProfile =>
        {
            Parallel.ForEach(macroDeckProfile.Folders, macroDeckFolder =>
            {
                Parallel.ForEach(macroDeckFolder.ActionButtons.FindAll(x => (x.LabelOff != null && !string.IsNullOrWhiteSpace(x.LabelOff.LabelText) && x.LabelOff.LabelText.Contains(variable.Name.ToLower(), StringComparison.OrdinalIgnoreCase)) ||
                                                                            (x.LabelOn != null && !string.IsNullOrWhiteSpace(x.LabelOn.LabelText) && x.LabelOn.LabelText.Contains(variable.Name.ToLower(), StringComparison.OrdinalIgnoreCase))).ToArray(),
                    UpdateVariableLabels);
            });
        });
    }

    public static void UpdateVariableLabels(ActionButton.ActionButton actionButton)
    {
        if (actionButton.LabelOff == null || actionButton.LabelOn == null) return;
        Task.Run(() =>
        {
            try
            {
                var labelOffText = actionButton.LabelOff.LabelText;
                var labelOnText = actionButton.LabelOn.LabelText;

                labelOffText = TemplateManager.RenderTemplate(labelOffText);
                labelOnText = TemplateManager.RenderTemplate(labelOnText);

                actionButton.LabelOff.LabelBase64 = Base64.GetBase64FromImage(LabelGenerator.GetLabel(new Bitmap(250, 250), labelOffText, actionButton.LabelOff.LabelPosition, new Font(actionButton.LabelOff.FontFamily, actionButton.LabelOff.Size), actionButton.LabelOff.LabelColor, Color.Black, new SizeF(2.0f, 2.0f)));
                actionButton.LabelOn.LabelBase64 = Base64.GetBase64FromImage(LabelGenerator.GetLabel(new Bitmap(250, 250), labelOnText, actionButton.LabelOn.LabelPosition, new Font(actionButton.LabelOn.FontFamily, actionButton.LabelOn.Size), actionButton.LabelOn.LabelColor, Color.Black, new SizeF(2.0f, 2.0f)));
                foreach (var macroDeckClient in MacroDeckServer.Clients)
                {
                    MacroDeckServer.SendButton(macroDeckClient, actionButton);
                }
            }
            catch
            {
                // ignored
            }
        });
    }

    internal static void Load()
    {
        MacroDeckLogger.Info(typeof(ProfileManager), "Loading profiles...");
        Profiles = new List<MacroDeckProfile>();
        var databasePath = ApplicationPaths.ProfilesFilePath;

        var db = new SQLiteConnection(databasePath);
        db.CreateTable<ProfileJson>();

        var query = db.Table<ProfileJson>();


        foreach (var folderJson in query)
        {
            var jsonString = folderJson.JsonString;
            var profile = JsonConvert.DeserializeObject<MacroDeckProfile>(jsonString, new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.Auto,
                NullValueHandling = NullValueHandling.Ignore,
                Error = (sender, args) => {
                    MacroDeckLogger.Error("Error while deserializing the profiles file: " + args.ErrorContext.Error.Message);
                    args.ErrorContext.Handled = true; 
                },
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
            });
            if (profile is { ProfileId: "" })
            {
                profile.ProfileId = Profiles.Count.ToString();
            }
            Profiles.Add(profile);
        }

        db.Close();

        if (Profiles.Count == 0)
        {
            var defaultProfile = new MacroDeckProfile
            {
                DisplayName = LanguageManager.Strings.Profile + " 1",
                Columns = 5,
                Rows = 3,
                Folders = new List<MacroDeckFolder>()
            };

            Profiles.Add(defaultProfile);

            Save();
        }

        if (!string.IsNullOrWhiteSpace(Settings.Default.SelectedProfile))
        {
            CurrentProfile = FindProfileById(Settings.Default.SelectedProfile);
        }

        if (CurrentProfile == null)
        {
            CurrentProfile = Profiles.FirstOrDefault();
            if (CurrentProfile != null) Settings.Default.SelectedProfile = CurrentProfile.ProfileId;
            Settings.Default.Save();
        }


        if (CurrentProfile != null && CurrentProfile.Folders.Count < 1)
        {
            var root = new MacroDeckFolder
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
        foreach (var actionButton in from macroDeckProfile in Profiles from macroDeckFolder in macroDeckProfile.Folders from actionButton in macroDeckFolder.ActionButtons select actionButton)
        {
            actionButton.UpdateBindingState();
            actionButton.UpdateHotkey();
            UpdateVariableLabels(actionButton);
            foreach (var pluginAction in actionButton.Actions)
            {
                pluginAction.SetActionButton(actionButton);
            }
        }

        MacroDeckLogger.Info("Loaded " + Profiles.Count + " profiles");
    }

    public static void Save()
    {
        if (MacroDeck.SafeMode)
        {
            return;
        }
        var databasePath = ApplicationPaths.ProfilesFilePath;

        var db = new SQLiteConnection(databasePath);
        db.CreateTable<ProfileJson>();
        db.DeleteAll<ProfileJson>();

        foreach (var profile in Profiles)
        {
            var jsonString = JsonConvert.SerializeObject(profile, new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.Auto,
                NullValueHandling = NullValueHandling.Ignore,
                Error = (sender, args) => {
                    MacroDeckLogger.Error("Error while serializing the profiles: " + args.ErrorContext.Error.Message);
                    args.ErrorContext.Handled = true;
                },
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
            });

            var profileJson = new ProfileJson
            {
                JsonString = jsonString
            };

            db.InsertOrReplace(profileJson);
        }

        db.Close();
        MacroDeckLogger.Info("Saved " + Profiles.Count + " profiles");
        ProfilesSaved?.Invoke(Profiles, EventArgs.Empty);
    }
    public static MacroDeckFolder CreateFolder(string displayName, MacroDeckFolder parent, MacroDeckProfile macroDeckProfile)
    {
        if (macroDeckProfile.Folders.FindAll(macroDeckFolder => macroDeckFolder.DisplayName.Equals(displayName)).Count > 0)
        {
            return null;
        }

        var newFolder = new MacroDeckFolder
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
        if (macroDeckProfile.Folders.FirstOrDefault()!.Equals(folder)) return;

        if (folder.ActionButtons != null)
        {
            foreach (var actionButton in folder.ActionButtons)
            {
                actionButton.Dispose();
            }
        }

        foreach (var macroDeckClient in MacroDeckServer.Clients.FindAll(macroDeckClient => macroDeckClient.Folder.FolderId.Equals(folder.FolderId)))
        {
            MacroDeckServer.SetFolder(macroDeckClient, macroDeckProfile.Folders[0]);
        }
            
        foreach (var child in folder.Childs.Select(childId => FindFolderById(childId, macroDeckProfile)))
        {
            DeleteFolder(child, macroDeckProfile);
        }

        foreach (var folders in macroDeckProfile.Folders.FindAll(macroDeckFolder => macroDeckFolder.Childs.Contains(folder.FolderId)))
        {
            folders.Childs.Remove(folder.FolderId);
        }

        MacroDeckLogger.Info("Delete " + folder.DisplayName + " in " + macroDeckProfile.DisplayName);
            
        macroDeckProfile.Folders.Remove(folder);
        Save();
    }


    public static MacroDeckProfile CreateProfile(string displayName, DeviceClass deviceClass = DeviceClass.SoftwareClient)
    {
        if (Profiles.FindAll(macroDeckProfile => macroDeckProfile.DisplayName.Equals(displayName)).Count > 0)
        {
            return Profiles.Find(macroDeckProfile => macroDeckProfile.DisplayName.Equals(displayName));
        }

        var newProfile = new MacroDeckProfile
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

        var rootFolder = new MacroDeckFolder
        {
            DisplayName = "*Root*",
            FolderId = GenerateFolderId("*Root*"),
            Childs = new List<string>(),
            ActionButtons = new List<ActionButton.ActionButton>()
        };

        newProfile.Folders.Add(rootFolder);

        Profiles.Add(newProfile);

        Save();

        ProfileCreated?.Invoke(newProfile, EventArgs.Empty);

        MacroDeckLogger.Info("Created profile " + displayName);

        return newProfile;
    }

    public static void DeleteProfile(MacroDeckProfile macroDeckProfile)
    {
        if (!Profiles.Contains(macroDeckProfile)) return;
        if (Profiles.Count < 2) return;

        foreach (var macroDeckFolder in macroDeckProfile.Folders)
        {
            macroDeckFolder.Dispose();
        }

        foreach (var macroDeckDevice in DeviceManager.GetKnownDevices().FindAll(macroDeckDevice => macroDeckDevice.ProfileId != null && macroDeckDevice.ProfileId.Equals(macroDeckProfile.ProfileId)))
        {
            DeviceManager.SetProfile(macroDeckDevice, Profiles[0]);
        }

        MacroDeckLogger.Info("Delete profile " + macroDeckProfile.DisplayName);

        macroDeckProfile.Dispose();

        Profiles.Remove(macroDeckProfile);

        Save();
    }

    private static string GenerateFolderId(string folderName)
    {
        var rgx = new Regex("[^a-zA-Z0-9 -]");
        return DateTimeOffset.Now.ToUnixTimeMilliseconds() + rgx.Replace(folderName.ToLower(), "");
    }

    public static MacroDeckFolder FindFolderById(string Id, MacroDeckProfile macroDeckProfile) => 
        macroDeckProfile.Folders.Find(macroDeckFolder => macroDeckFolder.FolderId.Equals(Id));


    public static MacroDeckFolder FindFolderByDisplayName(string displayName, MacroDeckProfile macroDeckProfile) => 
        macroDeckProfile.Folders.Find(macroDeckFolder => macroDeckFolder.DisplayName.Equals(displayName));

    public static ActionButton.ActionButton FindActionButton(MacroDeckFolder folder, int row, int col) => 
        folder.ActionButtons.Find(actionButton => actionButton.Position_X == col && actionButton.Position_Y == row);

    public static MacroDeckProfile FindProfileById(string id) => 
        Profiles.Find(macroDeckProfile => macroDeckProfile.ProfileId.Equals(id));

    public static MacroDeckProfile FindProfileByDisplayName(string displayName) => 
        Profiles.Find(macroDeckProfile => macroDeckProfile.DisplayName.Equals(displayName));
}