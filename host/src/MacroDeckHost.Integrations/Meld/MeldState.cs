namespace MacroDeckHost.Integrations.Meld;

internal sealed record MeldState
{
	public static MeldState Disconnected { get; } = new()
	{
		IsConnected = false,
		ApiVersion = 0,
		SupportsSetMuted = false,
		SupportsSetProperty = false,
		IsStreaming = false,
		IsRecording = false,
		Session = MeldSession.Empty
	};

	public required bool IsConnected { get; init; }

	public required int ApiVersion { get; init; }

	public required bool SupportsSetMuted { get; init; }

	public required bool SupportsSetProperty { get; init; }

	public required bool IsStreaming { get; init; }

	public required bool IsRecording { get; init; }

	public required MeldSession Session { get; init; }
}
