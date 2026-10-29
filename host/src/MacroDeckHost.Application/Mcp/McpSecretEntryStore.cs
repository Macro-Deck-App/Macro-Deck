using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Application.Mcp;

public sealed class McpSecretEntry
{
	private readonly TaskCompletionSource<Guid> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

	internal McpSecretEntry(
		string id,
		Guid tokenId,
		string clientName,
		SecretKind kind,
		string label,
		DateTime expiresAt)
	{
		Id = id;
		TokenId = tokenId;
		ClientName = clientName;
		Kind = kind;
		Label = label;
		ExpiresAt = expiresAt;
	}

	public string Id { get; }

	public Guid TokenId { get; }

	public string ClientName { get; }

	public SecretKind Kind { get; }

	public string Label { get; }

	public DateTime ExpiresAt { get; }

	public string? FormNonce { get; internal set; }

	internal bool Consumed { get; set; }

	public Task<Guid> Completion => _completion.Task;

	internal void Complete(Guid secretId)
	{
		_completion.TrySetResult(secretId);
	}

	internal void Abandon()
	{
		_completion.TrySetCanceled();
	}
}

public interface IMcpSecretEntryStore
{
	McpSecretEntry Create(Guid tokenId, string clientName, SecretKind kind, string label);

	McpSecretEntry? Find(string id);

	McpSecretEntry? FindForToken(string id, Guid tokenId);

	string IssueFormNonce(McpSecretEntry entry);

	bool TryConsume(string id, string? formNonce, out McpSecretEntry? entry);

	void Complete(McpSecretEntry entry, Guid secretId);

	void Abandon(string id);
}

public sealed class McpSecretEntryStore : IMcpSecretEntryStore
{
	public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);
	public const int MaxLabelLength = 100;
	public const int MaxPendingPerToken = 5;

	private readonly ConcurrentDictionary<string, McpSecretEntry> _entries = new(StringComparer.Ordinal);
	private readonly TimeProvider _timeProvider;
	private readonly Lock _gate = new();

	public McpSecretEntryStore(TimeProvider timeProvider)
	{
		_timeProvider = timeProvider;
	}

	public McpSecretEntry Create(Guid tokenId, string clientName, SecretKind kind, string label)
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

			var entry = new McpSecretEntry(NewId(), tokenId, clientName, kind, label, Now() + Lifetime);
			_entries[entry.Id] = entry;
			return entry;
		}
	}

	public McpSecretEntry? Find(string id)
	{
		return _entries.TryGetValue(id, out var entry) && !entry.Consumed && entry.ExpiresAt > Now() ? entry : null;
	}

	public McpSecretEntry? FindForToken(string id, Guid tokenId)
	{
		return _entries.TryGetValue(id, out var entry) && entry.TokenId == tokenId && entry.ExpiresAt > Now()
			? entry
			: null;
	}

	public string IssueFormNonce(McpSecretEntry entry)
	{
		var nonce = NewId();
		entry.FormNonce = nonce;
		return nonce;
	}

	public bool TryConsume(string id, string? formNonce, out McpSecretEntry? entry)
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

	public void Complete(McpSecretEntry entry, Guid secretId)
	{
		entry.Complete(secretId);
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
