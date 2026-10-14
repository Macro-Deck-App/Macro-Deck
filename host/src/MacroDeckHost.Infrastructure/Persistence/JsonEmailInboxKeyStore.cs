using MacroDeckHost.Application.Email;
using MacroDeckHost.Application.Paths;
using MacroDeckHost.Application.Persistence;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.Persistence;

public sealed class JsonEmailInboxKeyStore : IEmailInboxKeyStore
{
	private const int CurrentVersion = 1;

	private readonly Lock _lock = new();
	private readonly string _filePath;
	private readonly ILogger _logger;
	private readonly DurableJsonFile _files;
	private EmailInboxKeySet? _keys;
	private bool _persistable = true;

	public JsonEmailInboxKeyStore(
		IMacroDeckPaths paths,
		ILogger logger,
		IPersistenceRecoveryReporter? recoveryReporter = null)
	{
		_filePath = Path.Combine(paths.DataDirectory, "email-inboxes.json");
		_logger = logger.ForContext<JsonEmailInboxKeyStore>();
		_files = new DurableJsonFile("email inbox key file",
			PersistenceJsonOptions.Default,
			logger,
			PersistenceBackup.KeepLastKnownGood,
			recoveryReporter);
	}

	public IReadOnlyList<EmailInboxKey> Keys
	{
		get
		{
			lock (_lock)
			{
				return Loaded().Keys;
			}
		}
	}

	public IReadOnlyDictionary<string, string> Assign(IReadOnlyList<EmailInboxDescriptor> visible)
	{
		lock (_lock)
		{
			var current = Loaded();
			var next = EmailInboxKeys.Assign(current, visible);
			if (!ReferenceEquals(next, current))
			{
				_keys = next;
				Save(next);
			}

			return next.Keys.ToDictionary(key => key.Ref, key => key.Slug, StringComparer.Ordinal);
		}
	}

	private EmailInboxKeySet Loaded()
	{
		if (_keys is not null)
		{
			return _keys;
		}

		if (_files.Read<PersistedKeys>(_filePath) is { } loaded)
		{
			_keys = new EmailInboxKeySet(loaded.Generation,
			[
				.. (loaded.Inboxes ?? [])
				.Where(key => !string.IsNullOrEmpty(key.Ref) && !string.IsNullOrEmpty(key.Slug))
				.DistinctBy(key => key.Ref, StringComparer.Ordinal)
				.Select(key => new EmailInboxKey(key.Ref!, key.Slug!, key.Seen))
			]);
			return _keys;
		}

		// Read returns null for both a missing file and an unrecoverable one; only the former may be
		// overwritten, or the next save would reassign every inbox's variable names.
		_persistable = !File.Exists(_filePath)
			&& !File.Exists(_filePath + DurableJsonFile.TempSuffix)
			&& !File.Exists(_filePath + DurableJsonFile.BackupSuffix);
		if (!_persistable)
		{
			_logger.Warning("The email inbox key file {Path} could not be read; inbox variable names are not saved "
				+ "until it is repaired",
				_filePath);
		}

		_keys = EmailInboxKeySet.Empty;
		return _keys;
	}

	private void Save(EmailInboxKeySet keys)
	{
		if (!_persistable)
		{
			return;
		}

		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
			_files.Write(_filePath,
				new PersistedKeys
				{
					Version = CurrentVersion,
					Generation = keys.Generation,
					Inboxes =
					[
						.. keys.Keys.Select(key => new PersistedKey { Ref = key.Ref, Slug = key.Slug, Seen = key.Seen })
					]
				});
		}
		catch (Exception exception)
		{
			_logger.Error(exception, "Failed to write the email inbox keys to {Path}", _filePath);
		}
	}

	private sealed record PersistedKeys
	{
		public int Version { get; init; }

		public long Generation { get; init; }

		public IReadOnlyList<PersistedKey>? Inboxes { get; init; }
	}

	private sealed record PersistedKey
	{
		public string? Ref { get; init; }

		public string? Slug { get; init; }

		public long Seen { get; init; }
	}
}
