namespace MacroDeck.Sdk.ScreenSavers;

/// <summary>The host's identity for a registered screensaver.</summary>
/// <param name="ScreenSaverId">
/// The qualified id - <c>provider.id::screensaver-id</c> - a device stores to select this screensaver. The
/// host derives it; a provider never constructs it itself. Empty when the host predates screensavers and
/// could not register it: the provider keeps running and simply offers nothing there.
/// </param>
/// <param name="ProviderId">The integration or plugin that owns the screensaver.</param>
public sealed record ScreenSaverRegistration(string ScreenSaverId, string ProviderId);
