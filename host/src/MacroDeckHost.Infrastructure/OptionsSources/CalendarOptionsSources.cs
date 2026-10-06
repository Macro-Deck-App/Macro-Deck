using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeckHost.Application.Actions.Options;
using MacroDeckHost.Application.Calendar;
using MacroDeckHost.Application.Services;
using Microsoft.Extensions.DependencyInjection;
using Strings = MacroDeckHost.Localization.AppStrings.Widgets.Calendar;

namespace MacroDeckHost.Infrastructure.OptionsSources;

public sealed class CalendarAccountsOptionsSource : IHostOptionsSource
{
	private readonly ICalendarRegistry _registry;
	private readonly IServiceScopeFactory _scopeFactory;
	private readonly ILocalizationResolver _localization;

	public CalendarAccountsOptionsSource(
		ICalendarRegistry registry,
		IServiceScopeFactory scopeFactory,
		ILocalizationResolver localization)
	{
		_registry = registry;
		_scopeFactory = scopeFactory;
		_localization = localization;
	}

	public string Id => CalendarOptionsSourceIds.Accounts;

	public async Task<DynamicOptionsResult> GetOptionsAsync(string? filter, CancellationToken cancellationToken)
	{
		var culture = await ActiveLocalization.Culture(_scopeFactory);
		var accounts = _registry.GetAccounts();

		var qualifyWithProvider = accounts
			.Select(account => _localization.Resolve(account.ProviderName, culture))
			.Distinct(StringComparer.Ordinal)
			.Count() > 1;

		return CalendarOptions.Result(accounts.Select(account => new ActionParameterOption
			{
				Value = account.AccountId,
				Label = qualifyWithProvider
					? _localization.Resolve(Strings.Config.AccountProvider(account: account.DisplayName,
						provider: account.ProviderName), culture) ?? account.DisplayName
					: account.DisplayName
			}),
			filter);
	}
}

public sealed class CalendarCalendarsOptionsSource : IHostOptionsSource
{
	private readonly ICalendarEventCache _cache;
	private readonly IServiceScopeFactory _scopeFactory;
	private readonly ILocalizationResolver _localization;

	public CalendarCalendarsOptionsSource(
		ICalendarEventCache cache,
		IServiceScopeFactory scopeFactory,
		ILocalizationResolver localization)
	{
		_cache = cache;
		_scopeFactory = scopeFactory;
		_localization = localization;
	}

	public string Id => CalendarOptionsSourceIds.Calendars;

	public async Task<DynamicOptionsResult> GetOptionsAsync(string? filter, CancellationToken cancellationToken)
	{
		var accounts = _cache.Snapshot.Accounts;
		var qualifyWithAccount = accounts.Count > 1;
		var culture = await ActiveLocalization.Culture(_scopeFactory);

		return CalendarOptions.Result(accounts.SelectMany(state => state.Calendars.Select(calendar =>
				new ActionParameterOption
				{
					Value = calendar.Key,
					Label = qualifyWithAccount
						? _localization.Resolve(Strings.Details.Source(calendar: calendar.Name,
							account: state.Account.DisplayName,
							provider: state.Account.ProviderName), culture) ?? calendar.Name
						: calendar.Name
				})),
			filter);
	}
}

internal static class CalendarOptions
{
	public static DynamicOptionsResult Result(IEnumerable<ActionParameterOption> options, string? filter)
		=> new()
		{
			Options = options
				.Where(option => filter is null ||
					(option.Label.Literal ?? string.Empty).Contains(filter, StringComparison.OrdinalIgnoreCase))
				.OrderBy(option => option.Label.Literal, StringComparer.OrdinalIgnoreCase)
				.ToList(),
			AllowsCustomValue = true,
			CacheSeconds = 5
		};
}
