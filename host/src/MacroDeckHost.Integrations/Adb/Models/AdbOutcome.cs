using MacroDeck.Sdk.Android;

namespace MacroDeckHost.Integrations.Adb.Models;

internal sealed record AdbOutcome(AndroidDeviceErrorCode? Failure, string? Message)
{
	public static readonly AdbOutcome Ok = new(null, null);

	public bool Success => Failure is null;

	public static AdbOutcome Fail(AndroidDeviceErrorCode code, string? message = null)
	{
		return new AdbOutcome(code, message);
	}
}
