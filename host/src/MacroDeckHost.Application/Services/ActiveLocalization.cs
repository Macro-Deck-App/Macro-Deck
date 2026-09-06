using MacroDeck.Localization;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Application.Services;

/// <summary>
/// The active UI language, for the places where the host itself needs finished text - stored state, a
/// composed sentence, a comparison - rather than a reference a client would resolve for its reader.
/// </summary>
public static class ActiveLocalization
{
	public static async Task<string?> Culture(IServiceProvider services)
		=> (await services.GetRequiredService<IAppPreferenceService>().GetLocalization()).Culture;

	public static async Task<string?> Culture(IServiceScopeFactory scopeFactory)
	{
		await using var scope = scopeFactory.CreateAsyncScope();
		return await Culture(scope.ServiceProvider);
	}

	public static async Task<string> Resolve(IServiceProvider services, LocalizedText text)
	{
		var resolver = services.GetRequiredService<ILocalizationResolver>();
		return resolver.Resolve(text, await Culture(services)) ?? string.Empty;
	}

	public static async Task<string> Resolve(IServiceScopeFactory scopeFactory, LocalizedText text)
	{
		await using var scope = scopeFactory.CreateAsyncScope();
		return await Resolve(scope.ServiceProvider, text);
	}
}
