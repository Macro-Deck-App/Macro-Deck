using MacroDeck.Localization;
using MacroDeckHost.Application.Services;

namespace MacroDeckHost.Mcp;

public sealed class McpTextResolver
{
	private readonly ILocalizationResolver _resolver;
	private readonly IAppPreferenceService _preferences;
	private string? _culture;

	public McpTextResolver(ILocalizationResolver resolver, IAppPreferenceService preferences)
	{
		_resolver = resolver;
		_preferences = preferences;
	}

	public async Task<string> Culture()
	{
		return _culture ??= (await _preferences.GetLocalization()).Culture;
	}

	public async Task<string?> Resolve(LocalizedText text)
	{
		return text.IsEmpty ? null : _resolver.Resolve(text, await Culture());
	}
}
