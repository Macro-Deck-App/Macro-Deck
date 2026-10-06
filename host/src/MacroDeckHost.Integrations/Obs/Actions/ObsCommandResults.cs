using MacroDeck.Sdk.Actions;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Integrations.Obs.Actions;

internal static class ObsCommandResults
{
	public static ActionResult ToActionResult(ObsCommandResult result) => result.Outcome switch
	{
		ObsCommandOutcome.Success => ActionResult.Success(),
		ObsCommandOutcome.NotRecording => ActionResult.Failed(ActionErrorCodes.Unavailable,
			AppStrings.Integrations.Obs.Errors.NotRecording()),
		ObsCommandOutcome.Rejected => ActionResult.Failed(ActionErrorCodes.ProviderRejected,
			AppStrings.Integrations.Obs.Errors.RequestRejected(result.Message ?? string.Empty)),
		_ => ActionResult.Failed(ActionErrorCodes.NotConnected, AppStrings.Integrations.Obs.Errors.NotConnected())
	};
}
