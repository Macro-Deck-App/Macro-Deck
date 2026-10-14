using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeckHost.Application.Actions.Options;
using MacroDeckHost.Application.Email;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Localization;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Infrastructure.OptionsSources;

public sealed class EmailInboxOptionsSource : IHostOptionsSource
{
	private readonly IEmailInboxRegistry _registry;
	private readonly IServiceScopeFactory _scopeFactory;
	private readonly ILocalizationResolver _localization;

	public EmailInboxOptionsSource(
		IEmailInboxRegistry registry,
		IServiceScopeFactory scopeFactory,
		ILocalizationResolver localization)
	{
		_registry = registry;
		_scopeFactory = scopeFactory;
		_localization = localization;
	}

	public string Id => EmailOptionsSourceIds.Inboxes;

	public async Task<DynamicOptionsResult> GetOptionsAsync(string? filter, CancellationToken cancellationToken)
	{
		var culture = await ActiveLocalization.Culture(_scopeFactory);

		var options = _registry.GetInboxes()
			.Select(inbox =>
			{
				var provider = _localization.Resolve(inbox.ProviderName, culture) ?? string.Empty;
				var label = string.IsNullOrEmpty(provider)
					? inbox.DisplayName
					: _localization.Resolve(AppStrings.Events.Email.InboxOption(inbox.DisplayName,
							provider),
						culture)
					?? inbox.DisplayName;
				return new ActionParameterOption { Value = inbox.Ref, Label = label };
			})
			.Where(option
				=> filter is null
				|| (option.Label.Literal ?? string.Empty).Contains(filter, StringComparison.OrdinalIgnoreCase))
			.OrderBy(option => option.Label.Literal, StringComparer.OrdinalIgnoreCase)
			.ToList();

		return new DynamicOptionsResult { Options = options, AllowsCustomValue = true, CacheSeconds = 5 };
	}
}
