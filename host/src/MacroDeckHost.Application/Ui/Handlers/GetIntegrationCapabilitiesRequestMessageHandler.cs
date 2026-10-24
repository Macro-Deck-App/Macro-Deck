using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.Integrations.ConfigFlow;
using MacroDeckHost.Application.Plugins.Capabilities.Adapters.Actions;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Actions;
using MacroDeckHost.Application.Ui.Transport.Messages.Integrations;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Domain.Entities;
using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Variables;

namespace MacroDeckHost.Application.Ui.Handlers;

public class GetIntegrationCapabilitiesRequestMessageHandler
	: IUiTransportMessageHandler<GetIntegrationCapabilitiesRequest, GetIntegrationCapabilitiesResponse>
{
	private readonly IIntegrationRegistry _integrationRegistry;
	private readonly IIntegrationConfigStore _configStore;
	private readonly VariableRegistry _registry;
	private readonly StartupReadiness _readiness;
	private readonly IAppPreferenceService _preferences;
	private readonly ILocalizationResolver _localization;
	private readonly IIntegrationConfigMutationCoordinator? _mutations;
	private readonly IIntegrationCapabilityCounter? _counter;

	public GetIntegrationCapabilitiesRequestMessageHandler(
		IIntegrationRegistry integrationRegistry,
		IIntegrationConfigStore configStore,
		VariableRegistry registry,
		StartupReadiness readiness,
		IAppPreferenceService preferences,
		ILocalizationResolver localization,
		IIntegrationConfigMutationCoordinator? mutations = null,
		IIntegrationCapabilityCounter? counter = null)
	{
		_integrationRegistry = integrationRegistry;
		_configStore = configStore;
		_registry = registry;
		_readiness = readiness;
		_preferences = preferences;
		_localization = localization;
		_mutations = mutations;
		_counter = counter;
	}

	public async ValueTask<GetIntegrationCapabilitiesResponse> Handle(
		GetIntegrationCapabilitiesRequest request,
		CancellationToken cancellationToken)
	{
		var integration = _integrationRegistry.Integrations
			.FirstOrDefault(i => i.Id == request.IntegrationId);

		if (integration is null || integration is ISystemIntegration)
		{
			return new GetIntegrationCapabilitiesResponse { Found = false };
		}

		var enabled = _integrationRegistry.IsEnabled(integration.Id);
		var isInitialized = integration.IsInitialized;
		var configProvider = integration as IConfigFlowProvider;
		var supportsConfigFlow = configProvider is not null;
		var configuredEntryCount = !supportsConfigFlow
			? 0
			: _mutations is null
				? (await _configStore.List(integration.Id)).Count
				: (await _mutations.DescribeAsync(integration.Id, cancellationToken)).Count(entry => entry.Usable);

		var requiresSetup = integration.RequiresConfiguration() && configuredEntryCount == 0;
		var culture = (await _preferences.GetLocalization()).Culture;

		var variableProvider = integration as IVariableProvider;
		var variablesDependOnConfiguration = variableProvider?.VariablesDependOnConfiguration ?? false;

		// Browsing an unconfigured integration must never block on the variable store loading - unlike
		// GetVariablesRequestMessageHandler, which always waits because it answers with live values for
		// variables that are known to exist. Here, an integration that is not enabled or not yet
		// initialized cannot have any runtime variable state to wait for in the first place.
		if (enabled && isInitialized)
		{
			await _readiness.WhenReady.WaitAsync(cancellationToken);
		}

		var response = new GetIntegrationCapabilitiesResponse
		{
			Found = true,
			Enabled = enabled,
			IsInitialized = isInitialized,
			SupportsConfigFlow = supportsConfigFlow,
			RequiresSetup = requiresSetup,
			VariablesDependOnConfiguration = variablesDependOnConfiguration,
			ConfiguredEntryCount = configuredEntryCount
		};

		foreach (var action in integration.Actions)
		{
			if (!action.RunsHere())
			{
				continue;
			}

			response.Actions.Add(BuildActionCapability(action, requiresSetup, enabled, isInitialized, culture));
		}

		if (variableProvider is not null)
		{
			Dictionary<string, VariableEntity>? ownerVariablesByName = null;
			var listed = new HashSet<string>(StringComparer.Ordinal);

			foreach (var declared in variableProvider.DeclaredVariables)
			{
				var name = DeclaredVariableNames.PublishedNameOf(declared);
				if (!listed.Add(name))
				{
					continue;
				}

				response.Variables.Add(BuildVariableCapability(declared,
					name,
					variablesDependOnConfiguration,
					requiresSetup,
					enabled,
					isInitialized,
					() => ownerVariablesByName ??=
						IndexByName(_registry.GetByOwnerIntegration(integration.Id))));
			}
		}

		AddHostPublishedVariables(response, integration.Id, enabled, requiresSetup);

		if (_counter is not null)
		{
			var counts = await _counter.CountAsync(integration, cancellationToken);
			response.Counts =
			[
				.. counts.Select(count => new IntegrationCapabilityCountDto { Kind = count.Key, Count = count.Value })
			];
		}

		return response;
	}

	private IntegrationActionCapability BuildActionCapability(
		IActionDefinition action,
		bool requiresSetup,
		bool enabled,
		bool isInitialized,
		string? culture)
	{
		var (availability, reason) = requiresSetup
			? (CapabilityAvailability.SetupRequired, "Setup required")
			: !enabled
				? (CapabilityAvailability.IntegrationDisabled, "Integration disabled")
				: !isInitialized
					? (CapabilityAvailability.Unavailable, "Unavailable")
					: (CapabilityAvailability.Ready, "Ready");

		return new IntegrationActionCapability
		{
			Id = action.Id,
			Name = action.Name,
			Description = action.Description,
			ParameterCount = action.Parameters.Count,
			ParameterSummary = action.Parameters.Select(parameter => Summarize(parameter, culture)).ToList(),
			IsStateProviderAction = action is IStateProviderActionDefinition,
			// Read as a flag off the descriptor rather than by testing the remote adapter against
			// IIconProviderActionDefinition - a remote adapter never implements it directly, per ADR
			// 0056's closed eight-leaf family. See RemoteIconProviderActionRegistry.
			IsIconProviderAction = action switch
			{
				RemoteActionDefinition remote => remote.ProvidesIcon,
				_ => action is IIconProviderActionDefinition
			},
			Availability = availability,
			AvailabilityReason = reason
		};
	}

	private static Dictionary<string, VariableEntity> IndexByName(IReadOnlyList<VariableEntity> variables)
	{
		var byName = new Dictionary<string, VariableEntity>(StringComparer.Ordinal);
		foreach (var variable in variables)
		{
			byName.TryAdd(variable.Name, variable);
		}

		return byName;
	}

	// A summary line is one composed string, so the label cannot stay a reference and has to be
	// resolved into the reader's language here.
	private string Summarize(ActionParameter parameter, string? culture)
	{
		var mapped = ActionParameterDefMapper.Map(parameter);
		var label = mapped.Label.IsEmpty ? mapped.Name : _localization.Resolve(mapped.Label, culture);
		return $"{label} ({mapped.Type})";
	}

	private void AddHostPublishedVariables(
		GetIntegrationCapabilitiesResponse response,
		string integrationId,
		bool enabled,
		bool requiresSetup)
	{
		var declared = response.Variables.Select(variable => variable.Name).ToHashSet(StringComparer.Ordinal);
		foreach (var entity in HostPublishedIntegrationVariables.Of(_registry, integrationId, declared))
		{
			var (availability, reason) = requiresSetup
				? (CapabilityAvailability.SetupRequired, "Setup required")
				: !enabled
					? (CapabilityAvailability.IntegrationDisabled, "Integration disabled")
					: _registry.IsAvailable(entity.Id)
						? (CapabilityAvailability.Ready, "Ready")
						: (CapabilityAvailability.Unavailable, "Unavailable");
			var available = availability == CapabilityAvailability.Ready;
			response.Variables.Add(new IntegrationVariableCapability
			{
				Name = entity.Name,
				Type = VariableDtoMapper.TypeToWire(entity.Type),
				DecimalPlaces = entity.DecimalPlaces,
				Availability = availability,
				AvailabilityReason = reason,
				Value = available ? entity.Value : null,
				ValueAvailable = available
			});
		}
	}

	private IntegrationVariableCapability BuildVariableCapability(
		VariableDefinition declared,
		string name,
		bool variablesDependOnConfiguration,
		bool requiresSetup,
		bool enabled,
		bool isInitialized,
		Func<Dictionary<string, VariableEntity>> ownerVariablesByName)
	{
		var isTemplate = variablesDependOnConfiguration
			&& name.Contains(VariableNameTemplate.PlaceholderStart, StringComparison.Ordinal);

		CapabilityAvailability availability;
		string reason;
		string? value = null;
		var valueAvailable = false;

		if (isTemplate && requiresSetup)
		{
			availability = CapabilityAvailability.AvailableAfterSetup;
			reason = "Available after setup";
		}
		else if (requiresSetup)
		{
			availability = CapabilityAvailability.SetupRequired;
			reason = "Setup required";
		}
		else if (!enabled)
		{
			availability = CapabilityAvailability.IntegrationDisabled;
			reason = "Integration disabled";
		}
		else if (!isInitialized || isTemplate)
		{
			availability = CapabilityAvailability.Unavailable;
			reason = "Unavailable";
		}
		else
		{
			var entity = ownerVariablesByName().GetValueOrDefault(name);
			if (entity is null || !_registry.IsAvailable(entity.Id))
			{
				availability = CapabilityAvailability.Unavailable;
				reason = "Unavailable";
			}
			else
			{
				availability = CapabilityAvailability.Ready;
				reason = "Ready";
				value = entity.Value;
				valueAvailable = true;
			}
		}

		return new IntegrationVariableCapability
		{
			Name = name,
			Type = VariableDtoMapper.TypeToWire(SdkVariableTypeMapper.ToDomain(declared.Type)),
			DecimalPlaces = declared.DecimalPlaces,
			RefreshIntervalSeconds = declared.RefreshInterval?.TotalSeconds,
			IsTemplate = isTemplate,
			Availability = availability,
			AvailabilityReason = reason,
			Value = value,
			ValueAvailable = valueAvailable
		};
	}
}
