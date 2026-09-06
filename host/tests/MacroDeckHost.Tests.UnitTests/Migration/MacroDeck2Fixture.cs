using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using MacroDeckHost.Infrastructure.Migration.MacroDeck2;

namespace MacroDeckHost.Tests.UnitTests.Migration;

/// <summary>
/// Builds a Macro Deck 2 data directory on disk, in the shape its own serializer wrote. Written in code
/// rather than checked in as sample files: a real directory carries live credentials, and the interesting
/// cases here (a long-press-release list, a focus rule, an out-of-grid button) are exactly the ones a real
/// profile tends not to contain.
/// </summary>
internal sealed class MacroDeck2Fixture : IDisposable
{
	private readonly List<JsonObject> _profiles = [];
	private readonly List<(string Name, string Value, string Creator, string Type)> _variables = [];

	public MacroDeck2Fixture()
	{
		Root = Path.Combine(Path.GetTempPath(), "macro-deck-2-fixture", Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(Root);
	}

	public string Root { get; }

	public MacroDeck2Fixture WithConfig()
	{
		File.WriteAllText(Path.Combine(Root, "config.json"), """{"Language":"English"}""");
		return this;
	}

	public MacroDeck2Fixture WithProfile(string profileId, string name, int rows, int columns, JsonArray folders)
	{
		_profiles.Add(new JsonObject
		{
			["ProfileId"] = profileId,
			["DisplayName"] = name,
			["Rows"] = rows,
			["Columns"] = columns,
			["ButtonSpacing"] = 10,
			["ButtonRadius"] = 40,
			["ButtonBackground"] = true,
			["Folders"] = folders
		});

		return this;
	}

	public MacroDeck2Fixture WithVariable(string name, string value, string creator, string type)
	{
		_variables.Add((name, value, creator, type));
		return this;
	}

	public MacroDeck2Fixture WithIconPack(string displayName, params string[] iconIds)
	{
		var directory = Path.Combine(Root, "iconpacks", displayName.Replace('/', '_').Replace(' ', '_'));
		Directory.CreateDirectory(directory);
		File.WriteAllText(Path.Combine(directory, "ExtensionManifest.json"),
			JsonSerializer.Serialize(new { type = 1, name = displayName, packageId = "test." + displayName }));

		foreach (var iconId in iconIds)
		{
			File.WriteAllBytes(Path.Combine(directory, iconId + ".png"), OnePixelPng(iconId));
		}

		return this;
	}

	public MacroDeck2Fixture WithPluginSettings(string pluginKey, object settings)
	{
		var directory = Path.Combine(Root, "configs");
		Directory.CreateDirectory(directory);
		File.WriteAllText(Path.Combine(directory, pluginKey + ".json"), JsonSerializer.Serialize(settings));
		return this;
	}

	/// <summary>
	/// Writes a credential file the way Macro Deck 2 does: plaintext JSON with only the values encrypted,
	/// under a passphrase that on a real installation is the machine's own GUID.
	/// </summary>
	public MacroDeck2Fixture WithPluginCredentials(
		string pluginKey,
		string passPhrase,
		IReadOnlyDictionary<string, string> values)
	{
		var directory = Path.Combine(Root, "credentials");
		Directory.CreateDirectory(directory);

		var encrypted = values.ToDictionary(entry => entry.Key,
			entry => MacroDeck2StringCipher.Encrypt(entry.Value, passPhrase));

		File.WriteAllText(Path.Combine(directory, pluginKey), JsonSerializer.Serialize(new[] { encrypted }));
		return this;
	}

	public MacroDeck2Fixture Build()
	{
		var profiles = Path.Combine(Root, "profiles");
		Directory.CreateDirectory(profiles);

		foreach (var profile in _profiles)
		{
			var id = profile["ProfileId"]!.GetValue<string>();
			File.WriteAllText(Path.Combine(profiles, id + ".json"), profile.ToJsonString());
		}

		if (_variables.Count > 0)
		{
			WriteVariables();
		}

		return this;
	}

	public static JsonObject Folder(string folderId, string displayName, JsonArray buttons, params string[] children)
		=> new()
		{
			["FolderId"] = folderId,
			["DisplayName"] = displayName,
			["Childs"] = new JsonArray([.. children.Select(child => (JsonNode)child!)]),
			["ActionButtons"] = buttons,
			["ApplicationsFocusDevices"] = new JsonArray()
		};

	public static JsonObject Button(
		int x,
		int y,
		string? iconOff = null,
		string? iconOn = null,
		string? backColorOff = null,
		string? backColorOn = null,
		JsonObject? labelOff = null,
		JsonObject? labelOn = null,
		string? stateBindingVariable = null,
		JsonArray? actions = null,
		JsonArray? actionsRelease = null,
		JsonArray? actionsLongPress = null,
		JsonArray? actionsLongPressRelease = null,
		int keyCode = 0)
		=> new()
		{
			["Guid"] = Guid.NewGuid().ToString(),
			["State"] = false,
			["IconOff"] = iconOff,
			["IconOn"] = iconOn,
			["BackColorOff"] = backColorOff,
			["BackColorOn"] = backColorOn,
			["LabelOff"] = labelOff,
			["LabelOn"] = labelOn,
			["Position_X"] = x,
			["Position_Y"] = y,
			["StateBindingVariable"] = stateBindingVariable,
			["Actions"] = actions ?? [],
			["ActionsRelease"] = actionsRelease ?? [],
			["ActionsLongPress"] = actionsLongPress ?? [],
			["ActionsLongPressRelease"] = actionsLongPressRelease ?? [],
			["ModifierKeyCodes"] = 0,
			["KeyCode"] = keyCode
		};

	public static JsonObject Label(string text, int position = 2, string color = "White", double size = 10)
		=> new()
		{
			["LabelText"] = text,
			["LabelPosition"] = position,
			["LabelColor"] = color,
			["Size"] = size,
			["FontFamily"] = "Impact",
			["LabelBase64"] = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAAAAAA="
		};

	public static JsonObject Action(string type, string? configuration = null, string? name = null)
		=> new()
		{
			["$type"] = type,
			["Name"] = name,
			["CanConfigure"] = true,
			["Configuration"] = configuration,
			["ConfigurationSummary"] = configuration
		};

	/// <summary>
	/// Packs the built directory the way a Macro Deck 2 backup is packed: the data directory at the root
	/// of the archive, with the Windows separators its writer produces.
	/// </summary>
	public string PackAsBackup(bool windowsSeparators = true)
	{
		var zipPath = Path.Combine(Path.GetTempPath(), $"md2-backup-{Guid.NewGuid():N}.zip");
		using var stream = File.Create(zipPath);
		using var zip = new ZipArchive(stream, ZipArchiveMode.Create);

		foreach (var file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
		{
			var relative = Path.GetRelativePath(Root, file).Replace(Path.DirectorySeparatorChar, '/');
			var entry = zip.CreateEntry(windowsSeparators ? relative.Replace('/', '\\') : relative);
			using var source = File.OpenRead(file);
			using var destination = entry.Open();
			source.CopyTo(destination);
		}

		return zipPath;
	}

	public void Dispose()
	{
		try
		{
			if (Directory.Exists(Root))
			{
				Directory.Delete(Root, recursive: true);
			}
		}
		catch (IOException)
		{
		}
	}

	private void WriteVariables()
	{
		var path = Path.Combine(Root, "variables.db");
		using var connection = new Microsoft.Data.Sqlite.SqliteConnection(
			new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = path }.ToString());
		connection.Open();

		using (var create = connection.CreateCommand())
		{
			create.CommandText =
				"""CREATE TABLE IF NOT EXISTS "Variable" ("Name" varchar primary key not null, "Value" varchar, "Creator" varchar, "Type" varchar)""";
			create.ExecuteNonQuery();
		}

		foreach (var (name, value, creator, type) in _variables)
		{
			using var insert = connection.CreateCommand();
			insert.CommandText
				= "INSERT INTO Variable (Name, Value, Creator, Type) VALUES ($name, $value, $creator, $type)";
			insert.Parameters.AddWithValue("$name", name);
			insert.Parameters.AddWithValue("$value", value);
			insert.Parameters.AddWithValue("$creator", creator);
			insert.Parameters.AddWithValue("$type", type);
			insert.ExecuteNonQuery();
		}
	}

	/// <summary>A deterministic 1x1 PNG whose bytes differ per icon id, so content-hash dedup can be observed.</summary>
	private static byte[] OnePixelPng(string seed)
	{
		using var image = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(1, 1);
		var hash = (uint)seed.GetHashCode(StringComparison.Ordinal);
		image[0, 0] = new SixLabors.ImageSharp.PixelFormats.Rgba32((byte)(hash >> 16),
			(byte)(hash >> 8),
			(byte)hash,
			255);

		using var stream = new MemoryStream();
		image.Save(stream, new SixLabors.ImageSharp.Formats.Png.PngEncoder());
		return stream.ToArray();
	}
}
