using System.Collections.Concurrent;
using System.Text.Json;
using MacroDeckHost.Application.Paths;
using MacroDeckHost.Application.Persistence;
using MacroDeckHost.Application.Persistence.Profiles;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.Persistence;

public sealed class JsonProfileStore : IProfileStore
{
	private static readonly JsonSerializerOptions _options = PersistenceJsonOptions.Default;

	private readonly ConcurrentDictionary<Guid, object> _locks = new();
	private readonly string _directory;
	private readonly ILogger _logger;
	private readonly DurableJsonFile _files;

	public JsonProfileStore(IMacroDeckPaths paths,
		ILogger logger,
		IPersistenceRecoveryReporter? recoveryReporter = null)
	{
		_directory = paths.ProfilesDirectory;
		_logger = logger;
		_files = new DurableJsonFile("profile",
			_options,
			logger,
			PersistenceBackup.KeepLastKnownGood,
			recoveryReporter);
	}

	public ProfileLoadResult LoadAll()
	{
		var profiles = new List<ProfileFile>();
		var unreadableCount = 0;
		foreach (var path in DurableJsonFile.EnumerateDocumentPaths(_directory, ".json"))
		{
			ProfileFile? profile;
			if (Guid.TryParse(Path.GetFileNameWithoutExtension(path), out var id))
			{
				lock (LockFor(id))
				{
					profile = _files.Read<ProfileFile>(path);
				}
			}
			else
			{
				profile = _files.Read<ProfileFile>(path);
			}

			if (profile is not null)
			{
				profiles.Add(profile);
			}
			else
			{
				unreadableCount++;
			}
		}

		return new ProfileLoadResult(profiles, unreadableCount);
	}

	public bool Save(ProfileFile profile)
	{
		lock (LockFor(profile.Id))
		{
			try
			{
				Directory.CreateDirectory(_directory);
				_files.Write(PathFor(profile.Id), profile);
				return true;
			}
			catch (Exception ex)
			{
				_logger.Error(ex, "Failed to write profile {ProfileId}", profile.Id);
				return false;
			}
		}
	}

	public void Delete(Guid id)
	{
		lock (LockFor(id))
		{
			try
			{
				_files.Delete(PathFor(id));
			}
			catch (Exception ex)
			{
				_logger.Error(ex, "Failed to delete profile {ProfileId}", id);
			}
		}
	}

	private object LockFor(Guid id) => _locks.GetOrAdd(id, _ => new object());

	private string PathFor(Guid id) => Path.Combine(_directory, id + ".json");
}
