using System.Text.Json;
using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Infrastructure.Migration.MacroDeck2;

/// <summary>
/// One foreign plugin's settings and credentials, keyed by the file name Macro Deck 2 derived from the
/// plugin's author and its (overridable) name. Configuration files carry a <c>.json</c> suffix and
/// credential files carry none, so the key is the file name with any suffix removed.
/// </summary>
/// <remarks>
/// <paramref name="LastWriteUtc" /> exists because a plugin that changed its author string between
/// versions leaves a file behind under its previous key. Both keys claim the same integration, and only
/// the timestamp says which one is still current.
/// </remarks>
internal sealed record MacroDeck2PluginData(
	string Key,
	IReadOnlyDictionary<string, string> Settings,
	IReadOnlyList<IReadOnlyDictionary<string, string>> Credentials,
	DateTime LastWriteUtc)
{
	/// <summary>
	/// Whether this plugin had encrypted values at all. Distinguishes "stores no credentials" from "stores
	/// credentials none of which opened", which is a real state: Macro Deck 2 keys every value to the
	/// machine GUID at the time it was written, so a file left over from before a Windows reinstall stays
	/// shut even while the rest of the folder opens.
	/// </summary>
	public bool HadEncryptedValues { get; init; }
}

internal sealed record MacroDeck2PluginDataSet(
	IReadOnlyList<MacroDeck2PluginData> Plugins,
	MigrationCredentialStatus CredentialStatus);

internal static class MacroDeck2PluginDataReader
{
	/// <summary>
	/// Reads every settings and credential file, decrypting credential values with
	/// <paramref name="decryptionKey" />. A null key means none is available, which is reported rather
	/// than guessed at: the caller then offers to skip encrypted data or to be given a key.
	/// </summary>
	public static MacroDeck2PluginDataSet Read(MacroDeck2Paths paths, string? decryptionKey, bool skipDecryption)
	{
		var settings = ReadAll<Dictionary<string, string>>(paths.PluginConfigDirectory);
		var encrypted = ReadAll<List<Dictionary<string, string>>>(paths.PluginCredentialsDirectory);

		var status = ResolveStatus(encrypted, decryptionKey, skipDecryption);

		var keys = settings.Keys.Concat(encrypted.Keys).Distinct(StringComparer.OrdinalIgnoreCase);
		var plugins = new List<MacroDeck2PluginData>();
		foreach (var key in keys)
		{
			settings.TryGetValue(key, out var setting);
			encrypted.TryGetValue(key, out var credential);

			var decrypted = status == MigrationCredentialStatus.Decrypted && decryptionKey is not null
				? Decrypt(credential?.Value, decryptionKey)
				: [];

			var lastWrite = new[] { setting?.LastWriteUtc, credential?.LastWriteUtc }
					.Where(timestamp => timestamp.HasValue)
					.Max() ??
				DateTime.MinValue;

			plugins.Add(new MacroDeck2PluginData(key,
				setting?.Value ?? new Dictionary<string, string>(StringComparer.Ordinal),
				decrypted,
				lastWrite)
			{
				HadEncryptedValues = credential?.Value.Any(set => set.Count > 0) ?? false
			});
		}

		return new MacroDeck2PluginDataSet(plugins, status);
	}

	/// <summary>
	/// Whether <paramref name="candidate" /> actually opens this installation's credentials. Every stored
	/// value is tried, because a single value an older build happened to store in plaintext must not
	/// condemn a key that works everywhere else.
	/// </summary>
	public static bool KeyOpens(MacroDeck2Paths paths, string candidate)
	{
		var values = ReadAll<List<Dictionary<string, string>>>(paths.PluginCredentialsDirectory)
			.Values
			.SelectMany(file => file.Value)
			.SelectMany(set => set.Values)
			.ToList();

		return values.Count == 0 ||
			values.Any(value => MacroDeck2StringCipher.TryDecrypt(value, candidate) is not null);
	}

	public static bool HasCredentials(MacroDeck2Paths paths)
		=> Directory.Exists(paths.PluginCredentialsDirectory) &&
			Directory.EnumerateFiles(paths.PluginCredentialsDirectory).Any();

	private static MigrationCredentialStatus ResolveStatus(
		Dictionary<string, TimestampedFile<List<Dictionary<string, string>>>> encrypted,
		string? decryptionKey,
		bool skipDecryption)
	{
		var values = encrypted.Values
			.SelectMany(file => file.Value)
			.SelectMany(set => set.Values)
			.ToList();

		if (values.Count == 0)
		{
			return MigrationCredentialStatus.NotPresent;
		}

		if (skipDecryption)
		{
			return MigrationCredentialStatus.Skipped;
		}

		if (decryptionKey is null)
		{
			return MigrationCredentialStatus.KeyUnavailable;
		}

		// Any value opening is what makes the key right, not all of them. A folder routinely holds a file
		// written under an older machine GUID, and condemning the key over that one file would send the
		// user hunting for a second key that does not exist.
		return values.Any(value => MacroDeck2StringCipher.TryDecrypt(value, decryptionKey) is not null)
			? MigrationCredentialStatus.Decrypted
			: MigrationCredentialStatus.KeyRejected;
	}

	private static List<IReadOnlyDictionary<string, string>> Decrypt(
		List<Dictionary<string, string>>? sets,
		string key)
	{
		if (sets is null)
		{
			return [];
		}

		var decrypted = new List<IReadOnlyDictionary<string, string>>();
		foreach (var set in sets)
		{
			var values = new Dictionary<string, string>(StringComparer.Ordinal);
			foreach (var (name, cipherText) in set)
			{
				var plain = MacroDeck2StringCipher.TryDecrypt(cipherText, key);
				if (plain is not null)
				{
					values[name] = plain;
				}
			}

			if (values.Count > 0)
			{
				decrypted.Add(values);
			}
		}

		return decrypted;
	}

	private static Dictionary<string, TimestampedFile<T>> ReadAll<T>(string directory)
		where T : class
	{
		var result = new Dictionary<string, TimestampedFile<T>>(StringComparer.OrdinalIgnoreCase);
		if (!Directory.Exists(directory))
		{
			return result;
		}

		string[] files;
		try
		{
			files = Directory.GetFiles(directory);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			return result;
		}

		foreach (var path in files)
		{
			var key = Path.GetFileNameWithoutExtension(path);
			if (string.IsNullOrWhiteSpace(key))
			{
				continue;
			}

			T? value;
			try
			{
				value = JsonSerializer.Deserialize<T>(File.ReadAllText(path), MacroDeck2Json.Options);
			}
			catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
			{
				continue;
			}

			if (value is not null)
			{
				result[key] = new TimestampedFile<T>(value, File.GetLastWriteTimeUtc(path));
			}
		}

		return result;
	}

	private sealed record TimestampedFile<T>(T Value, DateTime LastWriteUtc);
}
