namespace MacroDeckHost.Integrations.Ping.State;

internal enum PingReachability
{
	Unknown,
	Online,
	Offline
}

internal enum PingProblem
{
	None,
	TimedOut,
	Unreachable,
	NameNotResolved,
	NotPermitted,
	Failed
}

internal sealed record PingSnapshot(Guid EntryId, string Title, string Key, string Host)
{
	public PingReachability Reachability { get; init; }

	public long? LatencyMilliseconds { get; init; }

	public double? PacketLossPercent { get; init; }

	public DateTimeOffset? LastSeen { get; init; }

	public PingProblem Problem { get; init; }

	public TimeSpan Interval { get; init; }

	public bool IsOnline => Reachability == PingReachability.Online;
}
