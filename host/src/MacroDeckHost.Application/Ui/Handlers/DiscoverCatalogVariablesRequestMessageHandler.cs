using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Variables;
using MacroDeckHost.Application.Variables;
using MacroDeck.Localization;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Sdk.Identity;
using MacroDeck.Sdk.Variables;

namespace MacroDeckHost.Application.Ui.Handlers;

public class DiscoverCatalogVariablesRequestMessageHandler
	: IUiTransportMessageHandler<DiscoverCatalogVariablesRequest, DiscoverCatalogVariablesResponse>
{
	private readonly VariableCatalogProviders _providers;
	private readonly VariableRegistry _registry;

	public DiscoverCatalogVariablesRequestMessageHandler(VariableCatalogProviders providers,
		VariableRegistry registry)
	{
		_providers = providers;
		_registry = registry;
	}

	public async ValueTask<DiscoverCatalogVariablesResponse> Handle(
		DiscoverCatalogVariablesRequest request,
		CancellationToken cancellationToken)
	{
		var provider = _providers.Resolve(request.IntegrationId);
		if (provider is null)
		{
			// The integration is not registered, not enabled, not initialized, or does not currently
			// declare the capability - never report this as a page with zero resources, which the client
			// cannot distinguish from "this provider genuinely has nothing right now".
			return new DiscoverCatalogVariablesResponse { Available = false };
		}

		// The protocol bound, not a host-local one: the plugin-side handler truncates anything larger while
		// still returning its own continuation token, so a higher host limit silently drops the tail.
		var limit = Math.Clamp(request.Limit ?? ProtocolLimits.MaxVariableCatalogPageSize,
			1,
			ProtocolLimits.MaxVariableCatalogPageSize);

		var query = new VariableCatalogQuery
		{
			ParentId = request.ParentId,
			// Filtering one fetched page out of a set the provider is paging would silently hide most
			// matches, so a provider that cannot search is never offered a search box at all.
			Search = provider.SupportsSearch ? request.Search : null,
			ContinuationToken = request.Cursor,
			PageSize = limit,
		};

		var page = await provider.DiscoverAsync(query, cancellationToken).ConfigureAwait(false);

		var response = new DiscoverCatalogVariablesResponse
		{
			NextCursor = page.ContinuationToken,
			HasMore = page.ContinuationToken is not null,
			Available = true,
		};

		foreach (var definition in page.Items)
		{
			response.Nodes.Add(ToDto(definition, request.IntegrationId, _registry));
		}

		return response;
	}

	internal static VariableCatalogNodeDto ToDto(
		VariableDefinition definition,
		string integrationId,
		VariableRegistry registry)
	{
		var resourceId = definition.Id ?? string.Empty;
		var boundVariableId = QualifiedId.TryCreate(integrationId, resourceId, LocalIdKind.Resource, out var id)
			? registry.FindByDefinition(id)?.Id
			: null;

		// A catalog node's label is runtime data most of the time - an OBS source's title - and authored
		// text only where the provider has one, so the plain name falls back to what identifies the
		// resource rather than to a rendered localization key.
		var label = definition.DisplayName.IsEmpty
			? LocalizedText.FromLiteral(definition.Name ?? resourceId)
			: definition.DisplayName;

		return new VariableCatalogNodeDto
		{
			Id = resourceId,
			Name = label.Literal ?? definition.Name ?? resourceId,
			DisplayName = label,
			Description = definition.Description,
			HasChildren = definition.IsContainer,
			Type = definition.IsBindable
				? VariableDtoMapper.TypeToWire(SdkVariableTypeMapper.ToDomain(definition.Type))
				: null,
			Icon = definition.Icon,
			SuggestedName = definition.Name,
			BoundVariableId = boundVariableId?.ToString(),
			DecimalPlaces = definition.DecimalPlaces,
			CanWrite = definition.CanWrite,
		};
	}
}
