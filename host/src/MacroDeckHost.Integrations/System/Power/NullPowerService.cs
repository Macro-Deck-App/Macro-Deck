using MacroDeck.Sdk.Actions;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Integrations.System.Power;

internal sealed class NullPowerService : IPowerService
{
	public bool IsSupported => false;

	public bool Supports(PowerOperation operation) => false;

	public Task<PowerResult> ExecuteAsync(
		PowerOperation operation,
		bool force,
		CancellationToken cancellationToken = default)
		=> Task.FromResult(PowerResult.Failed(AppStrings.Integrations.System.Errors.Power.NotSupportedOnOs(),
			ActionErrorCodes.Unavailable));
}
