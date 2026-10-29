using System.Collections.Concurrent;
using MacroDeckHost.Application.Mcp;

namespace MacroDeckHost.Mcp;

public sealed class McpConnectionRegistry : IMcpConnections
{
	private readonly ConcurrentDictionary<string, Session> _sessions = new(StringComparer.Ordinal);
	private readonly ConcurrentDictionary<HttpContext, Guid> _inFlight = new();
	private readonly TimeProvider _timeProvider;

	public McpConnectionRegistry(TimeProvider timeProvider)
	{
		_timeProvider = timeProvider;
	}

	public static TimeSpan IdleTimeout { get; } = TimeSpan.FromHours(2);

	public IReadOnlyList<McpConnection> Snapshot()
	{
		var cutoff = Now() - IdleTimeout;
		foreach (var stale in _sessions.Where(entry => entry.Value.LastSeenAt < cutoff).ToList())
		{
			_sessions.TryRemove(stale.Key, out _);
		}

		return _sessions.Where(entry => !entry.Value.Terminated)
			.Select(entry => new McpConnection
			{
				SessionId = entry.Key,
				TokenId = entry.Value.TokenId,
				ClientName = entry.Value.ClientName,
				ClientVersion = entry.Value.ClientVersion,
				ConnectedAt = entry.Value.ConnectedAt,
				LastSeenAt = entry.Value.LastSeenAt
			})
			.OrderBy(connection => connection.ConnectedAt)
			.ToList();
	}

	public int TerminateForToken(Guid tokenId)
	{
		var terminated = 0;
		foreach (var session in _sessions.Values.Where(session => session.TokenId == tokenId && !session.Terminated))
		{
			session.Terminated = true;
			terminated++;
		}

		foreach (var request in _inFlight.Where(entry => entry.Value == tokenId).Select(entry => entry.Key).ToList())
		{
			request.Abort();
		}

		return terminated;
	}

	public bool IsBoundToOtherToken(string sessionId, Guid tokenId)
	{
		return _sessions.TryGetValue(sessionId, out var session) && session.TokenId != tokenId;
	}

	public void Bind(string sessionId, Guid tokenId)
	{
		var now = Now();
		_sessions.TryAdd(sessionId, new Session(tokenId, now) { LastSeenAt = now });
	}

	public void Touch(string sessionId, string? clientName, string? clientVersion)
	{
		if (_sessions.TryGetValue(sessionId, out var session))
		{
			session.LastSeenAt = Now();
			session.ClientName = clientName ?? session.ClientName;
			session.ClientVersion = clientVersion ?? session.ClientVersion;
		}
	}

	// Clients on the 2026-07-28 revision have no session, so they are listed once per token instead.
	public void TouchStateless(Guid tokenId, string? clientName, string? clientVersion)
	{
		var now = Now();
		var session = _sessions.GetOrAdd(StatelessKey(tokenId), _ => new Session(tokenId, now));
		session.LastSeenAt = now;
		session.ClientName = clientName ?? session.ClientName;
		session.ClientVersion = clientVersion ?? session.ClientVersion;
	}

	public void Remove(string sessionId)
	{
		_sessions.TryRemove(sessionId, out _);
	}

	public void BeginRequest(HttpContext context, Guid tokenId)
	{
		_inFlight[context] = tokenId;
	}

	public void EndRequest(HttpContext context)
	{
		_inFlight.TryRemove(context, out _);
	}

	private static string StatelessKey(Guid tokenId)
	{
		return "stateless:" + tokenId.ToString("N");
	}

	private DateTime Now()
	{
		return _timeProvider.GetUtcNow().UtcDateTime;
	}

	private sealed class Session
	{
		public Session(Guid tokenId, DateTime connectedAt)
		{
			TokenId = tokenId;
			ConnectedAt = connectedAt;
		}

		public Guid TokenId { get; }

		public DateTime ConnectedAt { get; }

		public DateTime LastSeenAt { get; set; }

		public string? ClientName { get; set; }

		public string? ClientVersion { get; set; }

		public bool Terminated { get; set; }
	}
}
