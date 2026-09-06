using MacroDeck.Localization;

namespace MacroDeckHost.Tests.UnitTests.ClientTargets;

/// <summary>Returns the key, which is all a test needs to see that the right string was asked for.</summary>
internal sealed class FakeLocalizationResolver : ILocalizationResolver
{
	public string Resolve(LocalizedString value, string? culture) => value.Key.ToString();

	public string? Resolve(LocalizedText text, string? culture)
		=> text.Literal ?? (text.Localized is { } localized ? Resolve(localized, culture) : null);
}
