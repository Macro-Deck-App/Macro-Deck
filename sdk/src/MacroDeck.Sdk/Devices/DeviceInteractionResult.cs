namespace MacroDeck.Sdk.Devices;

/// <summary>
/// The verdict on one interaction reported through <see cref="IDeviceSession.SendInteractionAsync" />.
/// A rejection is a normal outcome, not a failure: nothing ran, the session stays open, and the next
/// valid interaction still works - so a provider reports the next press instead of tearing anything down.
/// </summary>
public sealed record DeviceInteractionResult
{
	/// <summary>The interaction resolved and ran.</summary>
	public static readonly DeviceInteractionResult Accepted =
		new() { Status = DeviceInteractionStatus.Accepted };

	/// <summary>The kind is contract-legal but has no widget model yet, so nothing ran.</summary>
	public static readonly DeviceInteractionResult NotSupported =
		new() { Status = DeviceInteractionStatus.NotSupported };

	public required DeviceInteractionStatus Status { get; init; }

	/// <summary>A <see cref="DeviceSessionReasons" /> code, present exactly when
	/// <see cref="Status" /> is <see cref="DeviceInteractionStatus.Rejected" />.</summary>
	public string? ReasonCode { get; init; }

	/// <summary>True when the host took the interaction, whether or not it had anything to run for it.</summary>
	public bool IsAccepted => Status != DeviceInteractionStatus.Rejected;

	/// <summary>The host refused the interaction, naming a <see cref="DeviceSessionReasons" /> code.</summary>
	public static DeviceInteractionResult Rejected(string reasonCode)
		=> new() { Status = DeviceInteractionStatus.Rejected, ReasonCode = reasonCode };
}
