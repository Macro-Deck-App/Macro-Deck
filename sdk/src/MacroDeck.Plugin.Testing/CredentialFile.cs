using System.Text.Json;
using MacroDeck.Plugin.Hosting.Credentials;

namespace MacroDeck.Plugin.Testing;

/// <summary>
/// Reads the credential file a self-registering plugin persists after it registers, so a test can
/// prove the issued secret actually landed on disk without depending on <c>FilePluginCredentialStore</c>,
/// which is internal to <c>MacroDeck.Plugin.Hosting</c>.
/// </summary>
public static class CredentialFile
{
	private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

	/// <summary>
	/// Reads the credentials a plugin with id <paramref name="pluginId" /> persisted under
	/// <paramref name="stateDirectory" />, or <see langword="null" /> when it has not registered yet (or
	/// the file cannot be read).
	/// </summary>
	public static PluginCredentials? Read(string stateDirectory, string pluginId)
	{
		ArgumentException.ThrowIfNullOrEmpty(stateDirectory);
		ArgumentException.ThrowIfNullOrEmpty(pluginId);

		var path = Path.Combine(stateDirectory, pluginId, "credentials.json");

		if (!File.Exists(path))
		{
			return null;
		}

		try
		{
			using var stream = File.OpenRead(path);
			return JsonSerializer.Deserialize<PluginCredentials>(stream, _json);
		}
		catch (JsonException)
		{
			return null;
		}
		catch (IOException)
		{
			return null;
		}
	}
}
