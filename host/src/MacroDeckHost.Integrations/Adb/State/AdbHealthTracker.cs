using MacroDeck.Sdk.Android;
using MacroDeckHost.Integrations.Adb.Models;

namespace MacroDeckHost.Integrations.Adb.State;

internal sealed class AdbHealthTracker
{
	private AndroidDeviceErrorCode? _lastFailure;

	public AndroidDeviceErrorCode? LastFailure => _lastFailure;

	public void Record(AdbOutcome outcome)
	{
		_lastFailure = outcome.Failure;
	}
}
