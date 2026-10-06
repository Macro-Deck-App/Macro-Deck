using MacroDeck.Sdk;
using MacroDeck.Sdk.Calendar;
using MacroDeck.Sdk.Identity;
using MacroDeckHost.Application.Integrations;
using Serilog;

namespace MacroDeckHost.Application.Calendar;

public sealed record ResolvedCalendarAccount(
	CalendarAccountDescriptor Account,
	ICalendarProvider Provider,
	bool IsPluginProvided);

public interface ICalendarRegistry
{
	IReadOnlyList<CalendarAccountDescriptor> GetAccounts();

	ResolvedCalendarAccount? Resolve(string accountId);

	bool IsCalendarIntegration(string integrationId);
}

public sealed class CalendarRegistry : ICalendarRegistry
{
	private readonly IIntegrationRegistry _integrations;
	private readonly ILogger _logger;

	public CalendarRegistry(IIntegrationRegistry integrations, ILogger logger)
	{
		_integrations = integrations;
		_logger = logger.ForContext<CalendarRegistry>();
	}

	public IReadOnlyList<CalendarAccountDescriptor> GetAccounts()
		=> [.. Enumerate().Select(entry => entry.Account)];

	public ResolvedCalendarAccount? Resolve(string accountId)
		=> Enumerate().FirstOrDefault(entry => entry.Account.AccountId == accountId);

	public bool IsCalendarIntegration(string integrationId)
		=> Enumerate().Any(entry => entry.Account.IntegrationId == integrationId);

	private IEnumerable<ResolvedCalendarAccount> Enumerate()
	{
		var seen = new HashSet<string>(StringComparer.Ordinal);

		foreach (var integration in EnabledProviders())
		{
			var provider = (ICalendarProvider)integration;
			var isPluginProvided = _integrations.GetOrigin(integration.Id) == IntegrationOrigin.Plugin;
			var providerName = ProviderDisplayName.Resolve(provider.ProviderName, integration);

			foreach (var account in provider.GetAccounts())
			{
				if (!QualifiedId.TryCreate(integration.Id, account.Id, out var id))
				{
					_logger.Warning("Integration {IntegrationId} announced an invalid calendar account id {LocalId}; " +
						"skipping it",
						integration.Id,
						account.Id);
					continue;
				}

				var accountId = id.ToString();
				if (!seen.Add(accountId))
				{
					_logger.Warning("Integration {IntegrationId} announced calendar account {LocalId} more than once; " +
						"skipping the duplicate",
						integration.Id,
						account.Id);
					continue;
				}

				yield return new ResolvedCalendarAccount(new CalendarAccountDescriptor(accountId,
						integration.Id,
						account.Id,
						providerName,
						string.IsNullOrWhiteSpace(account.DisplayName) ? account.Id : account.DisplayName),
					provider,
					isPluginProvided);
			}
		}
	}

	private IEnumerable<IIntegration> EnabledProviders() => _integrations.Integrations
		.Where(integration => integration is ICalendarProvider && _integrations.IsEnabled(integration.Id));
}
