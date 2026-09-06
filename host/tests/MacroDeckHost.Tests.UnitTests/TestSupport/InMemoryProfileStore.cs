using MacroDeckHost.Application.Persistence;
using MacroDeckHost.Application.Persistence.Profiles;

namespace MacroDeckHost.Tests.UnitTests.TestSupport;

internal sealed class InMemoryProfileStore : IProfileStore
{
	private readonly Dictionary<Guid, ProfileFile> _files = new();

	public InMemoryProfileStore(params ProfileFile[] initial)
	{
		foreach (var file in initial)
		{
			_files[file.Id] = file;
		}
	}

	public int SaveCount { get; private set; }
	public int DeleteCount { get; private set; }

	public bool FailSaves { get; set; }

	public int UnreadableCount { get; set; }

	public ProfileLoadResult LoadAll() => new(_files.Values.ToList(), UnreadableCount);

	public bool Save(ProfileFile profile)
	{
		SaveCount++;
		if (FailSaves)
		{
			return false;
		}

		_files[profile.Id] = profile;
		return true;
	}

	public void Delete(Guid id)
	{
		_files.Remove(id);
		DeleteCount++;
	}

	public ProfileFile? Get(Guid id) => _files.TryGetValue(id, out var file) ? file : null;
}
