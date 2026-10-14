namespace MacroDeck.Sdk.Companion;

/// <summary>
/// The phones and tablets connected to Macro Deck through the Companion app: what they report about themselves,
/// and commands and screenshots for them. Macro Deck owns the connections: a plugin never connects or removes a
/// device.
/// </summary>
/// <remarks>
/// A plugin may use Companion devices only when the user allows plugins to use them and the plugin declares the
/// <c>host:companion-devices</c> permission in its manifest; <see cref="Access" /> says whether both hold. An
/// integration built into Macro Deck always has access. A full screen screenshot additionally needs the device's
/// user to agree on the device. Handlers run on a background thread; one that throws is logged and does not affect
/// the others. Members added in later versions arrive as default interface members.
/// </remarks>
public interface ICompanionDeviceApi
{
	/// <summary>
	/// Whether this caller may use Companion devices right now. <see cref="CompanionDeviceAccess.Unsupported" /> until
	/// Macro Deck has reported it, and permanently on a Macro Deck that predates this API.
	/// </summary>
	CompanionDeviceAccess Access { get; }

	/// <summary>
	/// The connected devices that have reported their state. Empty unless <see cref="Access" /> is
	/// <see cref="CompanionDeviceAccess.Available" />.
	/// </summary>
	IReadOnlyList<CompanionDevice> Devices { get; }

	/// <summary>Raised after <see cref="Access" /> changed.</summary>
	event EventHandler? AccessChanged;

	/// <summary>
	/// Raised when a device reported its first state since it connected, and for every device in
	/// <see cref="Devices" /> when access becomes available. Compare <see cref="CompanionDevice.ReadySequence" /> to
	/// tell a new connection from one already seen.
	/// </summary>
	event EventHandler<CompanionDeviceEventArgs>? DeviceReady;

	/// <summary>Raised after a device reported its state, and after it disconnected or was removed.</summary>
	event EventHandler<CompanionDeviceEventArgs>? DeviceChanged;

	/// <summary>The connected device with this id, or <c>null</c>.</summary>
	CompanionDevice? FindDevice(Guid deviceId);

	/// <summary>
	/// Waits for a device that has just connected to report its state. True at once when it is in
	/// <see cref="Devices" />; false when it has no open connection, or does not report within a few seconds.
	/// </summary>
	Task<bool> WaitForDeviceAsync(Guid deviceId, CancellationToken cancellationToken = default);

	/// <summary>
	/// Sends a command without waiting for the device to carry it out. False when the device is not connected or
	/// <see cref="Access" /> is not <see cref="CompanionDeviceAccess.Available" />.
	/// </summary>
	Task<bool> SendAsync(Guid deviceId, CompanionDeviceCommand command, CancellationToken cancellationToken = default);

	/// <summary>
	/// Sends a command and waits for the device's answer. Use it only for a device whose
	/// <see cref="CompanionDeviceState.ConfirmsCommands" /> is true, or for a capability in
	/// <see cref="CompanionDeviceState.RequestableCapabilities" />, where the device asks its user first.
	/// </summary>
	/// <remarks>
	/// The device has a minute to accept the command and a further few seconds to confirm it once accepted;
	/// a command accepted but never confirmed answers <see cref="CompanionDeviceCommandResult.Unconfirmed" />.
	/// Cancelling the token withdraws a command the device has not accepted yet and then throws
	/// <see cref="OperationCanceledException" />; once accepted, the request waits for the answer.
	/// </remarks>
	Task<CompanionDeviceCommandResult> RequestAsync(
		Guid deviceId,
		CompanionDeviceCommand command,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Captures the device's screen as a PNG. A <see cref="CompanionScreenshotMode.FullScreen" /> capture can show a
	/// permission prompt on the device and fails with <see cref="CompanionDeviceFailure.ConsentDenied" /> when its
	/// user declines. Waits like <see cref="RequestAsync" />, with up to a minute once the device accepted.
	/// </summary>
	Task<CompanionScreenshotResult> CaptureScreenshotAsync(
		Guid deviceId,
		CompanionScreenshotMode mode,
		CancellationToken cancellationToken = default);
}
