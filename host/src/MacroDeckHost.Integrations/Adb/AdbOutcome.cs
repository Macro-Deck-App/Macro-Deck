using MacroDeck.Sdk.Android;

namespace MacroDeckHost.Integrations.Adb;

internal sealed record AdbOutcome(AndroidDeviceErrorCode? Failure, string? Message)
{
	public static readonly AdbOutcome Ok = new(null, null);

	public bool Success => Failure is null;

	public static AdbOutcome Fail(AndroidDeviceErrorCode code, string? message = null) => new(code, message);
}
