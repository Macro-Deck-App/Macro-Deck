using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MacroDeckHost.Integrations.Imap.Configuration;

namespace MacroDeckHost.Integrations.Imap.Models;

internal static class ImapInboxId
{
	// The host stores widget selections, trigger filters and variable names against this id, so it is
	// derived from the account itself: setting the same account up again keeps it.
	public static string For(string host, int port, string username, string folder)
	{
		var key = string.Join('|',
			host.Trim().ToLowerInvariant(),
			port.ToString(CultureInfo.InvariantCulture),
			username.Trim().ToLowerInvariant(),
			ImapAccountSettings.NormalizeFolder(folder));
		return "i" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..16];
	}

	public static string? FromValues(IReadOnlyDictionary<string, JsonElement> values)
	{
		var host = ReadString(values, ImapConfigKeys.Host);
		var username = ReadString(values, ImapConfigKeys.Username);
		if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(username))
		{
			return null;
		}

		var port = values.TryGetValue(ImapConfigKeys.Port, out var portValue)
			? ImapAccountSettings.ParsePort(portValue)
			: ImapAccountSettings.DefaultPort;
		return port is { } valid
			? For(host, valid, username, ReadString(values, ImapConfigKeys.Folder) ?? ImapAccountSettings.DefaultFolder)
			: null;
	}

	private static string? ReadString(IReadOnlyDictionary<string, JsonElement> values, string key)
	{
		return values.TryGetValue(key, out var element) && element.ValueKind == JsonValueKind.String
			? element.GetString()
			: null;
	}
}
