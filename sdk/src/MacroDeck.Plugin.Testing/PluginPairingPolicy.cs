using MacroDeck.Plugin.Protocol.Handshake;

namespace MacroDeck.Plugin.Testing;

/// <summary>
/// How <see cref="MacroDeckTestHost" /> answers a pairing request's status - the outcome the desktop
/// app's own approval prompt would eventually produce, decided up front instead of by driving a UI.
/// </summary>
public sealed class PluginPairingPolicy
{
	private enum Kind
	{
		AutoApprove,
		Reject,
		Pending
	}

	private readonly Kind _kind;

	private PluginPairingPolicy(Kind kind) => _kind = kind;

	/// <summary>
	/// Approves every pairing request the moment it is created, as if a human were already sitting at
	/// the approval prompt. The default: a conformance run is automated end to end and must never block
	/// on a human, so nothing about self-registration through pairing waits for one unless a test
	/// deliberately opts into <see cref="Pending" /> or <see cref="Reject" />.
	/// </summary>
	public static PluginPairingPolicy AutoApprove { get; } = new(Kind.AutoApprove);

	/// <summary>Rejects every pairing request the moment it is created, as if a human had declined the prompt.</summary>
	public static PluginPairingPolicy Reject { get; } = new(Kind.Reject);

	/// <summary>Leaves every pairing request pending forever - nobody has answered the prompt yet.</summary>
	public static PluginPairingPolicy Pending { get; } = new(Kind.Pending);

	/// <summary>The status a freshly created pairing request starts in under this policy.</summary>
	internal string InitialStatus => _kind switch
	{
		Kind.AutoApprove => PluginPairingStatuses.Approved,
		Kind.Reject => PluginPairingStatuses.Rejected,
		_ => PluginPairingStatuses.Pending
	};
}
