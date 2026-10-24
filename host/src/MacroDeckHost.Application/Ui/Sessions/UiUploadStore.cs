using System.Security.Cryptography;
using MacroDeck.Plugin.Protocol.Assets;

namespace MacroDeckHost.Application.Ui.Sessions;

public enum UiUploadOutcome
{
	Stored,

	NoSuchSession,

	Full
}

public sealed record UiUpload
{
	public required string UploadId { get; init; }

	public required ReadOnlyMemory<byte> Content { get; init; }

	public required string ContentHash { get; init; }
}

public sealed record UiUploadResult
{
	public required UiUploadOutcome Outcome { get; init; }

	public UiUpload? Upload { get; init; }
}

public sealed class UiUploadStore : IDisposable
{
	public const int MaxPendingPerSession = 4;

	public static TimeSpan Lifetime { get; } = TimeSpan.FromMinutes(5);

	private readonly UiSessionRegistry _sessions;
	private readonly TimeProvider _time;
	private readonly Lock _gate = new();
	private readonly Dictionary<string, List<Entry>> _bySession = new(StringComparer.Ordinal);

	public UiUploadStore(UiSessionRegistry sessions, TimeProvider time)
	{
		_sessions = sessions;
		_time = time;
		_sessions.Ended += OnSessionEnded;
	}

	public UiUploadResult Add(string sessionId, string? nodeId, string principal, ReadOnlyMemory<byte> content)
	{
		ArgumentNullException.ThrowIfNull(principal);

		// Only the client that owns the session may stage text for it, so an upload can never be pushed into
		// another principal's session by guessing its id.
		if (_sessions.Find(sessionId) is not { State: UiSessionState.Opening or UiSessionState.Open } session
			|| !string.Equals(session.OwnerPrincipal, principal, StringComparison.Ordinal))
		{
			return new UiUploadResult { Outcome = UiUploadOutcome.NoSuchSession };
		}

		var upload = new UiUpload
		{
			UploadId = NewId(),
			Content = content.ToArray(),
			ContentHash = AssetContentHash.Compute(content.Span)
		};
		var now = _time.GetUtcNow();
		var node = nodeId ?? string.Empty;

		lock (_gate)
		{
			if (!_bySession.TryGetValue(session.SessionId, out var entries))
			{
				entries = [];
				_bySession[session.SessionId] = entries;
			}

			entries.RemoveAll(entry => entry.ExpiresAt <= now
				|| string.Equals(entry.NodeId, node, StringComparison.Ordinal));

			if (entries.Count >= MaxPendingPerSession)
			{
				return new UiUploadResult { Outcome = UiUploadOutcome.Full };
			}

			entries.Add(new Entry(node, upload, now + Lifetime));
		}

		return new UiUploadResult { Outcome = UiUploadOutcome.Stored, Upload = upload };
	}

	public UiUpload? Read(string providerId, string sessionId, string uploadId)
	{
		if (_sessions.Find(sessionId) is not { } session
			|| !string.Equals(session.ProviderId, providerId, StringComparison.Ordinal))
		{
			return null;
		}

		var now = _time.GetUtcNow();

		lock (_gate)
		{
			return _bySession.TryGetValue(sessionId, out var entries)
				? entries.FirstOrDefault(entry => entry.ExpiresAt > now
						&& string.Equals(entry.Upload.UploadId, uploadId, StringComparison.Ordinal))
					?.Upload
				: null;
		}
	}

	public void Dispose()
	{
		_sessions.Ended -= OnSessionEnded;
	}

	private void OnSessionEnded(object? sender, UiSessionEndedEventArgs e)
	{
		lock (_gate)
		{
			_bySession.Remove(e.Session.SessionId);
		}
	}

	private static string NewId()
	{
		return Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
	}

	private sealed record Entry(string NodeId, UiUpload Upload, DateTimeOffset ExpiresAt);
}
