using MacroDeck.Sdk.Actions;
using MacroDeckHost.Integrations.YouTube.Protocol;
using Errors = MacroDeckHost.Localization.AppStrings.Integrations.YouTube.Errors;

namespace MacroDeckHost.Integrations.YouTube.Actions;

internal static class YouTubeActionErrors
{
	public static ActionResult QuotaExhausted()
		=> ActionResult.Failed(ActionErrorCodes.Unavailable, Errors.QuotaExhausted());

	public static ActionResult From(YouTubeApiException exception)
	{
		if (exception.IsQuotaExceeded)
		{
			return QuotaExhausted();
		}

		if (exception.IsRateLimited)
		{
			return ActionResult.Failed(ActionErrorCodes.ProviderRejected, Errors.RateLimited());
		}

		if (exception.IsUnauthorized)
		{
			return ActionResult.Failed(ActionErrorCodes.PermissionDenied, Errors.SignInExpired());
		}

		return exception.Reason switch
		{
			"insufficientPermissions" => ActionResult.Failed(ActionErrorCodes.PermissionDenied,
				Errors.MissingPermission()),
			"errorStreamInactive" => ActionResult.Failed(ActionErrorCodes.ProviderRejected, Errors.StreamInactive()),
			"invalidTransition" => ActionResult.Failed(ActionErrorCodes.ProviderRejected,
				Errors.InvalidTransition()),
			"liveStreamingNotEnabled" => ActionResult.Failed(ActionErrorCodes.PermissionDenied,
				Errors.LiveStreamingNotEnabled()),
			"insufficientLivePermissions" => ActionResult.Failed(ActionErrorCodes.PermissionDenied,
				Errors.InsufficientLivePermissions()),
			"liveChatEnded" or "liveChatNotFound" or "liveChatDisabled" => ActionResult.Failed(
				ActionErrorCodes.NotFound,
				Errors.NoLiveChat()),
			_ => ActionResult.Failed(ActionErrorCodes.ProviderRejected, Errors.RequestRejected())
		};
	}
}
