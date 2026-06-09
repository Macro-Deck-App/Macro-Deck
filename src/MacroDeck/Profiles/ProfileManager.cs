using System.IO;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using SQLite;
using SuchByte.MacroDeck.CottleIntegration;
using SuchByte.MacroDeck.Device;
using SuchByte.MacroDeck.Folders;
using Serilog;
using SuchByte.MacroDeck.JSON;
using SuchByte.MacroDeck.Language;
using SuchByte.MacroDeck.Properties;
using SuchByte.MacroDeck.Server;
using SuchByte.MacroDeck.StartupConfig;
using SuchByte.MacroDeck.Utils;
using SuchByte.MacroDeck.Variables;
using SuchByte.MacroDeck.WindowFocus;

namespace SuchByte.MacroDeck.Profiles;

public static class ProfileManager
{
    private static readonly ILogger Logger = Log.ForContext(typeof(ProfileManager));

    public static event EventHandler? ProfilesSaved;
    public static event EventHandler? ProfileCreated;

    public static MacroDeckProfile? CurrentProfile { get; set; }

    public static List<MacroDeckProfile> Profiles { get; private set; } = new();

    private static readonly object _saveLock = new();

    private static readonly JsonSerializerSettings _jsonSettings = new()
    {
        TypeNameHandling = TypeNameHandling.Auto,
        NullValueHandling = NullValueHandling.Ignore,
        ReferenceLoopHandling = ReferenceLoopHandling.Ignore
    };

    private static Dictionary<MacroDeckClient, (MacroDeckFolder PreviousFolder, string ProcessName)> history = new();

    public static void AddVariableChangedListener()
    {
        VariableManager.OnVariableChanged += VariableChanged;
    }

    private static WindowFocusDetection? _windowFocusDetection;

    public static void AddWindowFocusChangedListener()
    {
        _windowFocusDetection = new WindowFocusDetection();
        _windowFocusDetection.OnWindowFocusChanged += OnWindowFocusChanged;

        Application.ApplicationExit += OnApplicationExit;
    }

    private static void OnApplicationExit(object? sender, EventArgs e)
    {
        Application.ApplicationExit -= OnApplicationExit;
        if (_windowFocusDetection != null)
        {
            _windowFocusDetection.OnWindowFocusChanged -= OnWindowFocusChanged;
            _windowFocusDetection.Dispose();
            _windowFocusDetection = null;
        }
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

        var switchBack = history.Where(x =>
            x.Value.ProcessName.Equals(oldProcess) && !x.Key.Folder.ApplicationToTrigger.Equals(newProcess)).ToList();

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
                    .Where(client =>
                        client != null &&
                        client.Profile.Equals(macroDeckProfile) &&
                        !client.Folder.Equals(macroDeckFolder));

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
        Parallel.ForEach(Profiles,
            macroDeckProfile =>
            {
                Parallel.ForEach(macroDeckProfile.Folders,
                    macroDeckFolder =>
                    {
                        Parallel.ForEach(macroDeckFolder.ActionButtons.FindAll(x =>
                                    (x.LabelOff != null &&
                                        !string.IsNullOrWhiteSpace(x.LabelOff.LabelText) &&
                                        x.LabelOff.LabelText.Contains(variable.Name.ToLower(),
                                            StringComparison.OrdinalIgnoreCase)) ||
                                    (x.LabelOn != null &&
                                        !string.IsNullOrWhiteSpace(x.LabelOn.LabelText) &&
                                        x.LabelOn.LabelText.Contains(variable.Name.ToLower(),
                                            StringComparison.OrdinalIgnoreCase)))
                                .ToArray(),
                            UpdateVariableLabels);
                    });
            });
    }

    public static void UpdateVariableLabels(ActionButton.ActionButton actionButton)
    {
        if (actionButton.LabelOff == null || actionButton.LabelOn == null)
        {
            return;
        }

        Task.Run(() =>
        {
            try
            {
                var labelOffText = actionButton.LabelOff.LabelText;
                var labelOnText = actionButton.LabelOn.LabelText;

                labelOffText = TemplateManager.RenderTemplate(labelOffText);
                labelOnText = TemplateManager.RenderTemplate(labelOnText);

                using (var labelOffImage = LabelGenerator.GetLabel(
                    new Bitmap(250, 250),
                    labelOffText,
                    actionButton.LabelOff.LabelPosition,
                    new Font(actionButton.LabelOff.FontFamily, actionButton.LabelOff.Size),
                    actionButton.LabelOff.LabelColor,
                    Color.Black,
                    new SizeF(2.0f, 2.0f)))
                {
                    actionButton.LabelOff.LabelBase64 = Base64.GetBase64FromImage(labelOffImage);
                }

                using (var labelOnImage = LabelGenerator.GetLabel(
                    new Bitmap(250, 250),
                    labelOnText,
                    actionButton.LabelOn.LabelPosition,
                    new Font(actionButton.LabelOn.FontFamily, actionButton.LabelOn.Size),
                    actionButton.LabelOn.LabelColor,
                    Color.Black,
                    new SizeF(2.0f, 2.0f)))
                {
                    actionButton.LabelOn.LabelBase64 = Base64.GetBase64FromImage(labelOnImage);
                }
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
        Logger.Information("Loading profiles...");
        Profiles = new List<MacroDeckProfile>();

        MigrateLegacyDatabase();

        var profilesDir = ApplicationPaths.ProfilesDirectoryPath;
        foreach (var file in Directory.GetFiles(profilesDir, "*.json"))
        {
            try
            {
                var jsonString = File.ReadAllText(file);
                var settings = new JsonSerializerSettings
                {
                    TypeNameHandling = TypeNameHandling.Auto,
                    NullValueHandling = NullValueHandling.Ignore,
                    Error = (sender, args) =>
                    {
                        Logger.Error(args.ErrorContext.Error, "Error while deserializing profile {File}", file);
                        args.ErrorContext.Handled = true;
                    },
                    ReferenceLoopHandling = ReferenceLoopHandling.Ignore
                };
                var profile = JsonConvert.DeserializeObject<MacroDeckProfile>(jsonString, settings);
                if (profile is null)
                {
                    continue;
                }

                if (profile.ProfileId == "")
                {
                    profile.ProfileId = Profiles.Count.ToString();
                }

                Profiles.Add(profile);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to load profile from {File}", file);
            }
        }

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
            if (CurrentProfile != null)
            {
                Settings.Default.SelectedProfile = CurrentProfile.ProfileId;
            }

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
        foreach (var actionButton in from macroDeckProfile in Profiles
            from macroDeckFolder in macroDeckProfile.Folders
            from actionButton in macroDeckFolder.ActionButtons
            select actionButton)
        {
            actionButton.UpdateBindingState();
            actionButton.UpdateHotkey();
            UpdateVariableLabels(actionButton);
            foreach (var pluginAction in actionButton.Actions)
            {
                pluginAction.SetActionButton(actionButton);
            }
        }

        Logger.Information("Loaded {ProfileCount} profiles", Profiles.Count);
    }

    public static void Save()
    {
        if (MacroDeck.SafeMode)
        {
            return;
        }

        lock (_saveLock)
        {
            var profilesDir = ApplicationPaths.ProfilesDirectoryPath;
            var activeIds = new HashSet<string>();

            foreach (var profile in Profiles)
            {
                var profileId = profile.ProfileId;
                activeIds.Add(profileId);

                string jsonString;
                try
                {
                    jsonString = JsonConvert.SerializeObject(profile, new JsonSerializerSettings
                    {
                        TypeNameHandling = TypeNameHandling.Auto,
                        NullValueHandling = NullValueHandling.Ignore,
                        Error = (sender, args) =>
                        {
                            Logger.Error(args.ErrorContext.Error, "Error while serializing profile {ProfileId}", profileId);
                            args.ErrorContext.Handled = true;
                        },
                        ReferenceLoopHandling = ReferenceLoopHandling.Ignore
                    });
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Failed to serialize profile {ProfileId}", profileId);
                    continue;
                }

                var finalPath = Path.Combine(profilesDir, $"{profileId}.json");
                var tmpPath = finalPath + ".tmp";
                File.WriteAllText(tmpPath, jsonString);
                File.Move(tmpPath, finalPath, overwrite: true);
            }

            foreach (var file in Directory.GetFiles(profilesDir, "*.json"))
            {
                var id = Path.GetFileNameWithoutExtension(file);
                if (!activeIds.Contains(id))
                {
                    try { File.Delete(file); }
                    catch (Exception ex) { Logger.Warning(ex, "Failed to delete orphaned profile file {File}", file); }
                }
            }
        }

        Logger.Debug("Saved {ProfileCount} profiles", Profiles.Count);
        ProfilesSaved?.Invoke(Profiles, EventArgs.Empty);
    }

    private static void MigrateLegacyDatabase()
    {
        var legacyPath = ApplicationPaths.ProfilesLegacyFilePath;
        var profilesDir = ApplicationPaths.ProfilesDirectoryPath;

        if (!File.Exists(legacyPath))
        {
            return;
        }

        if (Directory.GetFiles(profilesDir, "*.json").Length > 0)
        {
            return;
        }

        Logger.Information("Migrating profiles from legacy SQLite database...");

        try
        {
            var db = new SQLiteConnection(legacyPath);
            db.CreateTable<ProfileJson>();
            var entries = db.Table<ProfileJson>().ToList();
            db.Close();

            var migrated = 0;
            foreach (var entry in entries)
            {
                try
                {
                    var settings = new JsonSerializerSettings
                    {
                        TypeNameHandling = TypeNameHandling.Auto,
                        NullValueHandling = NullValueHandling.Ignore,
                        Error = (sender, args) =>
                        {
                            Logger.Error(args.ErrorContext.Error, "Error while deserializing legacy profile entry");
                            args.ErrorContext.Handled = true;
                        },
                        ReferenceLoopHandling = ReferenceLoopHandling.Ignore
                    };
                    var profile = JsonConvert.DeserializeObject<MacroDeckProfile>(entry.JsonString, settings);
                    if (profile is null)
                    {
                        continue;
                    }

                    if (string.IsNullOrEmpty(profile.ProfileId))
                    {
                        profile.ProfileId = migrated.ToString();
                    }

                    var finalPath = Path.Combine(profilesDir, $"{profile.ProfileId}.json");
                    var tmpPath = finalPath + ".tmp";
                    File.WriteAllText(tmpPath, entry.JsonString);
                    File.Move(tmpPath, finalPath, overwrite: true);
                    migrated++;
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Failed to migrate legacy profile entry");
                }
            }

            var migratedDbPath = legacyPath + ".migrated";
            File.Move(legacyPath, migratedDbPath, overwrite: true);
            Logger.Information("Migrated {Count} profiles from SQLite to JSON files. Legacy database renamed to {MigratedPath}", migrated, migratedDbPath);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to migrate legacy profiles database");
        }
    }

    public static MacroDeckFolder CreateFolder(string displayName,
        MacroDeckFolder parent,
        MacroDeckProfile macroDeckProfile)
    {
        if (macroDeckProfile.Folders.FindAll(macroDeckFolder => macroDeckFolder.DisplayName.Equals(displayName)).Count >
            0)
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

        Logger.Information("Created folder {FolderName} in {ProfileName}", displayName, macroDeckProfile.DisplayName);

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
        if (!macroDeckProfile.Folders.Contains(folder))
        {
            return;
        }

        if (macroDeckProfile.Folders.FirstOrDefault()!.Equals(folder))
        {
            return;
        }

        if (folder.ActionButtons != null)
        {
            foreach (var actionButton in folder.ActionButtons)
            {
                actionButton.Dispose();
            }
        }

        foreach (var macroDeckClient in MacroDeckServer.Clients.FindAll(macroDeckClient =>
            macroDeckClient.Folder.FolderId.Equals(folder.FolderId)))
        {
            MacroDeckServer.SetFolder(macroDeckClient, macroDeckProfile.Folders[0]);
        }

        foreach (var child in folder.Childs.Select(childId => FindFolderById(childId, macroDeckProfile)).ToArray())
        {
            DeleteFolder(child, macroDeckProfile);
        }

        foreach (var folders in macroDeckProfile.Folders.FindAll(macroDeckFolder =>
            macroDeckFolder.Childs.Contains(folder.FolderId)))
        {
            folders.Childs.Remove(folder.FolderId);
        }

        Logger.Information("Delete {FolderName} in {ProfileName}", folder.DisplayName, macroDeckProfile.DisplayName);

        macroDeckProfile.Folders.Remove(folder);
        Save();
    }


    public static MacroDeckProfile CreateProfile(string displayName,
        DeviceClass deviceClass = DeviceClass.SoftwareClient)
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
            ProfileId = GenerateFolderId(displayName)
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

        Logger.Information("Created profile {ProfileName}", displayName);

        return newProfile;
    }

    public static void DeleteProfile(MacroDeckProfile macroDeckProfile)
    {
        if (!Profiles.Contains(macroDeckProfile))
        {
            return;
        }

        if (Profiles.Count < 2)
        {
            return;
        }

        foreach (var macroDeckFolder in macroDeckProfile.Folders)
        {
            macroDeckFolder.Dispose();
        }

        foreach (var macroDeckDevice in DeviceManager.GetKnownDevices().FindAll(macroDeckDevice =>
            macroDeckDevice.ProfileId != null && macroDeckDevice.ProfileId.Equals(macroDeckProfile.ProfileId)))
        {
            DeviceManager.SetProfile(macroDeckDevice, Profiles[0]);
        }

        Logger.Information("Delete profile {ProfileName}", macroDeckProfile.DisplayName);

        macroDeckProfile.Dispose();

        Profiles.Remove(macroDeckProfile);

        Save();
    }

    private static string GenerateFolderId(string folderName)
    {
        var rgx = new Regex("[^a-zA-Z0-9 -]");
        return DateTimeOffset.Now.ToUnixTimeMilliseconds() + rgx.Replace(folderName.ToLower(), "");
    }

    public static MacroDeckFolder FindFolderById(string Id, MacroDeckProfile macroDeckProfile)
    {
        return macroDeckProfile.Folders.Find(macroDeckFolder => macroDeckFolder.FolderId.Equals(Id));
    }


    public static MacroDeckFolder FindFolderByDisplayName(string displayName, MacroDeckProfile macroDeckProfile)
    {
        return macroDeckProfile.Folders.Find(macroDeckFolder => macroDeckFolder.DisplayName.Equals(displayName));
    }

    public static ActionButton.ActionButton FindActionButton(MacroDeckFolder folder, int row, int col)
    {
        return folder.ActionButtons.Find(actionButton =>
            actionButton.Position_X == col && actionButton.Position_Y == row);
    }

    public static MacroDeckProfile FindProfileById(string id)
    {
        return Profiles.Find(macroDeckProfile => macroDeckProfile.ProfileId.Equals(id));
    }

    public static MacroDeckProfile FindProfileByDisplayName(string displayName)
    {
        return Profiles.Find(macroDeckProfile => macroDeckProfile.DisplayName.Equals(displayName));
    }
}
