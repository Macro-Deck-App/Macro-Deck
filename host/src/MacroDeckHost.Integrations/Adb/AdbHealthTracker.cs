namespace MacroDeckHost.Integrations.Adb;

internal sealed class AdbHealthTracker
{
	private AdbGatewayFailureCode? _lastFailure;

	public AdbGatewayFailureCode? LastFailure => _lastFailure;

	public void Record(AdbGatewayResult result) => _lastFailure = result.Success ? null : result.Failure;
}
