using MacroDeckHost.Application.Actions.Options;
using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.Persistence.Repositories;
using MacroDeckHost.Application.Profiles;
using MacroDeckHost.Application.Services;
using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Decks;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Infrastructure.OptionsSources;

public sealed class ProfilesOptionsSource : IHostOptionsSource
{
	private readonly IProfileRegistry _profiles;

	public ProfilesOptionsSource(IProfileRegistry profiles)
	{
		_profiles = profiles;
	}

	public string Id => DeckOptionsSourceIds.Profiles;

	public Task<DynamicOptionsResult> GetOptionsAsync(string? filter, CancellationToken cancellationToken)
	{
		var options = _profiles.GetProfiles()
			.Select(profile => new ActionParameterOption { Value = profile.Id, Label = profile.Name })
			.Where(option => filter is null ||
				(option.Label.Literal ?? string.Empty).Contains(filter, StringComparison.OrdinalIgnoreCase))
			.OrderBy(option => option.Label.Literal, StringComparer.OrdinalIgnoreCase)
			.ToList();

		return Task.FromResult(new DynamicOptionsResult
		{
			Options = options,
			AllowsCustomValue = false,
			CacheSeconds = 5
		});
	}
}

public sealed class FoldersOptionsSource : IHostOptionsSource
{
	private readonly IDeckNavigator _navigator;

	public FoldersOptionsSource(IDeckNavigator navigator)
	{
		_navigator = navigator;
	}

	public string Id => DeckOptionsSourceIds.Folders;

	public Task<DynamicOptionsResult> GetOptionsAsync(string? filter, CancellationToken cancellationToken)
	{
		var options = _navigator.GetFolders()
			.Select(folder => new ActionParameterOption { Value = folder.Id, Label = folder.Label })
			.Where(option => filter is null ||
				(option.Label.Literal ?? string.Empty).Contains(filter, StringComparison.OrdinalIgnoreCase))
			.ToList();

		return Task.FromResult(new DynamicOptionsResult
		{
			Options = options,
			AllowsCustomValue = false,
			CacheSeconds = 5
		});
	}
}

public sealed class DevicesOptionsSource : IHostOptionsSource
{
	private readonly IServiceScopeFactory _scopeFactory;

	public DevicesOptionsSource(IServiceScopeFactory scopeFactory)
	{
		_scopeFactory = scopeFactory;
	}

	public string Id => DeckOptionsSourceIds.Devices;

	public async Task<DynamicOptionsResult> GetOptionsAsync(string? filter, CancellationToken cancellationToken)
	{
		using var scope = _scopeFactory.CreateScope();
		var repository = scope.ServiceProvider.GetRequiredService<IDeviceRepository>();
		var devices = await repository.GetAll();

		var options = devices
			.Select(device => new ActionParameterOption { Value = device.Id.ToString(), Label = device.Name })
			.Where(option => filter is null ||
				(option.Label.Literal ?? string.Empty).Contains(filter, StringComparison.OrdinalIgnoreCase))
			.OrderBy(option => option.Label.Literal, StringComparer.OrdinalIgnoreCase)
			.ToList();

		return new DynamicOptionsResult
		{
			Options = options,
			AllowsCustomValue = false,
			CacheSeconds = 5
		};
	}
}

public sealed class IntegrationsOptionsSource : IHostOptionsSource
{
	private readonly IIntegrationRegistry _integrations;
	private readonly IServiceScopeFactory _scopeFactory;
	private readonly ILocalizationResolver _localization;

	public IntegrationsOptionsSource(
		IIntegrationRegistry integrations,
		IServiceScopeFactory scopeFactory,
		ILocalizationResolver localization)
	{
		_integrations = integrations;
		_scopeFactory = scopeFactory;
		_localization = localization;
	}

	public string Id => DeckOptionsSourceIds.Integrations;

	public async Task<DynamicOptionsResult> GetOptionsAsync(string? filter, CancellationToken cancellationToken)
	{
		// The filter the client typed and the ordering are both applied here, so an integration name that
		// is a localization reference is resolved before it becomes a label.
		var culture = await ActiveLocalization.Culture(_scopeFactory);

		var options = _integrations.Integrations
			.Select(integration => new ActionParameterOption
			{
				Value = integration.Id,
				Label = _localization.Resolve(integration.Name, culture)
			})
			.Where(option => filter is null ||
				(option.Label.Literal ?? string.Empty).Contains(filter, StringComparison.OrdinalIgnoreCase))
			.OrderBy(option => option.Label.Literal, StringComparer.OrdinalIgnoreCase)
			.ToList();

		return new DynamicOptionsResult
		{
			Options = options,
			AllowsCustomValue = false,
			CacheSeconds = 5
		};
	}
}
