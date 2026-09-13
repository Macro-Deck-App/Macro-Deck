using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeckHost.Integrations.Companion.Actions;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Integrations.Companion;

public static class CompanionCapabilities
{
	public const string ScreenOn = "screenOn";
	public const string ScreenOff = "screenOff";
	public const string ScreenshotDeck = "screenshotDeck";
	public const string ScreenshotFull = "screenshotFull";
	public const string Focus = "focus";
	public const string NetworkName = "networkName";
	public const string Cpu = "cpu";
	public const string Memory = "memory";

	public static IReadOnlySet<string> All { get; } = new HashSet<string>(StringComparer.Ordinal)
	{
		ScreenOn, ScreenOff, ScreenshotDeck, ScreenshotFull, Focus, NetworkName, Cpu, Memory
	};

	public static IReadOnlySet<string> Requestable { get; } = new HashSet<string>(StringComparer.Ordinal)
	{
		ScreenOn, ScreenOff, Focus, ScreenshotFull
	};

	internal static (ActionResult? Error, bool Prompt, bool AwaitResult) Require(ICompanionGateway gateway,
		Guid deviceId,
		string capability)
	{
		if (!gateway.TryGetState(deviceId, out var state))
		{
			return (CompanionTargetResolver.NotConnected(), false, false);
		}

		if (state.Capabilities.Contains(capability))
		{
			return (null, false, state.AnswersCommands);
		}

		return state.RequestableCapabilities.Contains(capability)
			? (null, true, true)
			: (Unavailable(capability), false, false);
	}

	internal static ActionResult Failed(CompanionCommandFailure failure, string capability, bool prompted)
		=> failure switch
		{
			CompanionCommandFailure.NotConnected => CompanionTargetResolver.NotConnected(),
			CompanionCommandFailure.Removed => ActionResult.Failed(ActionErrorCodes.NotFound,
				AppStrings.Integrations.Companion.Errors.ConfigurationNotFound()),
			CompanionCommandFailure.TimedOut => ActionResult.Failed(ActionErrorCodes.Timeout,
				TimedOutMessage(capability, prompted)),
			CompanionCommandFailure.ConsentDenied => ActionResult.Failed(ActionErrorCodes.PermissionDenied,
				AppStrings.Integrations.Companion.Errors.PermissionDeclined()),
			CompanionCommandFailure.Unavailable => Unavailable(capability),
			_ => ActionResult.Failed(ActionErrorCodes.ProviderError,
				capability is ScreenshotDeck or ScreenshotFull
					? AppStrings.Integrations.Companion.Errors.ScreenshotFailed()
					: AppStrings.Integrations.Companion.Errors.CommandFailed())
		};

	internal static ActionResult Unavailable(string capability)
		=> ActionResult.Failed(ActionErrorCodes.Unavailable, UnavailableMessage(capability));

	private static LocalizedText TimedOutMessage(string capability, bool prompted) => capability switch
	{
		_ when prompted => AppStrings.Integrations.Companion.Errors.PermissionTimedOut(),
		ScreenshotDeck or ScreenshotFull => AppStrings.Integrations.Companion.Errors.ScreenshotTimedOut(),
		_ => AppStrings.Integrations.Companion.Errors.CommandFailed()
	};

	private static LocalizedText UnavailableMessage(string capability) => capability switch
	{
		ScreenOn => AppStrings.Integrations.Companion.Errors.ScreenOnUnavailable(),
		ScreenOff => AppStrings.Integrations.Companion.Errors.ScreenOffUnavailable(),
		Focus => AppStrings.Integrations.Companion.Errors.FocusUnavailable(),
		ScreenshotDeck => AppStrings.Integrations.Companion.Errors.DeckNotOnScreen(),
		_ => AppStrings.Integrations.Companion.Errors.FullScreenshotUnavailable()
	};
}
