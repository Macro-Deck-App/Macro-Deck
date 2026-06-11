using System.Collections.Concurrent;
using System.IO;
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

    public static List<MacroDeckProfile> Profiles { get; private set; } = [];

    private static readonly Lock SaveLock = new();

    private static readonly ConcurrentDictionary<MacroDeckClient, (MacroDeckFolder PreviousFolder, string ProcessName)>
        History = new();

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
        _ = Task.Run(() => UpdateSetFolderForProcess(e.NewProcess, e.PreviousProcess));
    }

    private static void UpdateSetFolderForProcess(string newProcess, string oldProcess)
    {
        if (string.IsNullOrWhiteSpace(newProcess))
        {
            return;
        }

        foreach (var pair in History
            .Where(x =>
                string.Equals(x.Value.ProcessName, oldProcess, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(x.Key.Folder?.ApplicationToTrigger,
                    newProcess,
                    StringComparison.OrdinalIgnoreCase))
            .ToArray())
        {
            MacroDeckServer.SetFolder(pair.Key, pair.Value.PreviousFolder);
        }

        var affectedProfiles = Profiles
            .Where(profile =>
                profile.Folders.Any(folder =>
                    string.Equals(folder.ApplicationToTrigger,
                        newProcess,
                        StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        foreach (var profile in affectedProfiles)
        {
            foreach (var folder in profile.Folders.Where(folder =>
                string.Equals(folder.ApplicationToTrigger,
                    newProcess,
                    StringComparison.OrdinalIgnoreCase)))
            {
                foreach (var deviceId in folder.ApplicationsFocusDevices)
                {
                    var device = DeviceManager.GetMacroDeckDevice(deviceId);

                    if (device is null ||
                        !device.Available ||
                        string.IsNullOrWhiteSpace(device.ClientId))
                    {
                        continue;
                    }

                    var client = MacroDeckServer.GetMacroDeckClient(device.ClientId);

                    if (client is null ||
                        !ReferenceEquals(client.Profile, profile) ||
                        ReferenceEquals(client.Folder, folder))
                    {
                        continue;
                    }

                    History[client] = (client.Folder, newProcess);
                    MacroDeckServer.SetFolder(client, folder);
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
        if (string.IsNullOrWhiteSpace(variable?.Name))
        {
            return;
        }

        var variableName = variable.Name;

        var buttons = Profiles
            .SelectMany(profile => profile.Folders)
            .SelectMany(folder => folder.ActionButtons)
            .Where(button =>
                (button.LabelOff?.LabelText.Contains(variableName, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (button.LabelOn?.LabelText.Contains(variableName, StringComparison.OrdinalIgnoreCase) ?? false))
            .Distinct()
            .ToArray();

        Parallel.ForEach(buttons, UpdateVariableLabels);
    }

    public static void UpdateVariableLabels(ActionButton.ActionButton actionButton)
    {
        if (actionButton?.LabelOff == null || actionButton.LabelOn == null)
        {
            return;
        }

        try
        {
            var labelOffText = TemplateManager.RenderTemplate(actionButton.LabelOff.LabelText);
            var labelOnText = TemplateManager.RenderTemplate(actionButton.LabelOn.LabelText);

            using (var labelOffBitmap = new Bitmap(250, 250))
            using (var labelOffFont = new Font(actionButton.LabelOff.FontFamily, actionButton.LabelOff.Size))
            using (var labelOffImage = LabelGenerator.GetLabel(labelOffBitmap,
                labelOffText,
                actionButton.LabelOff.LabelPosition,
                labelOffFont,
                actionButton.LabelOff.LabelColor,
                Color.Black,
                new SizeF(2f, 2f)))
            {
                actionButton.LabelOff.LabelBase64 = Base64.GetBase64FromImage(labelOffImage);
            }

            using (var labelOnBitmap = new Bitmap(250, 250))
            using (var labelOnFont = new Font(actionButton.LabelOn.FontFamily, actionButton.LabelOn.Size))
            using (var labelOnImage = LabelGenerator.GetLabel(labelOnBitmap,
                labelOnText,
                actionButton.LabelOn.LabelPosition,
                labelOnFont,
                actionButton.LabelOn.LabelColor,
                Color.Black,
                new SizeF(2f, 2f)))
            {
                actionButton.LabelOn.LabelBase64 = Base64.GetBase64FromImage(labelOnImage);
            }

            foreach (var client in MacroDeckServer.Clients)
            {
                MacroDeckServer.SendButton(client, actionButton);
            }
        }
        catch
        {
            // ignored
        }
    }

    internal static void Load()
    {
        Logger.Information("Loading profiles...");
        Profiles = [];

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
                    profile.ProfileId = Guid.CreateVersion7().ToString();
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
                Folders = []
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
                FolderId = Guid.CreateVersion7().ToString(),
                DisplayName = "*Root*",
                Childs = [],
                ActionButtons = []
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
                pluginAction?.SetActionButton(actionButton);
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

        lock (SaveLock)
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
                    jsonString = JsonConvert.SerializeObject(profile,
                        new JsonSerializerSettings
                        {
                            TypeNameHandling = TypeNameHandling.Auto,
                            NullValueHandling = NullValueHandling.Ignore,
                            Error = (sender, args) =>
                            {
                                Logger.Error(args.ErrorContext.Error,
                                    "Error while serializing profile {ProfileId}",
                                    profileId);
                                args.ErrorContext.Handled = true;
                            },
                            ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                            Formatting = Formatting.Indented
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
                    try
                    {
                        File.Delete(file);
                    }
                    catch (Exception ex)
                    {
                        Logger.Warning(ex, "Failed to delete orphaned profile file {File}", file);
                    }
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
            Logger.Information(
                "Migrated {Count} profiles from SQLite to JSON files. Legacy database renamed to {MigratedPath}",
                migrated,
                migratedDbPath);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to migrate legacy profiles database");
        }
    }

    public static MacroDeckFolder? CreateFolder(string displayName,
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
            Childs = [],
            ActionButtons = [],
            FolderId = Guid.CreateVersion7().ToString()
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
            Folders = [],
            ProfileId = Guid.CreateVersion7().ToString()
        };

        var rootFolder = new MacroDeckFolder
        {
            DisplayName = "*Root*",
            FolderId = Guid.CreateVersion7().ToString(),
            Childs = [],
            ActionButtons = []
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

    public static MacroDeckFolder? FindFolderById(string id, MacroDeckProfile macroDeckProfile)
    {
        return macroDeckProfile.Folders.FirstOrDefault(x => x.FolderId.Equals(id));
    }


    public static MacroDeckFolder? FindFolderByDisplayName(string displayName, MacroDeckProfile macroDeckProfile)
    {
        return macroDeckProfile.Folders.FirstOrDefault(macroDeckFolder =>
            macroDeckFolder.DisplayName.Equals(displayName));
    }

    public static ActionButton.ActionButton? FindActionButton(MacroDeckFolder folder, int row, int col)
    {
        return folder.ActionButtons.FirstOrDefault(actionButton =>
            actionButton.Position_X == col && actionButton.Position_Y == row);
    }

    public static MacroDeckProfile? FindProfileById(string id)
    {
        return Profiles.FirstOrDefault(macroDeckProfile => macroDeckProfile.ProfileId.Equals(id));
    }

    public static MacroDeckProfile? FindProfileByDisplayName(string displayName)
    {
        return Profiles.FirstOrDefault(macroDeckProfile => macroDeckProfile.DisplayName.Equals(displayName));
    }
}
