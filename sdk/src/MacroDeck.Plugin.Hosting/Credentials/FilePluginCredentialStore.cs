using System.Text.Json;
using MacroDeck.Plugin.Hosting.Logging;
using Microsoft.Extensions.Options;
using Serilog;

namespace MacroDeck.Plugin.Hosting.Credentials;

/// <summary>
/// Self-registering mode: the plugin registers once and keeps the secret it was issued, so every
/// later start reuses it instead of consuming another enrollment token.
/// </summary>
internal sealed class FilePluginCredentialStore : IPluginCredentialStore
{
	private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web)
	{
		WriteIndented = true
	};

	private readonly ILogger _logger;
	private readonly string _path;

	public FilePluginCredentialStore(
		IOptions<PluginHostOptions> options,
		PluginMetadata metadata,
		ILogger logger)
	{
		ArgumentNullException.ThrowIfNull(options);
		ArgumentNullException.ThrowIfNull(metadata);

		_logger = logger.ForContext<FilePluginCredentialStore>();

		var root = string.IsNullOrEmpty(options.Value.StateDirectory)
			? PluginStateDirectory.Default()
			: options.Value.StateDirectory;

		Directory = Path.Combine(root, metadata.Id);
		_path = Path.Combine(Directory, "credentials.json");
	}

	/// <summary>The per-plugin directory the credential file lives in.</summary>
	public string Directory { get; }

	public bool CanSave => true;

	public async Task<PluginCredentials?> LoadAsync(CancellationToken cancellationToken = default)
	{
		if (!File.Exists(_path))
		{
			return null;
		}

		try
		{
			await using var stream = File.OpenRead(_path);
			return await JsonSerializer.DeserializeAsync<PluginCredentials>(stream, _json, cancellationToken);
		}
		catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
		{
			// A credential file that cannot be read is treated as absent, so a corrupted one leads to
			// "register again" rather than a plugin that can never start. The secret is still on the
			// host, so re-registration is refused there rather than silently duplicating.
			_logger.CredentialsUnreadable(_path, exception);
			return null;
		}
	}

	public async Task SaveAsync(PluginCredentials credentials, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(credentials);

		CreateDirectory();

		// Written to a temp file in the same directory and moved into place, so a crash mid-write
		// cannot leave a half-written file that reads as valid-but-wrong. Permissions are set on the
		// temp file *before* any bytes are in it: creating it world-readable and tightening afterwards
		// leaves a window where the secret is exposed.
		var temporary = Path.Combine(Directory, $"credentials.{Guid.NewGuid():N}.tmp");

		try
		{
			await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
			{
				RestrictToOwner(temporary);
				await JsonSerializer.SerializeAsync(stream, credentials, _json, cancellationToken);
			}

			File.Move(temporary, _path, overwrite: true);
			RestrictToOwner(_path);
		}
		catch
		{
			TryDelete(temporary);
			throw;
		}
	}

	private void CreateDirectory()
	{
		if (OperatingSystem.IsWindows())
		{
			System.IO.Directory.CreateDirectory(Directory);
			return;
		}

		System.IO.Directory.CreateDirectory(Directory,
			UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
	}

	/// <summary>
	/// Owner-only on Unix. On Windows the per-user profile ACL is the protection, which is the same
	/// posture the host takes for its own data directory - no ACL is edited here, and nothing is
	/// encrypted at rest.
	/// </summary>
	private static void RestrictToOwner(string path)
	{
		if (OperatingSystem.IsWindows())
		{
			return;
		}

		File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
	}

	private static void TryDelete(string path)
	{
		try
		{
			File.Delete(path);
		}
		catch (IOException)
		{
			// Best effort: the write already failed and is being reported, and a stray temp file is
			// not worth masking that with a second exception.
		}
		catch (UnauthorizedAccessException)
		{
		}
	}
}
