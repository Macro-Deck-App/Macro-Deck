using MacroDeckHost.Application.Actions.Options;
using MacroDeckHost.Application.MusicPlayer;
using MacroDeckHost.Application.Services;
using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Infrastructure.OptionsSources;

public sealed class MusicPlayerInstancesOptionsSource : IHostOptionsSource
{
	private readonly IMusicPlayerRegistry _registry;
	private readonly IServiceScopeFactory _scopeFactory;
	private readonly ILocalizationResolver _localization;

	public MusicPlayerInstancesOptionsSource(
		IMusicPlayerRegistry registry,
		IServiceScopeFactory scopeFactory,
		ILocalizationResolver localization)
	{
		_registry = registry;
		_scopeFactory = scopeFactory;
		_localization = localization;
	}

	public string Id => MusicPlayerOptionsSourceIds.Instances;

	public async Task<DynamicOptionsResult> GetOptionsAsync(string? filter, CancellationToken cancellationToken)
	{
		// A label composed from the provider name cannot stay a reference, and the "more than one
		// provider" test compares those names, so they are resolved once up front.
		var culture = await ActiveLocalization.Culture(_scopeFactory);
		var instances = _registry.GetInstances()
			.Select(instance => (Instance: instance, Provider: _localization.Resolve(instance.ProviderName, culture)))
			.ToList();

		var qualifyWithProvider = instances
				.Select(entry => entry.Provider)
				.Distinct(StringComparer.Ordinal)
				.Count() >
			1;

		var options = instances
			.Select(entry => new ActionParameterOption
			{
				Value = entry.Instance.InstanceId,
				Label = qualifyWithProvider
					? $"{entry.Provider} - {entry.Instance.DisplayName}"
					: entry.Instance.DisplayName
			})
			.Where(option => filter is null ||
				(option.Label.Literal ?? string.Empty).Contains(filter, StringComparison.OrdinalIgnoreCase))
			.OrderBy(option => option.Label.Literal, StringComparer.OrdinalIgnoreCase)
			.ToList();

		return new DynamicOptionsResult
		{
			Options = options,
			AllowsCustomValue = true,
			CacheSeconds = 5
		};
	}
}
