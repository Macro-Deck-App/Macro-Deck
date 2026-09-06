using MacroDeck.Sdk;
using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.Integrations.ConfigFlow;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Integrations;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Variables;

namespace MacroDeckHost.Application.Ui.Handlers;

public class GetIntegrationsRequestMessageHandler
	: IUiTransportMessageHandler<GetIntegrationsRequest, GetIntegrationsResponse>
{
	private readonly IIntegrationRegistry _integrationRegistry;
	private readonly IIntegrationConfigStore _configStore;
	private readonly IIntegrationIssueService _issueService;
	private readonly IIntegrationConfigMutationCoordinator? _mutations;

	public GetIntegrationsRequestMessageHandler(
		IIntegrationRegistry integrationRegistry,
		IIntegrationConfigStore configStore,
		IIntegrationIssueService issueService,
		IIntegrationConfigMutationCoordinator? mutations = null)
	{
		_integrationRegistry = integrationRegistry;
		_configStore = configStore;
		_issueService = issueService;
		_mutations = mutations;
	}

	public async ValueTask<GetIntegrationsResponse> Handle(
		GetIntegrationsRequest request,
		CancellationToken cancellationToken)
	{
		var response = new GetIntegrationsResponse();

		foreach (var integration in _integrationRegistry.Integrations)
		{
			if (integration is ISystemIntegration)
			{
				continue;
			}

			var configProvider = integration as IConfigFlowProvider;
			var supportsConfigFlow = configProvider is not null;
			var variableProvider = integration as IVariableProvider;
			var issues = await _issueService.GetIssuesAsync(integration.Id, cancellationToken);
			var configuredEntryCount = !supportsConfigFlow
				? 0
				: _mutations is null
					? (await _configStore.List(integration.Id)).Count
					: (await _mutations.DescribeAsync(integration.Id, cancellationToken)).Count(entry => entry.Usable);

			response.Integrations.Add(new Integration
			{
				Id = integration.Id,
				Name = integration.Name,
				Version = integration.Version,
				IsInternal = _integrationRegistry.GetOrigin(integration.Id) == IntegrationOrigin.BuiltIn,
				Enabled = _integrationRegistry.IsEnabled(integration.Id),
				IsInitialized = integration.IsInitialized,
				ActionCount = integration.Actions.Count(a => a.RunsHere()),
				VariableCount = variableProvider?.DeclaredVariables.Count ?? 0,
				VariablesDependOnConfiguration = variableProvider?.VariablesDependOnConfiguration ?? false,
				SupportsConfigFlow = supportsConfigFlow,
				AllowsMultipleConfigurations = configProvider?.AllowsMultipleConfigurations ?? true,
				ConfiguredEntryCount = configuredEntryCount,
				HasIcon = integration is IIntegrationIconProvider,
				IconVersion = IntegrationIconVersion.For(integration),
				IssueCount = issues.Count,
				IssueSeverity = IntegrationIssueDto.MaxSeverityName(issues),
				ProvidedCapabilities = ProvidedCapabilityCatalog.For(integration)
					.Select(capability => new ProvidedCapabilityDto { Kind = capability.Kind, Name = capability.Name })
					.ToList()
			});
		}

		return response;
	}
}
