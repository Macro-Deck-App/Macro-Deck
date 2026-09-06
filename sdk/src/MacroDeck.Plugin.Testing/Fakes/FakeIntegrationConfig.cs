using MacroDeck.Sdk.ConfigFlow;

namespace MacroDeck.Plugin.Testing.Fakes;

/// <summary>
/// In-memory <see cref="IIntegrationConfig" /> that keeps a plain and a secret keyspace per entry,
/// written by <see cref="SeedString" />/<see cref="SetStringAsync" /> and
/// <see cref="SeedSecret" />/<see cref="SetSecretAsync" /> respectively and never visible through the
/// other one's getter. A single shared dictionary would pass every naive round-trip test while hiding
/// the one bug this fake exists to catch: a plugin calling <see cref="GetStringAsync" /> for a value it
/// (or the test) only ever put in the secret keyspace.
///
/// <para>
/// There is no config flow here to create entries through, so seed one with <see cref="AddEntry" />
/// before running the plugin under test, then use <see cref="SeedString" />/<see cref="SeedSecret" /> to
/// populate it - or call those directly with a fresh <see cref="Guid" /> and let them create a bare
/// entry on demand, see their remarks.
/// </para>
/// </summary>
public sealed class FakeIntegrationConfig : IIntegrationConfig
{
	private readonly Lock _gate = new();
	private readonly List<Guid> _order = [];
	private readonly Dictionary<Guid, Entry> _entries = new();

	/// <summary>
	/// Every entry seeded so far, in the order <see cref="AddEntry" /> (or an implicit seed) created it.
	/// </summary>
	public IReadOnlyList<ConfigEntrySnapshot> Entries
	{
		get
		{
			lock (_gate)
			{
				return [.. _order.Select(id => new ConfigEntrySnapshot(id, _entries[id].Title))];
			}
		}
	}

	/// <summary>
	/// Creates a fresh config entry with the given title and returns its id, for seeding through
	/// <see cref="SeedString" />/<see cref="SeedSecret" />.
	/// </summary>
	public Guid AddEntry(string title)
	{
		var id = Guid.NewGuid();

		lock (_gate)
		{
			_entries[id] = new Entry(title);
			_order.Add(id);
		}

		return id;
	}

	/// <summary>
	/// Sets a plain value directly, bypassing <see cref="SetStringAsync" />'s unknown-entry check.
	/// An <paramref name="entryId" /> that was never seeded via <see cref="AddEntry" /> gets one created
	/// on the spot (titled after the id) rather than failing the seed call - a test asserting an entry's
	/// title should still go through <see cref="AddEntry" /> explicitly.
	/// </summary>
	public void SeedString(Guid entryId, string key, string value)
	{
		lock (_gate)
		{
			GetOrCreateEntry(entryId).Strings[key] = value;
		}
	}

	/// <summary>The secret-keyspace counterpart to <see cref="SeedString" /> - see its remarks.</summary>
	public void SeedSecret(Guid entryId, string key, string value)
	{
		lock (_gate)
		{
			GetOrCreateEntry(entryId).Secrets[key] = value;
		}
	}

	/// <inheritdoc />
	public Task<IReadOnlyList<ConfigEntrySnapshot>> GetEntriesAsync(CancellationToken cancellationToken = default)
		=> Task.FromResult(Entries);

	/// <summary>
	/// Reads a plain value. Returns <c>null</c> for an unknown entry, an unknown key, or a key that only
	/// exists in the secret keyspace.
	/// </summary>
	public Task<string?> GetStringAsync(Guid entryId, string key, CancellationToken cancellationToken = default)
	{
		lock (_gate)
		{
			if (_entries.TryGetValue(entryId, out var entry) && entry.Strings.TryGetValue(key, out var value))
			{
				return Task.FromResult<string?>(value);
			}

			return Task.FromResult<string?>(null);
		}
	}

	/// <summary>
	/// Reads a secret value. Returns <c>null</c> for an unknown entry, an unknown key, or a key that only
	/// exists in the plain keyspace.
	/// </summary>
	public Task<string?> GetSecretAsync(Guid entryId, string key, CancellationToken cancellationToken = default)
	{
		lock (_gate)
		{
			if (_entries.TryGetValue(entryId, out var entry) && entry.Secrets.TryGetValue(key, out var value))
			{
				return Task.FromResult<string?>(value);
			}

			return Task.FromResult<string?>(null);
		}
	}

	/// <summary>
	/// Writes a plain value, or removes the key when <paramref name="value" /> is <c>null</c>. Throws for
	/// an unknown entry rather than silently dropping the write - mirroring the real
	/// <c>IntegrationConfig</c>, which does the same specifically to avoid a class of silent-data-loss bug
	/// (a rotated credential reported as persisted while nothing was actually stored).
	/// </summary>
	/// <exception cref="InvalidOperationException"><paramref name="entryId" /> was never seeded.</exception>
	public Task SetStringAsync(Guid entryId, string key, string? value, CancellationToken cancellationToken = default)
	{
		lock (_gate)
		{
			var entry = _entries.TryGetValue(entryId, out var found) ? found : throw MissingEntry(entryId, key);

			if (value is null)
			{
				entry.Strings.Remove(key);
			}
			else
			{
				entry.Strings[key] = value;
			}
		}

		return Task.CompletedTask;
	}

	/// <summary>
	/// Writes a secret value. Same unknown-entry behaviour as <see cref="SetStringAsync" /> - see its
	/// remarks.
	/// </summary>
	/// <exception cref="InvalidOperationException"><paramref name="entryId" /> was never seeded.</exception>
	public Task SetSecretAsync(Guid entryId, string key, string value, CancellationToken cancellationToken = default)
	{
		lock (_gate)
		{
			var entry = _entries.TryGetValue(entryId, out var found) ? found : throw MissingEntry(entryId, key);
			entry.Secrets[key] = value;
		}

		return Task.CompletedTask;
	}

	// Must be called under _gate.
	private Entry GetOrCreateEntry(Guid entryId)
	{
		if (_entries.TryGetValue(entryId, out var entry))
		{
			return entry;
		}

		entry = new Entry(entryId.ToString());
		_entries[entryId] = entry;
		_order.Add(entryId);
		return entry;
	}

	private static InvalidOperationException MissingEntry(Guid entryId, string key)
		=> new($"Config entry {entryId} does not exist; '{key}' was not stored.");

	// Mutable so a Set*Async can write in place under _gate without a second dictionary lookup.
	private sealed class Entry(string title)
	{
		public string Title { get; } = title;

		public Dictionary<string, string> Strings { get; } = new(StringComparer.Ordinal);

		public Dictionary<string, string> Secrets { get; } = new(StringComparer.Ordinal);
	}
}
