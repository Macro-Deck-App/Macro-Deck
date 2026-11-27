using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeckHost.Integrations.Clipboard.Native;
using Strings = MacroDeckHost.Localization.AppStrings.Integrations.Clipboard;

namespace MacroDeckHost.Integrations.Clipboard.Actions;

internal static class ClipboardErrors
{
	public static LocalizedText Describe(ClipboardAvailability availability)
	{
		return availability.Reason switch
		{
			ClipboardUnavailableReason.NoDesktopSession => Strings.Unavailable.NoDesktopSession(),
			ClipboardUnavailableReason.ToolMissing
				=> Strings.Unavailable.ToolMissing(availability.MissingTool ?? string.Empty),
			_ => Strings.Unavailable.UnsupportedOs()
		};
	}

	public static ActionResult From(ClipboardException exception, ClipboardAvailability availability)
	{
		return exception.Failure switch
		{
			ClipboardFailure.Locked => ActionResult.Failed(ActionErrorCodes.ProviderError, Strings.Errors.Locked()),
			ClipboardFailure.TooLarge => ActionResult.Failed(ActionErrorCodes.InvalidParameter,
				Strings.Errors.ContentTooLarge()),
			ClipboardFailure.UnsupportedFormat => ActionResult.Failed(ActionErrorCodes.InvalidParameter,
				Strings.Errors.UnsupportedImage()),
			ClipboardFailure.NotFound => ActionResult.Failed(ActionErrorCodes.NotFound,
				Strings.Errors.SourceNotFound()),
			ClipboardFailure.Refused => ActionResult.Failed(ActionErrorCodes.PermissionDenied,
				Strings.Errors.Refused()),
			ClipboardFailure.Unavailable => ActionResult.Failed(ActionErrorCodes.Unavailable,
				availability.IsAvailable ? Strings.Errors.Failed() : Describe(availability)),
			_ => ActionResult.Failed(ActionErrorCodes.ProviderError, Strings.Errors.Failed())
		};
	}
}
