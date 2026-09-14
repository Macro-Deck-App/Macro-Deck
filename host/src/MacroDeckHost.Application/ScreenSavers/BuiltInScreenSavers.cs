namespace MacroDeckHost.Application.ScreenSavers;

public static class BuiltInScreenSavers
{
	public const string ProviderId = "app.macro-deck.screensavers";

	public const string Clock = ProviderId + "::clock";

	public const string NowPlaying = ProviderId + "::now-playing";

	public static bool IsBuiltIn(string? screenSaverId)
		=> screenSaverId is not null &&
			screenSaverId.StartsWith(ProviderId + "::", StringComparison.Ordinal);
}
