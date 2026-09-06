using System.Text.Json;
using System.Text.Json.Nodes;
using MacroDeck.Sdk.Migration;
using MacroDeckHost.Application.Migration;
using MacroDeckHost.Application.Persistence.Profiles;
using MacroDeckHost.Application.Portable;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Domain.Widgets;
using MacroDeckHost.Localization;
using Microsoft.Data.Sqlite;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.Migration.MacroDeck2;

public sealed class MacroDeck2MigrationSource : IMigrationSource
{
	public const string SourceId = "macro-deck-2";

	private const string SourceDisplayName = "Macro Deck 2";

	private readonly IMigrationActionRegistry _migrators;
	private readonly IMacroDeck2MachineKeyReader _machineKey;
	private readonly ILogger _logger;

	internal MacroDeck2MigrationSource(
		IMigrationActionRegistry migrators,
		IMacroDeck2MachineKeyReader machineKey,
		ILogger logger)
	{
		_migrators = migrators;
		_machineKey = machineKey;
		_logger = logger;
	}

	public MacroDeck2MigrationSource(IMigrationActionRegistry migrators, ILogger logger)
		: this(migrators, new MacroDeck2MachineKeyReader(), logger)
	{
	}

	public string Id => SourceId;

	public string Name => SourceDisplayName;

	public string? TryDetectDefaultPath() => MacroDeck2Paths.TryDetectDefaultPath();

	public bool Recognizes(string path)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return false;
		}

		if (!MacroDeck2Archive.IsArchive(path))
		{
			return new MacroDeck2Paths(path).LooksLikeMacroDeck2();
		}

		using var archive = MacroDeck2Archive.TryOpen(path);
		return archive is not null;
	}

	public async Task<Result<MigrationPlan, MigrationError>> Read(
		MigrationRequest request,
		CancellationToken cancellationToken)
	{
		// A backup archive is unpacked into a temporary directory that looks exactly like a live data
		// directory, so everything below reads one shape rather than two.
		if (MacroDeck2Archive.IsArchive(request.Path))
		{
			if (!File.Exists(request.Path))
			{
				return Result.Fail<MigrationPlan, MigrationError>(MigrationError.SourceNotFound,
					"The backup file does not exist");
			}

			var archive = MacroDeck2Archive.TryOpen(request.Path);
			if (archive is null)
			{
				return Result.Fail<MigrationPlan, MigrationError>(MigrationError.SourceNotFound,
					"The file is not a readable Macro Deck 2 backup");
			}

			// The unpacked copy has to outlive this call: the plan names icon files inside it, and those
			// are only opened when it is applied. Ownership passes to the plan, which deletes it.
			var result = await ReadDirectory(request with { Path = archive.Root }, cancellationToken);
			if (result.Success)
			{
				result.Data!.Scope = archive;
			}
			else
			{
				archive.Dispose();
			}

			return result;
		}

		return await ReadDirectory(request, cancellationToken);
	}

	private async Task<Result<MigrationPlan, MigrationError>> ReadDirectory(
		MigrationRequest request,
		CancellationToken cancellationToken)
	{
		var paths = new MacroDeck2Paths(request.Path);
		if (!Directory.Exists(request.Path))
		{
			return Result.Fail<MigrationPlan, MigrationError>(MigrationError.SourceNotFound,
				"The folder does not exist");
		}

		if (!paths.LooksLikeMacroDeck2())
		{
			return Result.Fail<MigrationPlan, MigrationError>(MigrationError.SourceNotFound,
				"The folder is not a Macro Deck 2 data directory");
		}

		var key = ResolveKey(paths, request, out var keyRejected);
		if (keyRejected)
		{
			return Result.Fail<MigrationPlan, MigrationError>(MigrationError.InvalidDecryptionKey,
				"The supplied key does not decrypt this installation's credentials");
		}

		var profiles = ReadProfiles(paths);
		if (profiles.Count == 0)
		{
			return Result.Fail<MigrationPlan, MigrationError>(MigrationError.NothingToMigrate,
				"No Macro Deck 2 profiles were found");
		}

		return Result.Ok<MigrationPlan, MigrationError>(await Translate(paths,
				profiles,
				key,
				request.SkipDecryption,
				cancellationToken)
			.ConfigureAwait(false));
	}

	/// <summary>
	/// The key Macro Deck 2 itself would use, unless the caller supplied one or asked to skip encrypted
	/// data. A supplied key is checked against real ciphertext before it is trusted, so a typo surfaces as
	/// a rejection rather than as silently empty credentials.
	/// </summary>
	private string? ResolveKey(MacroDeck2Paths paths, MigrationRequest request, out bool rejected)
	{
		rejected = false;
		if (request.SkipDecryption || !MacroDeck2PluginDataReader.HasCredentials(paths))
		{
			return null;
		}

		if (!string.IsNullOrWhiteSpace(request.DecryptionKey))
		{
			var supplied = request.DecryptionKey.Trim();
			if (MacroDeck2PluginDataReader.KeyOpens(paths, supplied))
			{
				return supplied;
			}

			rejected = true;
			return null;
		}

		var machineKey = _machineKey.TryRead();
		return machineKey is not null && MacroDeck2PluginDataReader.KeyOpens(paths, machineKey) ? machineKey : null;
	}

	private async Task<MigrationPlan> Translate(
		MacroDeck2Paths paths,
		IReadOnlyList<MacroDeck2Profile> profiles,
		string? key,
		bool skipDecryption,
		CancellationToken cancellationToken)
	{
		var plan = new MigrationPlan { SourceId = SourceId, SourceName = SourceDisplayName };

		var icons = MacroDeck2IconCatalog.Load(paths.IconPacksDirectory);
		var iconIds = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
		var missingIcons = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var variableNames = BuildVariableNames(paths, plan);
		var unsupported = new Dictionary<(string Assembly, string TypeName), int>();

		// Macro Deck 2 did not keep all of a plugin's configuration in its settings file - a SinusBot
		// button names the bot instance to play on, and nothing else does. Every action of a claimed
		// plugin is therefore kept, translated or not, and handed to that plugin's own migration when its
		// settings are read further down.
		var claimedActions = new Dictionary<string, List<ForeignAction>>(StringComparer.OrdinalIgnoreCase);

		var packsTaken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		// A referenced pack is migrated whole. Taking only the icons a button happens to use would leave
		// the pack in Macro Deck 3 looking like a handful of loose images, with nothing to pick from the
		// next time the user edits that button.
		Guid? ResolveIcon(string? reference)
		{
			if (string.IsNullOrWhiteSpace(reference))
			{
				return null;
			}

			if (iconIds.TryGetValue(reference, out var existing))
			{
				return existing;
			}

			var pack = icons.ResolvePack(reference);
			if (pack is null)
			{
				missingIcons.Add(reference);
				return null;
			}

			if (packsTaken.Add(pack.Name))
			{
				foreach (var image in pack.Images())
				{
					var key = $"{image.PackName}.{image.IconId}";
					if (iconIds.ContainsKey(key))
					{
						continue;
					}

					var id = Guid.NewGuid();
					iconIds[key] = id;
					plan.Icons.Add(new MigrationIconRequest(id, image.PackName, image.FilePath, image.IconId));
				}
			}

			if (iconIds.TryGetValue(reference, out var resolved))
			{
				return resolved;
			}

			// The pack is here but this image is not - the icon was deleted from it at some point.
			missingIcons.Add(reference);
			return null;
		}

		string ResolveVariable(string name)
			=> variableNames.TryGetValue(name, out var mapped) ? mapped : name;

		var translator = new MacroDeck2ButtonTranslator(ResolveIcon, ResolveVariable);

		foreach (var profile in profiles)
		{
			cancellationToken.ThrowIfCancellationRequested();
			plan.Profiles.Add(await TranslateProfile(profile, translator, plan, unsupported, claimedActions));
		}

		foreach (var reference in missingIcons.Order(StringComparer.Ordinal))
		{
			plan.Warnings.Add(new MigrationWarning(MigrationWarningKind.MissingIcon,
				reference,
				AppStrings.Migration.Warning.IconPackMissing()));
		}

		plan.UnsupportedActions.AddRange(unsupported
			.OrderByDescending(entry => entry.Value)
			.ThenBy(entry => entry.Key.TypeName, StringComparer.Ordinal)
			.Select(entry => new UnsupportedAction(entry.Key.Assembly, entry.Key.TypeName, entry.Value)));

		await ReadIntegrationConfigs(paths, key, skipDecryption, plan, claimedActions);

		// The same limitation reported once per affected button buries everything else: ten identical lines
		// about one action's lost volume setting say no more than one does.
		var distinct = plan.Warnings.Distinct().ToList();
		plan.Warnings.Clear();
		plan.Warnings.AddRange(distinct);

		return plan;
	}

	private async Task<PortableContent> TranslateProfile(
		MacroDeck2Profile profile,
		MacroDeck2ButtonTranslator translator,
		MigrationPlan plan,
		Dictionary<(string Assembly, string TypeName), int> unsupported,
		Dictionary<string, List<ForeignAction>> claimedActions)
	{
		var folderIds = profile.Folders
			.ToDictionary(folder => folder.FolderId, _ => Guid.NewGuid(), StringComparer.Ordinal);

		var parents = new Dictionary<string, string>(StringComparer.Ordinal);
		foreach (var folder in profile.Folders)
		{
			foreach (var child in folder.Childs)
			{
				parents.TryAdd(child, folder.FolderId);
			}
		}

		var root = profile.Folders.FirstOrDefault(folder => folder.IsRoot) ?? profile.Folders.FirstOrDefault();
		var context = new MacroDeck2ProfileContext(folderIds,
			root is null ? null : folderIds[root.FolderId]);

		var rows = Math.Clamp(profile.Rows <= 0 ? GridDefaults.Rows : profile.Rows,
			GridDefaults.MinRows,
			GridDefaults.MaxRows);
		var columns = Math.Clamp(profile.Columns <= 0 ? GridDefaults.Columns : profile.Columns,
			GridDefaults.MinColumns,
			GridDefaults.MaxColumns);

		var file = new ProfileFile
		{
			Id = Guid.NewGuid(),
			Name = string.IsNullOrWhiteSpace(profile.DisplayName) ? SourceDisplayName : profile.DisplayName,
			LayoutType = ProfileLayoutType.Grid,
			DefaultRows = rows,
			DefaultColumns = columns,
			DefaultWidgetSpacing = profile.ButtonSpacing > 0 ? profile.ButtonSpacing : null,
			DefaultWidgetBorderRadius = BorderRadius(profile.ButtonRadius)
		};

		var order = 0;
		var createdAt = DateTime.UtcNow;
		foreach (var folder in profile.Folders)
		{
			var translated = new ProfileFolder
			{
				Id = folderIds[folder.FolderId],
				Name = folder.IsRoot ? "Root" : folder.DisplayName,
				ParentId = parents.TryGetValue(folder.FolderId, out var parent) &&
					folderIds.TryGetValue(parent, out var parentId)
						? parentId
						: null,
				Order = order,
				CreatedAt = createdAt.AddTicks(order)
			};

			order++;

			if (!string.IsNullOrWhiteSpace(folder.ApplicationToTrigger) || folder.ApplicationsFocusDevices.Count > 0)
			{
				plan.Warnings.Add(new MigrationWarning(MigrationWarningKind.UnsupportedFeature,
					folder.DisplayName,
					AppStrings.Migration.Warning.ApplicationFocusNotMigrated()));
			}

			foreach (var button in folder.ActionButtons)
			{
				if (button.PositionX < 0 ||
					button.PositionY < 0 ||
					button.PositionX >= columns ||
					button.PositionY >= rows)
				{
					plan.Warnings.Add(new MigrationWarning(MigrationWarningKind.SkippedWidget,
						folder.DisplayName,
						AppStrings.Migration.Warning.ButtonOutsideGrid(column: button.PositionX,
							row: button.PositionY,
							columns: columns,
							rows: rows)));
					continue;
				}

				if (button.KeyCode != 0)
				{
					plan.Warnings.Add(new MigrationWarning(MigrationWarningKind.UnsupportedFeature,
						folder.DisplayName,
						AppStrings.Migration.Warning.HotkeyNotMigrated()));
				}

				if (button.ActionsLongPressRelease.Count > 0)
				{
					plan.Warnings.Add(new MigrationWarning(MigrationWarningKind.UnsupportedTrigger,
						folder.DisplayName,
						AppStrings.Migration.Warning.LongPressReleaseNotMigrated()));
				}

				var flows = await MacroDeck2FlowBuilder.Build(button,
					action => TranslateAction(action, context, plan, unsupported, claimedActions));

				translated.Widgets.Add(new ProfileWidget
				{
					Id = Guid.NewGuid(),
					Type = WidgetTypeIds.ActionButton,
					PositionX = button.PositionX,
					PositionY = button.PositionY,
					Width = 1,
					Height = 1,
					Data = translator.Build(button, flows).ToJsonString()
				});
			}

			file.Folders.Add(translated);
		}

		return new PortableContent { Kind = PortableArchiveKind.Profile, Profile = file };
	}

	private async Task<JsonObject> TranslateAction(
		MacroDeck2Action action,
		MacroDeck2ProfileContext context,
		MigrationPlan plan,
		Dictionary<(string Assembly, string TypeName), int> unsupported,
		Dictionary<string, List<ForeignAction>> claimedActions)
	{
		var (typeName, assembly) = action.SplitType();

		var builtIn = MacroDeck2BuiltInActions.Migrate(typeName, action, context);
		if (builtIn is not null)
		{
			plan.MigratedActionCount++;
			return MacroDeck2FlowBuilder.Translated(builtIn);
		}

		var migrator = _migrators.FindByActionSource(MigrationSource.MacroDeck2, assembly);
		if (migrator is null)
		{
			return Unsupported();
		}

		var foreign = new ForeignAction(typeName,
			assembly,
			action.Name,
			action.Configuration,
			action.ConfigurationSummary);

		// Kept whether or not it translates: an action this migration has no equivalent for can still be
		// the only place the source recorded something its configuration needs.
		if (!claimedActions.TryGetValue(assembly, out var claimed))
		{
			claimed = [];
			claimedActions[assembly] = claimed;
		}

		claimed.Add(foreign);

		var translated = await migrator.MigrateActionAsync(foreign, CancellationToken.None);

		if (translated is not null)
		{
			plan.MigratedActionCount++;
			foreach (var warning in translated.Warnings ?? [])
			{
				plan.Warnings.Add(new MigrationWarning(MigrationWarningKind.UnsupportedFeature, typeName, warning));
			}

			return MacroDeck2FlowBuilder.Translated(translated);
		}

		return Unsupported();

		JsonObject Unsupported()
		{
			var key = (assembly, typeName);
			unsupported[key] = unsupported.GetValueOrDefault(key) + 1;
			return MacroDeck2FlowBuilder.Placeholder(action, SourceDisplayName);
		}
	}

	private async Task ReadIntegrationConfigs(
		MacroDeck2Paths paths,
		string? key,
		bool skipDecryption,
		MigrationPlan plan,
		Dictionary<string, List<ForeignAction>> claimedActions)
	{
		var data = MacroDeck2PluginDataReader.Read(paths, key, skipDecryption);
		plan.CredentialStatus = data.CredentialStatus;

		// Two files can claim one integration when a plugin changed its author string between versions.
		// Only the newest is current, so the other is reported rather than silently overwriting it.
		var claimed = new Dictionary<string, string>(StringComparer.Ordinal);
		foreach (var plugin in data.Plugins.OrderByDescending(plugin => plugin.LastWriteUtc))
		{
			var migrator = _migrators.FindBySettingsSource(MigrationSource.MacroDeck2, plugin.Key);
			if (migrator is null)
			{
				continue;
			}

			// Its own actions, gathered under whichever action sources this same migration claims - the
			// two names differ, and only the migration knows both.
			var actions = migrator.ClaimedActionSources
				.SelectMany(source => claimedActions.TryGetValue(source, out var found) ? found : [])
				.ToList();

			var entries = await migrator.MigrateConfigurationAsync(new ForeignPluginSettings(plugin.Key,
						plugin.Settings,
						plugin.Credentials,
						actions),
					CancellationToken.None)
				.ConfigureAwait(false);
			if (entries.Count == 0)
			{
				continue;
			}

			var integrationId = entries[0].IntegrationId;
			if (!claimed.TryAdd(integrationId, plugin.Key))
			{
				plan.Warnings.Add(new MigrationWarning(MigrationWarningKind.DuplicateSourceFile,
					plugin.Key,
					AppStrings.Migration.Warning.DuplicateSettingsFile(name: claimed[integrationId])));
				continue;
			}

			plan.IntegrationConfigs.AddRange(entries);
		}

		// A plugin whose values stayed shut is reported per plugin rather than per run: with a mixed folder
		// the run as a whole succeeded, and only the affected integrations are left unconfigured.
		foreach (var plugin in data.Plugins.Where(plugin => plugin.HadEncryptedValues &&
			plugin.Credentials.Count == 0 &&
			_migrators.FindBySettingsSource(MigrationSource.MacroDeck2, plugin.Key) is not null))
		{
			plan.Warnings.Add(new MigrationWarning(MigrationWarningKind.SkippedCredentials,
				plugin.Key,
				data.CredentialStatus == MigrationCredentialStatus.Skipped
					? AppStrings.Migration.Warning.CredentialsLeftEncrypted()
					: AppStrings.Migration.Warning.CredentialsNotDecrypted()));
		}
	}

	/// <summary>Mirrors the client's own <c>WIDGET_REFERENCE_CELL_SIZE</c>, the cell size every stored
	/// widget pixel measurement is expressed against.</summary>
	private const int ReferenceCellSize = 120;

	/// <summary>
	/// Macro Deck 2's button radius is a percentage of the button's height, and it is the <em>diameter</em>
	/// of the corner arc (<c>RoundedButton.GetFigurePath</c> passes it as the arc's bounding box), so its
	/// default of 40 draws a corner radius of a fifth of the height. Macro Deck 3 stores pixels against a
	/// reference cell instead, so the same look is that fraction of the reference cell - carried across
	/// verbatim, 40 would arrive close to twice as round as it ever looked.
	/// </summary>
	private static int? BorderRadius(int buttonRadius)
		=> buttonRadius > 0
			? Math.Max(1,
				(int)Math.Round(buttonRadius / 100.0 / 2 * ReferenceCellSize, MidpointRounding.AwayFromZero))
			: null;

	private static Dictionary<string, string> BuildVariableNames(MacroDeck2Paths paths, MigrationPlan plan)
	{
		var mapped = new Dictionary<string, string>(StringComparer.Ordinal);
		var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		foreach (var variable in MacroDeck2VariableReader.ReadUserVariables(paths))
		{
			var name = VariableNameSanitizer.IsValid(variable.Name)
				? variable.Name
				: VariableNameSanitizer.Sanitize(variable.Name);

			if (!VariableNameSanitizer.IsValid(name) || !taken.Add(name))
			{
				plan.Warnings.Add(new MigrationWarning(MigrationWarningKind.SkippedVariable,
					variable.Name,
					AppStrings.Migration.Warning.VariableNameCollision()));
				continue;
			}

			mapped[variable.Name] = name;
			plan.Variables.Add(new MigratedVariable(name, variable.Type, variable.Value, null));
		}

		return mapped;
	}

	private List<MacroDeck2Profile> ReadProfiles(MacroDeck2Paths paths)
	{
		var profiles = new List<MacroDeck2Profile>();

		if (Directory.Exists(paths.ProfilesDirectory))
		{
			foreach (var file in Directory.EnumerateFiles(paths.ProfilesDirectory, "*.json")
				.Order(StringComparer.Ordinal))
			{
				var profile = Deserialize(ReadAllText(file));
				if (profile is not null)
				{
					profiles.Add(profile);
				}
			}
		}

		// Only an unsuffixed database is still authoritative. Macro Deck 2 renames it to
		// "profiles.db.migrated" the moment it has written the JSON files beside it, so a file carrying
		// that suffix is a spent tombstone holding older data than what sits next to it.
		if (profiles.Count == 0 && File.Exists(paths.LegacyProfilesDatabase))
		{
			profiles.AddRange(ReadLegacyDatabase(paths.LegacyProfilesDatabase));
		}

		return profiles;
	}

	private List<MacroDeck2Profile> ReadLegacyDatabase(string databasePath)
	{
		var profiles = new List<MacroDeck2Profile>();
		try
		{
			var connectionString = new SqliteConnectionStringBuilder
			{
				DataSource = databasePath,
				Mode = SqliteOpenMode.ReadOnly
			}.ToString();

			using var connection = new SqliteConnection(connectionString);
			connection.Open();

			using var command = connection.CreateCommand();
			command.CommandText = "SELECT JsonString FROM ProfileJson";

			using var reader = command.ExecuteReader();
			while (reader.Read())
			{
				var profile = Deserialize(reader.IsDBNull(0) ? null : reader.GetString(0));
				if (profile is not null)
				{
					profiles.Add(profile);
				}
			}
		}
		catch (SqliteException ex)
		{
			_logger.Warning(ex, "Could not read the legacy Macro Deck 2 profile database at {Path}", databasePath);
		}

		return profiles;
	}

	private string? ReadAllText(string path)
	{
		try
		{
			return File.ReadAllText(path);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			_logger.Warning(ex, "Could not read the Macro Deck 2 profile at {Path}", path);
			return null;
		}
	}

	private MacroDeck2Profile? Deserialize(string? json)
	{
		if (string.IsNullOrWhiteSpace(json))
		{
			return null;
		}

		try
		{
			return JsonSerializer.Deserialize<MacroDeck2Profile>(json, MacroDeck2Json.Options);
		}
		catch (JsonException ex)
		{
			_logger.Warning(ex, "Could not read a Macro Deck 2 profile");
			return null;
		}
	}
}
