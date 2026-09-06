namespace MacroDeck.Sdk.Devices;

/// <summary>
/// Thrown when a device session cannot serve a request at all, as opposed to refusing it with an
/// outcome. Carries a <see cref="DeviceSessionReasons" /> code so a provider can branch on the cause
/// instead of parsing a message. The session itself stays open.
/// </summary>
public sealed class DeviceSessionException : Exception
{
	public DeviceSessionException(string reasonCode, string message)
		: base(message) => ReasonCode = reasonCode;

	public DeviceSessionException(string reasonCode, string message, Exception innerException)
		: base(message, innerException) => ReasonCode = reasonCode;

	public DeviceSessionException()
		=> ReasonCode = string.Empty;

	public DeviceSessionException(string message)
		: base(message) => ReasonCode = string.Empty;

	public DeviceSessionException(string message, Exception innerException)
		: base(message, innerException) => ReasonCode = string.Empty;

	/// <summary>A <see cref="DeviceSessionReasons" /> code, empty when the cause has none.</summary>
	public string ReasonCode { get; }
}
