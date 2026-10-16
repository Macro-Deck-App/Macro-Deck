using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Companion;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Integrations.Companion.Actions;

internal static class CompanionCapabilities
{
	public const string ScreenOn = CompanionDeviceCapabilities.ScreenOn;
	public const string ScreenOff = CompanionDeviceCapabilities.ScreenOff;
	public const string ScreenshotDeck = CompanionDeviceCapabilities.ScreenshotDeck;
	public const string ScreenshotFull = CompanionDeviceCapabilities.ScreenshotFull;
	public const string Focus = CompanionDeviceCapabilities.Focus;

	internal static (ActionResult? Error, bool Prompt, bool AwaitResult) Require(
		ICompanionDeviceApi devices,
		Guid deviceId,
		string capability)
	{
		if (devices.FindDevice(deviceId)?.State is not { } state)
		{
			return (CompanionTargetResolver.NotConnected(), false, false);
		}

		if (state.Capabilities.Contains(capability))
		{
			return (null, false, state.ConfirmsCommands);
		}

		return state.RequestableCapabilities.Contains(capability)
			? (null, true, true)
			: (Unavailable(capability), false, false);
	}

	internal static ActionResult Failed(CompanionDeviceFailure failure, string capability, bool prompted)
	{
		return failure switch
		{
			CompanionDeviceFailure.NotConnected => CompanionTargetResolver.NotConnected(),
			CompanionDeviceFailure.Removed => ActionResult.Failed(ActionErrorCodes.NotFound,
				AppStrings.Integrations.Companion.Errors.ConfigurationNotFound()),
			CompanionDeviceFailure.TimedOut => ActionResult.Failed(ActionErrorCodes.Timeout,
				TimedOutMessage(capability, prompted)),
			CompanionDeviceFailure.ConsentDenied => ActionResult.Failed(ActionErrorCodes.PermissionDenied,
				AppStrings.Integrations.Companion.Errors.PermissionDeclined()),
			CompanionDeviceFailure.Unavailable => Unavailable(capability),
			_ => ActionResult.Failed(ActionErrorCodes.ProviderError,
				capability is ScreenshotDeck or ScreenshotFull
					? AppStrings.Integrations.Companion.Errors.ScreenshotFailed()
					: AppStrings.Integrations.Companion.Errors.CommandFailed())
		};
	}

	internal static ActionResult Unavailable(string capability)
	{
		return ActionResult.Failed(ActionErrorCodes.Unavailable, UnavailableMessage(capability));
	}

	private static LocalizedText TimedOutMessage(string capability, bool prompted)
	{
		return capability switch
		{
			_ when prompted => AppStrings.Integrations.Companion.Errors.PermissionTimedOut(),
			ScreenshotDeck or ScreenshotFull => AppStrings.Integrations.Companion.Errors.ScreenshotTimedOut(),
			_ => AppStrings.Integrations.Companion.Errors.CommandFailed()
		};
	}

	private static LocalizedText UnavailableMessage(string capability)
	{
		return capability switch
		{
			ScreenOn => AppStrings.Integrations.Companion.Errors.ScreenOnUnavailable(),
			ScreenOff => AppStrings.Integrations.Companion.Errors.ScreenOffUnavailable(),
			Focus => AppStrings.Integrations.Companion.Errors.FocusUnavailable(),
			ScreenshotDeck => AppStrings.Integrations.Companion.Errors.DeckNotOnScreen(),
			_ => AppStrings.Integrations.Companion.Errors.FullScreenshotUnavailable()
		};
	}
}
