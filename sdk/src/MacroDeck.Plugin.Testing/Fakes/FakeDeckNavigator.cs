using MacroDeck.Sdk.Decks;

namespace MacroDeck.Plugin.Testing.Fakes;

/// <summary>Which <see cref="IDeckNavigator" /> method a <see cref="DeckNavigationCall" /> recorded.</summary>
public enum DeckNavigationKind
{
	/// <summary>Recorded by <see cref="IDeckNavigator.ChangeFolderAsync" />.</summary>
	ChangeFolder,

	/// <summary>Recorded by <see cref="IDeckNavigator.ChangeProfileAsync" />.</summary>
	ChangeProfile,

	/// <summary>Recorded by <see cref="IDeckNavigator.GoToParentAsync" />.</summary>
	GoToParent,

	/// <summary>Recorded by <see cref="IDeckNavigator.GoBackAsync" />.</summary>
	GoBack
}

/// <summary>One navigation call recorded by <see cref="FakeDeckNavigator" />.</summary>
public sealed record DeckNavigationCall
{
	/// <summary>Which method was called.</summary>
	public required DeckNavigationKind Kind { get; init; }

	/// <summary>
	/// The folder or profile id, for <see cref="DeckNavigationKind.ChangeFolder" /> and
	/// <see cref="DeckNavigationKind.ChangeProfile" />; <c>null</c> for the other two kinds.
	/// </summary>
	public string? Id { get; init; }

	/// <summary>The origin client id passed to the call, if any.</summary>
	public string? OriginClientId { get; init; }
}

/// <summary>
/// In-memory <see cref="IDeckNavigator" />. The four navigation methods never fail - they just record
/// what was asked for, in <see cref="Calls" /> - since there is no real deck here that could refuse.
/// <see cref="GetFolders" />, <see cref="GetProfiles" /> and <see cref="GetClients" /> return whatever
/// <see cref="SeedFolders" />, <see cref="SeedProfiles" /> and <see cref="SeedClients" /> last set, empty until
/// then; <see cref="RaiseClientChanged" /> moves a client and raises <see cref="ClientChanged" />.
/// </summary>
public sealed class FakeDeckNavigator : IDeckNavigator
{
	private readonly Lock _gate = new();
	private readonly List<DeckNavigationCall> _calls = [];
	private IReadOnlyList<DeckFolder> _folders = [];
	private IReadOnlyList<DeckProfile> _profiles = [];
	private IReadOnlyList<DeckClient> _clients = [];

	/// <inheritdoc />
	public event EventHandler<DeckClientChangedEventArgs>? ClientChanged;

	/// <summary>Every navigation call recorded so far, in call order.</summary>
	public IReadOnlyList<DeckNavigationCall> Calls
	{
		get
		{
			lock (_gate)
			{
				return [.. _calls];
			}
		}
	}

	/// <summary>Sets what <see cref="GetFolders" /> returns.</summary>
	public void SeedFolders(params DeckFolder[] folders)
	{
		lock (_gate)
		{
			_folders = [.. folders];
		}
	}

	/// <summary>Sets what <see cref="GetProfiles" /> returns.</summary>
	public void SeedProfiles(params DeckProfile[] profiles)
	{
		lock (_gate)
		{
			_profiles = [.. profiles];
		}
	}

	/// <summary>Sets what <see cref="GetClients" /> returns, without raising <see cref="ClientChanged" />.</summary>
	public void SeedClients(params DeckClient[] clients)
	{
		lock (_gate)
		{
			_clients = [.. clients];
		}
	}

	/// <summary>
	/// Puts <paramref name="client" /> into <see cref="GetClients" />, replacing the entry with the same
	/// client id, then raises <see cref="ClientChanged" /> with the given previous position, the way the
	/// host reports a client that was first seen (both <c>null</c>) or moved.
	/// </summary>
	public void RaiseClientChanged(DeckClient client, string? previousProfileId = null, string? previousFolderId = null)
	{
		lock (_gate)
		{
			_clients = [.. _clients.Where(existing => existing.ClientId != client.ClientId), client];
		}

		ClientChanged?.Invoke(this, new DeckClientChangedEventArgs(client, previousProfileId, previousFolderId));
	}

	/// <inheritdoc />
	public Task ChangeFolderAsync(string folderId,
		string? originClientId = null,
		CancellationToken cancellationToken = default)
	{
		Record(DeckNavigationKind.ChangeFolder, folderId, originClientId);
		return Task.CompletedTask;
	}

	/// <inheritdoc />
	public Task ChangeProfileAsync(string profileId,
		string? originClientId = null,
		CancellationToken cancellationToken = default)
	{
		Record(DeckNavigationKind.ChangeProfile, profileId, originClientId);
		return Task.CompletedTask;
	}

	/// <inheritdoc />
	public Task GoToParentAsync(string? originClientId = null, CancellationToken cancellationToken = default)
	{
		Record(DeckNavigationKind.GoToParent, null, originClientId);
		return Task.CompletedTask;
	}

	/// <inheritdoc />
	public Task GoBackAsync(string? originClientId = null, CancellationToken cancellationToken = default)
	{
		Record(DeckNavigationKind.GoBack, null, originClientId);
		return Task.CompletedTask;
	}

	/// <inheritdoc />
	public IReadOnlyList<DeckFolder> GetFolders()
	{
		lock (_gate)
		{
			return _folders;
		}
	}

	/// <inheritdoc />
	public IReadOnlyList<DeckProfile> GetProfiles()
	{
		lock (_gate)
		{
			return _profiles;
		}
	}

	/// <inheritdoc />
	public IReadOnlyList<DeckClient> GetClients()
	{
		lock (_gate)
		{
			return _clients;
		}
	}

	private void Record(DeckNavigationKind kind, string? id, string? originClientId)
	{
		lock (_gate)
		{
			_calls.Add(new DeckNavigationCall { Kind = kind, Id = id, OriginClientId = originClientId });
		}
	}
}
