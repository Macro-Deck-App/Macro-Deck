using MacroDeck.Sdk.Actions;
using MacroDeckHost.Integrations.Obs.Connections;
using MacroDeckHost.Integrations.Obs.Protocol;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Integrations.Obs.Actions;

internal static class ObsCommandResults
{
	public static ActionResult ToActionResult(ObsCommandResult result)
	{
		return result.Outcome switch
		{
			ObsCommandOutcome.Success => ActionResult.Success(),
			ObsCommandOutcome.Rejected when result.Code == ObsRequestException.TimedOut => ActionResult.Failed(
				ActionErrorCodes.Timeout,
				AppStrings.Integrations.Obs.Errors.TimedOut()),
			ObsCommandOutcome.Rejected => ActionResult.Failed(ActionErrorCodes.ProviderRejected,
				AppStrings.Integrations.Obs.Errors.RequestRejected(result.Message ?? string.Empty)),
			_ => ActionResult.Failed(ActionErrorCodes.NotConnected, AppStrings.Integrations.Obs.Errors.NotConnected())
		};
	}

	public static ActionResult ToRecordingActionResult(ObsCommandResult result)
	{
		return result is { Outcome: ObsCommandOutcome.Rejected, Code: ObsRequestException.OutputNotRunning }
			? ActionResult.Failed(ActionErrorCodes.Unavailable, AppStrings.Integrations.Obs.Errors.NotRecording())
			: ToActionResult(result);
	}
}
