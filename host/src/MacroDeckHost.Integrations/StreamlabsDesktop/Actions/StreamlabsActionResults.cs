using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Integrations.StreamlabsDesktop.Actions;

internal static class StreamlabsActionResults
{
	public static ActionResult ToActionResult(this StreamlabsCommandResult result) => result.Status switch
	{
		StreamlabsCommandStatus.Ok => ActionResult.Success(),
		StreamlabsCommandStatus.NotFound => ActionResult.Failed(ActionErrorCodes.NotFound,
			result.Message is { Length: > 0 } message
				? message
				: AppStrings.Integrations.StreamlabsDesktop.Errors.UnknownTarget()),
		StreamlabsCommandStatus.Rejected => ActionResult.Failed(ActionErrorCodes.ProviderRejected,
			result.Message is { Length: > 0 } rejectedMessage
				? rejectedMessage
				: AppStrings.Integrations.StreamlabsDesktop.Errors.RequestRefused()),
		_ => NotConnected
	};

	public static ActionResult NotConnected { get; } =
		ActionResult.Failed(ActionErrorCodes.NotConnected,
			AppStrings.Integrations.StreamlabsDesktop.Errors.NotConnected());

	public static ActionResult MissingParameter(LocalizedText message)
		=> ActionResult.Failed(ActionErrorCodes.InvalidParameter, message);
}
