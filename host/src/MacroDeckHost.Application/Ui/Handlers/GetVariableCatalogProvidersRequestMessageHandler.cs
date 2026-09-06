using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Variables;
using MacroDeckHost.Application.Variables;
using MacroDeck.Localization;

namespace MacroDeckHost.Application.Ui.Handlers;

public class GetVariableCatalogProvidersRequestMessageHandler
	: IUiTransportMessageHandler<GetVariableCatalogProvidersRequest, GetVariableCatalogProvidersResponse>
{
	private readonly VariableCatalogProviders _providers;
	private readonly IIntegrationRegistry _integrations;
	private readonly VariableBindingLookup _bindings;
	private readonly StartupReadiness _readiness;

	public GetVariableCatalogProvidersRequestMessageHandler(VariableCatalogProviders providers,
		IIntegrationRegistry integrations,
		VariableBindingLookup bindings,
		StartupReadiness readiness)
	{
		_providers = providers;
		_integrations = integrations;
		_bindings = bindings;
		_readiness = readiness;
	}

	public async ValueTask<GetVariableCatalogProvidersResponse> Handle(
		GetVariableCatalogProvidersRequest request,
		CancellationToken cancellationToken)
	{
		// Clients connect as soon as Kestrel listens; an early read must not observe integrations that
		// have not finished registering yet as "no variable catalogs".
		await _readiness.WhenReady.WaitAsync(cancellationToken);

		var response = new GetVariableCatalogProvidersResponse();
		foreach (var (integrationId, provider) in _providers.GetAvailable())
		{
			var integration = _integrations.Integrations.FirstOrDefault(i =>
				string.Equals(i.Id, integrationId, StringComparison.Ordinal));
			var name = !string.IsNullOrEmpty(provider.CatalogName)
				? LocalizedText.FromLiteral(provider.CatalogName)
				: integration?.Name ?? LocalizedText.FromLiteral(integrationId);

			response.Providers.Add(new VariableCatalogProviderDto
			{
				IntegrationId = integrationId,
				Name = name,
				SupportsSearch = provider.SupportsSearch,
				SupportsManualIds = true,
				// Unbound, not total: what the provider reports is how large its catalog is, and the
				// host is the only side that knows how much of it has been bound already.
				UnboundCount = provider.CatalogEntryCount is { } total
					? Math.Max(0, total - _bindings.CountFor(integrationId))
					: null,
			});
		}

		return response;
	}
}
