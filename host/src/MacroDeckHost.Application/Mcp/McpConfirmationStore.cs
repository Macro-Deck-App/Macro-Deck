using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace MacroDeckHost.Application.Mcp;

public sealed class McpConfirmationEntry
{
	private readonly TaskCompletionSource<bool> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

	internal McpConfirmationEntry(
		string id,
		Guid tokenId,
		string clientName,
		string fingerprint,
		IReadOnlyList<string> lines,
		DateTime expiresAt)
	{
		Id = id;
		TokenId = tokenId;
		ClientName = clientName;
		Fingerprint = fingerprint;
		Lines = lines;
		ExpiresAt = expiresAt;
	}

	public string Id { get; }

	public Guid TokenId { get; }

	public string ClientName { get; }

	public string Fingerprint { get; }

	public IReadOnlyList<string> Lines { get; }

	public DateTime ExpiresAt { get; }

	public string? FormNonce { get; internal set; }

	internal bool Consumed { get; set; }

	public Task<bool> Completion => _completion.Task;

	internal void Complete(bool allowed)
	{
		_completion.TrySetResult(allowed);
	}

	internal void Abandon()
	{
		_completion.TrySetCanceled();
	}
}

public interface IMcpConfirmationStore
{
	McpConfirmationEntry Create(Guid tokenId, string clientName, string fingerprint, IReadOnlyList<string> lines);

	McpConfirmationEntry? Find(string id);

	McpConfirmationEntry? FindForToken(string id, Guid tokenId);

	string IssueFormNonce(McpConfirmationEntry entry);

	bool TryConsume(string id, string? formNonce, out McpConfirmationEntry? entry);

	void Complete(McpConfirmationEntry entry, bool allowed);

	void Abandon(string id);
}

public sealed class McpConfirmationStore : IMcpConfirmationStore
{
	public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);
	public const int MaxPendingPerToken = 5;

	private readonly ConcurrentDictionary<string, McpConfirmationEntry> _entries = new(StringComparer.Ordinal);
	private readonly TimeProvider _timeProvider;
	private readonly Lock _gate = new();

	public McpConfirmationStore(TimeProvider timeProvider)
	{
		_timeProvider = timeProvider;
	}

	public McpConfirmationEntry Create(Guid tokenId, string clientName, string fingerprint, IReadOnlyList<string> lines)
	{
		lock (_gate)
		{
			Prune();
			foreach (var stale in _entries.Values.Where(entry => entry.TokenId == tokenId)
				.OrderBy(entry => entry.ExpiresAt)
				.SkipLast(MaxPendingPerToken - 1)
				.ToList())
			{
				Abandon(stale.Id);
			}

			var entry = new McpConfirmationEntry(NewId(), tokenId, clientName, fingerprint, lines, Now() + Lifetime);
			_entries[entry.Id] = entry;
			return entry;
		}
	}

	public McpConfirmationEntry? Find(string id)
	{
		return _entries.TryGetValue(id, out var entry) && !entry.Consumed && entry.ExpiresAt > Now() ? entry : null;
	}

	public McpConfirmationEntry? FindForToken(string id, Guid tokenId)
	{
		return _entries.TryGetValue(id, out var entry) && entry.TokenId == tokenId && entry.ExpiresAt > Now()
			? entry
			: null;
	}

	public string IssueFormNonce(McpConfirmationEntry entry)
	{
		var nonce = NewId();
		entry.FormNonce = nonce;
		return nonce;
	}

	public bool TryConsume(string id, string? formNonce, out McpConfirmationEntry? entry)
	{
		lock (_gate)
		{
			entry = Find(id);
			if (entry?.FormNonce is not { } expected
				|| formNonce is null
				|| !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected),
					Encoding.ASCII.GetBytes(formNonce)))
			{
				entry = null;
				return false;
			}

			entry.Consumed = true;
			return true;
		}
	}

	public void Complete(McpConfirmationEntry entry, bool allowed)
	{
		entry.Complete(allowed);
	}

	public void Abandon(string id)
	{
		if (_entries.TryRemove(id, out var entry))
		{
			entry.Abandon();
		}
	}

	private void Prune()
	{
		var now = Now();
		foreach (var expired in _entries.Values.Where(entry => entry.ExpiresAt <= now).ToList())
		{
			Abandon(expired.Id);
		}
	}

	private DateTime Now()
	{
		return _timeProvider.GetUtcNow().UtcDateTime;
	}

	private static string NewId()
	{
		return Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
	}
}
