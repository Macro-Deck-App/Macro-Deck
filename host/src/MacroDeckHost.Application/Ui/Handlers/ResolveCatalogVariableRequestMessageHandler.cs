using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Variables;
using MacroDeckHost.Application.Variables;

namespace MacroDeckHost.Application.Ui.Handlers;

public class ResolveCatalogVariableRequestMessageHandler
	: IUiTransportMessageHandler<ResolveCatalogVariableRequest, ResolveCatalogVariableResponse>
{
	private readonly VariableCatalogProviders _providers;
	private readonly VariableRegistry _registry;

	public ResolveCatalogVariableRequestMessageHandler(VariableCatalogProviders providers, VariableRegistry registry)
	{
		_providers = providers;
		_registry = registry;
	}

	public async ValueTask<ResolveCatalogVariableResponse> Handle(
		ResolveCatalogVariableRequest request,
		CancellationToken cancellationToken)
	{
		var provider = _providers.Resolve(request.IntegrationId);
		if (provider is null)
		{
			return new ResolveCatalogVariableResponse
			{
				Error = VariableBindingErrorMapper.ToTransportError(VariableBindingError.ProviderUnavailable),
			};
		}

		var definition = await provider.ResolveAsync(request.ResourceId, cancellationToken).ConfigureAwait(false);
		if (definition is null)
		{
			return new ResolveCatalogVariableResponse
			{
				Error = VariableBindingErrorMapper.ToTransportError(VariableBindingError.Unresolvable),
			};
		}

		return new ResolveCatalogVariableResponse
		{
			Node = DiscoverCatalogVariablesRequestMessageHandler.ToDto(definition, request.IntegrationId, _registry),
		};
	}
}
