using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace MacroDeckHost.Application.Mcp;

public sealed record McpActionRef(string IntegrationId, string ActionId);

public sealed class McpConfirmationEntry
{
	private readonly TaskCompletionSource<bool> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

	internal McpConfirmationEntry(
		string id,
		Guid tokenId,
		string clientName,
		string fingerprint,
		IReadOnlyList<string> lines,
		IReadOnlyList<McpActionRef> actions,
		bool alwaysAllowable,
		bool inApp,
		DateTime createdAt,
		DateTime expiresAt)
	{
		Id = id;
		TokenId = tokenId;
		ClientName = clientName;
		Fingerprint = fingerprint;
		Lines = lines;
		Actions = actions;
		AlwaysAllowable = alwaysAllowable;
		InApp = inApp;
		CreatedAt = createdAt;
		ExpiresAt = expiresAt;
	}

	public string Id { get; }

	public Guid TokenId { get; }

	public string ClientName { get; }

	public string Fingerprint { get; }

	public IReadOnlyList<string> Lines { get; }

	public IReadOnlyList<McpActionRef> Actions { get; }

	public bool AlwaysAllowable { get; }

	public bool InApp { get; }

	public DateTime CreatedAt { get; }

	public DateTime ExpiresAt { get; }

	public string? FormNonce { get; internal set; }

	internal bool Consumed { get; set; }

	internal ITimer? ExpiryTimer { get; set; }

	internal bool Answered => _completion.Task.IsCompletedSuccessfully;

	public Task<bool> Completion => _completion.Task;

	internal void Complete(bool allowed)
	{
		_completion.TrySetResult(allowed);
	}

	internal void Abandon()
	{
		ExpiryTimer?.Dispose();
		_completion.TrySetCanceled();
	}
}

public enum McpInAppAnswerResult
{
	Answered,
	NotFound,
	AlreadyAnswered
}

public interface IMcpConfirmationStore
{
	event Action? Changed;

	McpConfirmationEntry Create(Guid tokenId, string clientName, string fingerprint, IReadOnlyList<string> lines);

	McpConfirmationEntry GetOrCreateInApp(
		Guid tokenId,
		string clientName,
		string fingerprint,
		IReadOnlyList<string> lines,
		IReadOnlyList<McpActionRef> actions,
		bool alwaysAllowable);

	McpConfirmationEntry? Find(string id);

	McpConfirmationEntry? FindForToken(string id, Guid tokenId);

	IReadOnlyList<McpConfirmationEntry> PendingInApp();

	McpInAppAnswerResult AnswerInApp(string id, bool allowed, out McpConfirmationEntry? entry);

	bool TryConsumeInApp(string id, out bool allowed);

	void AbandonForToken(Guid tokenId);

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

	public event Action? Changed;

	public McpConfirmationEntry Create(Guid tokenId, string clientName, string fingerprint, IReadOnlyList<string> lines)
	{
		bool changed;
		McpConfirmationEntry entry;
		lock (_gate)
		{
			changed = MakeRoom(tokenId);
			entry = Add(tokenId, clientName, fingerprint, lines, [], false, false);
		}

		RaiseIf(changed);
		return entry;
	}

	public McpConfirmationEntry GetOrCreateInApp(
		Guid tokenId,
		string clientName,
		string fingerprint,
		IReadOnlyList<string> lines,
		IReadOnlyList<McpActionRef> actions,
		bool alwaysAllowable)
	{
		McpConfirmationEntry entry;
		lock (_gate)
		{
			var now = Now();
			var existing = _entries.Values.FirstOrDefault(candidate => candidate.InApp
				&& candidate.TokenId == tokenId
				&& candidate.Fingerprint == fingerprint
				&& candidate.ExpiresAt > now);
			if (existing is not null)
			{
				return existing;
			}

			MakeRoom(tokenId);
			entry = Add(tokenId, clientName, fingerprint, lines, actions, alwaysAllowable, true);
			var id = entry.Id;
			entry.ExpiryTimer = _timeProvider.CreateTimer(_ => Abandon(id), null, Lifetime, Timeout.InfiniteTimeSpan);
		}

		RaiseIf(true);
		return entry;
	}

	public McpConfirmationEntry? Find(string id)
	{
		return _entries.TryGetValue(id, out var entry) && !entry.InApp && !entry.Consumed && entry.ExpiresAt > Now()
			? entry
			: null;
	}

	public McpConfirmationEntry? FindForToken(string id, Guid tokenId)
	{
		return _entries.TryGetValue(id, out var entry)
			&& !entry.InApp
			&& entry.TokenId == tokenId
			&& entry.ExpiresAt > Now()
				? entry
				: null;
	}

	public IReadOnlyList<McpConfirmationEntry> PendingInApp()
	{
		var now = Now();
		return _entries.Values.Where(entry => entry.InApp && !entry.Answered && entry.ExpiresAt > now)
			.OrderBy(entry => entry.CreatedAt)
			.ToList();
	}

	public McpInAppAnswerResult AnswerInApp(string id, bool allowed, out McpConfirmationEntry? entry)
	{
		lock (_gate)
		{
			if (!_entries.TryGetValue(id, out entry) || !entry.InApp || entry.ExpiresAt <= Now())
			{
				entry = null;
				return McpInAppAnswerResult.NotFound;
			}

			if (entry.Answered)
			{
				return McpInAppAnswerResult.AlreadyAnswered;
			}

			entry.Complete(allowed);
		}

		RaiseIf(true);
		return McpInAppAnswerResult.Answered;
	}

	// Only the caller that removes the entry gets its answer, so one Allow runs the call once.
	public bool TryConsumeInApp(string id, out bool allowed)
	{
		lock (_gate)
		{
			if (!_entries.TryGetValue(id, out var entry) || !entry.InApp || !entry.Answered)
			{
				allowed = false;
				return false;
			}

			allowed = entry.Completion.Result;
			_entries.TryRemove(id, out _);
			entry.ExpiryTimer?.Dispose();
			return true;
		}
	}

	public void AbandonForToken(Guid tokenId)
	{
		var changed = false;
		lock (_gate)
		{
			foreach (var entry in _entries.Values.Where(entry => entry.TokenId == tokenId).ToList())
			{
				changed |= Remove(entry.Id);
			}
		}

		RaiseIf(changed);
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
		bool changed;
		lock (_gate)
		{
			changed = Remove(id);
		}

		RaiseIf(changed);
	}

	private McpConfirmationEntry Add(
		Guid tokenId,
		string clientName,
		string fingerprint,
		IReadOnlyList<string> lines,
		IReadOnlyList<McpActionRef> actions,
		bool alwaysAllowable,
		bool inApp)
	{
		var now = Now();
		var entry = new McpConfirmationEntry(NewId(),
			tokenId,
			clientName,
			fingerprint,
			lines,
			actions,
			alwaysAllowable,
			inApp,
			now,
			now + Lifetime);
		_entries[entry.Id] = entry;
		return entry;
	}

	private bool MakeRoom(Guid tokenId)
	{
		var now = Now();
		var changed = false;
		foreach (var expired in _entries.Values.Where(entry => entry.ExpiresAt <= now).ToList())
		{
			changed |= Remove(expired.Id);
		}

		foreach (var stale in _entries.Values.Where(entry => entry.TokenId == tokenId)
			.OrderBy(entry => entry.ExpiresAt)
			.SkipLast(MaxPendingPerToken - 1)
			.ToList())
		{
			changed |= Remove(stale.Id);
		}

		return changed;
	}

	private bool Remove(string id)
	{
		if (!_entries.TryRemove(id, out var entry))
		{
			return false;
		}

		entry.Abandon();
		return entry.InApp;
	}

	// Raised outside the lock: subscribers publish to the UI and the notification list.
	private void RaiseIf(bool changed)
	{
		if (changed)
		{
			Changed?.Invoke();
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
