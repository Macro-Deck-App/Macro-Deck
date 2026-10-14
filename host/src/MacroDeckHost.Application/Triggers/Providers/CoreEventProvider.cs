using MacroDeckHost.Application.Actions.Options;
using MacroDeckHost.Localization;
using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Events;

namespace MacroDeckHost.Application.Triggers.Providers;

public sealed class CoreEventProvider : IHostEventProvider
{
	public const string ProviderIdValue = "macro-deck";

	public string ProviderId => ProviderIdValue;

	public LocalizedText ProviderName => AppStrings.Events.Core.ProviderName();

	public IReadOnlyList<EventDefinition> EventDefinitions { get; } =
	[
		new()
		{
			Id = EventIds.VariableChanged,
			Name = AppStrings.Events.Core.VariableChangedName(),
			Description = AppStrings.Events.Core.VariableChangedDescription(),
			Category = AppStrings.Events.Core.VariablesCategory(),
			ConfigurationParameters =
			[
				ActionParameter.Autocomplete("variable",
					AppStrings.Events.Common.VariableLabel(),
					AppStrings.Events.Core.VariableChangedWatchDescription(),
					optionsSourceId: VariableOptionsSourceIds.Variables,
					required: true),
				ActionParameter.Text("value",
					AppStrings.Events.Core.ChangedToLabel(),
					AppStrings.Events.Core.ChangedToDescription()),
				ActionParameter.Text("previousValue",
					AppStrings.Events.Core.ChangedFromLabel(),
					AppStrings.Events.Core.ChangedFromDescription())
			],
			PayloadParameters =
			[
				VariableNamePayload(),
				ActionParameter.Text("value", AppStrings.Events.Core.NewValueLabel()),
				ActionParameter.Text("previousValue", AppStrings.Events.Core.PreviousValueLabel())
			]
		},
		new()
		{
			Id = EventIds.VariableCreated,
			Name = AppStrings.Events.Core.VariableCreatedName(),
			Category = AppStrings.Events.Core.VariablesCategory(),
			ConfigurationParameters = [VariableNameFilter()],
			PayloadParameters = [VariableNamePayload()]
		},
		new()
		{
			Id = EventIds.VariableDeleted,
			Name = AppStrings.Events.Core.VariableDeletedName(),
			Category = AppStrings.Events.Core.VariablesCategory(),
			ConfigurationParameters = [VariableNameFilter()],
			PayloadParameters = [VariableNamePayload()]
		},
		new()
		{
			Id = EventIds.ProfileChanged,
			Name = AppStrings.Events.Core.ProfileChangedName(),
			Description = AppStrings.Events.Core.ProfileChangedDescription(),
			Category = AppStrings.Events.Core.DeckCategory(),
			ConfigurationParameters = [ProfileFilter()],
			PayloadParameters =
			[
				ProfilePayloadId(),
				ActionParameter.Text("profileName", AppStrings.Events.Common.ProfileLabel()),
				ActionParameter.Text("change", AppStrings.Events.Core.ChangeLabel())
			]
		},
		new()
		{
			Id = EventIds.FolderChanged,
			Name = AppStrings.Events.Core.FolderChangedName(),
			Description = AppStrings.Events.Core.FolderChangedDescription(),
			Category = AppStrings.Events.Core.DeckCategory(),
			ConfigurationParameters = [FolderFilter()],
			PayloadParameters =
			[
				FolderPayloadId(),
				ActionParameter.Text("folderName", AppStrings.Events.Common.FolderLabel()),
				ProfilePayloadId(),
				DevicePayloadId(),
				ActionParameter.Text("clientId", AppStrings.Events.Common.ClientLabel())
			]
		},
		new()
		{
			Id = EventIds.ClientConnected,
			Name = AppStrings.Events.Core.ClientConnectedName(),
			Description = AppStrings.Events.Core.ClientConnectedDescription(),
			Category = AppStrings.Events.Core.ClientsCategory(),
			ConfigurationParameters = [DeviceFilter()],
			PayloadParameters = ClientPresencePayload()
		},
		new()
		{
			Id = EventIds.ClientDisconnected,
			Name = AppStrings.Events.Core.ClientDisconnectedName(),
			Category = AppStrings.Events.Core.ClientsCategory(),
			ConfigurationParameters = [DeviceFilter()],
			PayloadParameters = ClientPresencePayload()
		},
		new()
		{
			Id = EventIds.IntegrationConnected,
			Name = AppStrings.Events.Core.IntegrationConnectedName(),
			Description = AppStrings.Events.Core.IntegrationConnectedDescription(),
			Category = AppStrings.Events.Core.IntegrationsCategory(),
			ConfigurationParameters = [IntegrationFilter()],
			PayloadParameters =
			[
				IntegrationPayloadId(),
				ActionParameter.Text("integrationName", AppStrings.Events.Common.IntegrationLabel())
			]
		},
		new()
		{
			Id = EventIds.IntegrationDisconnected,
			Name = AppStrings.Events.Core.IntegrationDisconnectedName(),
			Category = AppStrings.Events.Core.IntegrationsCategory(),
			ConfigurationParameters = [IntegrationFilter()],
			PayloadParameters =
			[
				IntegrationPayloadId(),
				ActionParameter.Text("integrationName", AppStrings.Events.Common.IntegrationLabel())
			]
		},
		new()
		{
			Id = EventIds.ServerStarted,
			Name = AppStrings.Events.Core.ServerStartedName(),
			Description = AppStrings.Events.Core.ServerStartedDescription(),
			Category = AppStrings.Events.Core.ApplicationCategory(),
			PayloadParameters = [ActionParameter.Text("version", AppStrings.Events.Common.VersionLabel())]
		},
		new()
		{
			Id = EventIds.ServerStopped,
			Name = AppStrings.Events.Core.ServerStoppedName(),
			Description = AppStrings.Events.Core.ServerStoppedDescription(),
			Category = AppStrings.Events.Core.ApplicationCategory(),
			PayloadParameters = [ActionParameter.Text("version", AppStrings.Events.Common.VersionLabel())]
		}
	];

	private static IReadOnlyList<ActionParameter> ClientPresencePayload()
	{
		return
		[
			ActionParameter.Text("clientId", AppStrings.Events.Common.ClientLabel()),
			DevicePayloadId(),
			ActionParameter.Text("deviceName", AppStrings.Events.Common.DeviceLabel())
		];
	}

	// A payload id borrows the option source its matching filter uses, so a condition comparing
	// against it is authored by picking a name while the stored value stays the stable id. Only the
	// type and the source are shared: a filter also carries "leaving this empty means any", which a
	// payload field - always populated by the occurrence - must not inherit.
	private static ActionParameter DevicePayloadId()
	{
		return ActionParameter.DynamicChoice("deviceId",
			AppStrings.Events.Core.DeviceIdLabel(),
			optionsSourceId: DeckOptionsSourceIds.Devices);
	}

	private static ActionParameter FolderPayloadId()
	{
		return ActionParameter.DynamicChoice("folderId",
			AppStrings.Events.Core.FolderIdLabel(),
			optionsSourceId: DeckOptionsSourceIds.Folders);
	}

	private static ActionParameter IntegrationPayloadId()
	{
		return ActionParameter.DynamicChoice("integrationId",
			AppStrings.Events.Core.IntegrationIdLabel(),
			optionsSourceId: DeckOptionsSourceIds.Integrations);
	}

	private static ActionParameter ProfilePayloadId()
	{
		return ActionParameter.DynamicChoice("profileId",
			AppStrings.Events.Core.ProfileIdLabel(),
			optionsSourceId: DeckOptionsSourceIds.Profiles);
	}

	private static ActionParameter VariableNamePayload()
	{
		return ActionParameter.Autocomplete("variable",
			AppStrings.Events.Common.VariableLabel(),
			optionsSourceId: VariableOptionsSourceIds.Variables);
	}

	private static ActionParameter DeviceFilter()
	{
		return ActionParameter.DynamicChoice("deviceId",
			AppStrings.Events.Common.DeviceLabel(),
			AppStrings.Events.Common.DeviceFilterDescription(),
			DeckOptionsSourceIds.Devices,
			AppStrings.Events.Common.AnyDevicePlaceholder());
	}

	private static ActionParameter VariableNameFilter()
	{
		return ActionParameter.Autocomplete("variable",
			AppStrings.Events.Common.VariableLabel(),
			AppStrings.Events.Common.VariableFilterDescription(),
			optionsSourceId: VariableOptionsSourceIds.Variables);
	}

	// These all filter on an *id* with a friendly-name pick list rather than on a name typed by hand:
	// a rename must not silently break a trigger, and the user must never see the raw id. Ids are
	// also what a profile archive remaps on import. Each names what leaving it empty does, because the
	// options only load when the list is opened and an empty field otherwise reads as "not configured
	// yet" rather than "no filter".
	private static ActionParameter ProfileFilter()
	{
		return ActionParameter.DynamicChoice("profileId",
			AppStrings.Events.Common.ProfileLabel(),
			AppStrings.Events.Common.ProfileFilterDescription(),
			DeckOptionsSourceIds.Profiles,
			AppStrings.Events.Common.AnyProfilePlaceholder());
	}

	private static ActionParameter FolderFilter()
	{
		return ActionParameter.DynamicChoice("folderId",
			AppStrings.Events.Common.FolderLabel(),
			AppStrings.Events.Common.FolderFilterDescription(),
			DeckOptionsSourceIds.Folders,
			AppStrings.Events.Common.AnyFolderPlaceholder());
	}

	private static ActionParameter IntegrationFilter()
	{
		return ActionParameter.DynamicChoice("integrationId",
			AppStrings.Events.Common.IntegrationLabel(),
			AppStrings.Events.Common.IntegrationFilterDescription(),
			DeckOptionsSourceIds.Integrations,
			AppStrings.Events.Common.AnyIntegrationPlaceholder());
	}
}
