using MacroDeck.Sdk.Android;

namespace MacroDeckHost.Integrations.Adb;

internal sealed class AdbHealthTracker
{
	private AndroidDeviceErrorCode? _lastFailure;

	public AndroidDeviceErrorCode? LastFailure => _lastFailure;

	public void Record(AdbOutcome outcome)
	{
		_lastFailure = outcome.Failure;
	}
}
