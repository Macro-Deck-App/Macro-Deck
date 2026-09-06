using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.Plugins.Capabilities;
using MacroDeckHost.Application.Plugins.Capabilities.Adapters.Actions;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Actions;
using MacroDeck.Sdk.Actions;
using MacroDeck.Ui.Model.Versioning;

namespace MacroDeckHost.Application.Ui.Handlers;

public class GetActionsRequestMessageHandler
	: IUiTransportMessageHandler<GetActionsRequest, GetActionsResponse>
{
	private readonly IIntegrationRegistry _integrationRegistry;
	private readonly IRemotePluginSnapshotStore _snapshots;

	public GetActionsRequestMessageHandler(IIntegrationRegistry integrationRegistry,
		IRemotePluginSnapshotStore snapshots)
	{
		_integrationRegistry = integrationRegistry;
		_snapshots = snapshots;
	}

	public ValueTask<GetActionsResponse> Handle(
		GetActionsRequest request,
		CancellationToken cancellationToken)
	{
		var response = new GetActionsResponse();

		// The registry pairs each action with the qualified identity the host derives for it. The DTO
		// still carries the two halves separately, because that pair is what stored flow blocks hold.
		foreach (var descriptor in _integrationRegistry.GetActions())
		{
			var action = descriptor.Definition;
			var actionDef = new ActionDefinition
			{
				Id = descriptor.LocalId,
				IntegrationId = descriptor.OwnerId,
				IntegrationName = descriptor.Owner.Name,
				Name = action.Name,
				Description = action.Description
			};

			foreach (var param in action.Parameters)
			{
				actionDef.Parameters.Add(ActionParameterDefMapper.Map(param));
			}

			if (action is IConfigurableActionDefinition { DescriptiveUiSchema: not null } configurable)
			{
				actionDef.DescriptiveUiSchema = configurable.DescriptiveUiSchema;
			}

			if (action is IStateProviderActionDefinition)
			{
				actionDef.IsStateProviderAction = true;
			}

			// Read as a flag off the descriptor rather than by testing the remote adapter against
			// IIconProviderActionDefinition - a remote adapter never implements it directly, per ADR
			// 0056's closed eight-leaf family. See RemoteIconProviderActionRegistry.
			actionDef.IsIconProviderAction = action switch
			{
				RemoteActionDefinition remote => remote.ProvidesIcon,
				_ => action is IIconProviderActionDefinition
			};

			// Read as a flag off the descriptor rather than by testing the remote adapter against
			// IUiConfigurableActionDefinition - it never implements that SDK interface, so the switch
			// below has to branch on the adapter type before it would ever reach that check.
			switch (action)
			{
				case RemoteActionDefinition remote:
					actionDef.SupportsConfigUi = remote.ConfiguresWithUiTree;
					if (actionDef.SupportsConfigUi && _snapshots.Has(descriptor.OwnerId))
					{
						actionDef.ConfigUiModelVersion = _snapshots.GetSnapshot(descriptor.OwnerId).UiModelVersion;
					}

					break;

				case IUiConfigurableActionDefinition:
					actionDef.SupportsConfigUi = true;
					actionDef.ConfigUiModelVersion = UiModelVersions.Current;
					break;
			}

			response.Actions.Add(actionDef);
		}

		return ValueTask.FromResult(response);
	}
}
