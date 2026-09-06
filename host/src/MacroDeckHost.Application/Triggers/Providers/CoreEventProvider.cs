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
					label: AppStrings.Events.Common.VariableLabel(),
					description: AppStrings.Events.Core.VariableChangedWatchDescription(),
					optionsSourceId: VariableOptionsSourceIds.Variables,
					required: true),
				ActionParameter.Text("value",
					label: AppStrings.Events.Core.ChangedToLabel(),
					description: AppStrings.Events.Core.ChangedToDescription()),
				ActionParameter.Text("previousValue",
					label: AppStrings.Events.Core.ChangedFromLabel(),
					description: AppStrings.Events.Core.ChangedFromDescription())
			],
			PayloadParameters =
			[
				VariableNamePayload(),
				ActionParameter.Text("value", label: AppStrings.Events.Core.NewValueLabel()),
				ActionParameter.Text("previousValue", label: AppStrings.Events.Core.PreviousValueLabel())
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
				ActionParameter.Text("profileName", label: AppStrings.Events.Common.ProfileLabel()),
				ActionParameter.Text("change", label: AppStrings.Events.Core.ChangeLabel())
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
				ActionParameter.Text("folderName", label: AppStrings.Events.Common.FolderLabel()),
				ProfilePayloadId(),
				DevicePayloadId(),
				ActionParameter.Text("clientId", label: AppStrings.Events.Common.ClientLabel())
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
				ActionParameter.Text("integrationName", label: AppStrings.Events.Common.IntegrationLabel())
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
				ActionParameter.Text("integrationName", label: AppStrings.Events.Common.IntegrationLabel())
			]
		},
		new()
		{
			Id = EventIds.ServerStarted,
			Name = AppStrings.Events.Core.ServerStartedName(),
			Description = AppStrings.Events.Core.ServerStartedDescription(),
			Category = AppStrings.Events.Core.ApplicationCategory(),
			PayloadParameters = [ActionParameter.Text("version", label: AppStrings.Events.Common.VersionLabel())]
		},
		new()
		{
			Id = EventIds.ServerStopped,
			Name = AppStrings.Events.Core.ServerStoppedName(),
			Description = AppStrings.Events.Core.ServerStoppedDescription(),
			Category = AppStrings.Events.Core.ApplicationCategory(),
			PayloadParameters = [ActionParameter.Text("version", label: AppStrings.Events.Common.VersionLabel())]
		}
	];

	private static IReadOnlyList<ActionParameter> ClientPresencePayload() =>
	[
		ActionParameter.Text("clientId", label: AppStrings.Events.Common.ClientLabel()),
		DevicePayloadId(),
		ActionParameter.Text("deviceName", label: AppStrings.Events.Common.DeviceLabel())
	];

	// A payload id borrows the option source its matching filter uses, so a condition comparing
	// against it is authored by picking a name while the stored value stays the stable id. Only the
	// type and the source are shared: a filter also carries "leaving this empty means any", which a
	// payload field - always populated by the occurrence - must not inherit.
	private static ActionParameter DevicePayloadId()
		=> ActionParameter.DynamicChoice("deviceId",
			label: AppStrings.Events.Core.DeviceIdLabel(),
			optionsSourceId: DeckOptionsSourceIds.Devices);

	private static ActionParameter FolderPayloadId()
		=> ActionParameter.DynamicChoice("folderId",
			label: AppStrings.Events.Core.FolderIdLabel(),
			optionsSourceId: DeckOptionsSourceIds.Folders);

	private static ActionParameter IntegrationPayloadId()
		=> ActionParameter.DynamicChoice("integrationId",
			label: AppStrings.Events.Core.IntegrationIdLabel(),
			optionsSourceId: DeckOptionsSourceIds.Integrations);

	private static ActionParameter ProfilePayloadId()
		=> ActionParameter.DynamicChoice("profileId",
			label: AppStrings.Events.Core.ProfileIdLabel(),
			optionsSourceId: DeckOptionsSourceIds.Profiles);

	private static ActionParameter VariableNamePayload()
		=> ActionParameter.Autocomplete("variable",
			label: AppStrings.Events.Common.VariableLabel(),
			optionsSourceId: VariableOptionsSourceIds.Variables);

	private static ActionParameter DeviceFilter()
		=> ActionParameter.DynamicChoice("deviceId",
			label: AppStrings.Events.Common.DeviceLabel(),
			description: AppStrings.Events.Common.DeviceFilterDescription(),
			optionsSourceId: DeckOptionsSourceIds.Devices,
			placeholder: AppStrings.Events.Common.AnyDevicePlaceholder());

	private static ActionParameter VariableNameFilter()
		=> ActionParameter.Autocomplete("variable",
			label: AppStrings.Events.Common.VariableLabel(),
			description: AppStrings.Events.Common.VariableFilterDescription(),
			optionsSourceId: VariableOptionsSourceIds.Variables);

	// These all filter on an *id* with a friendly-name pick list rather than on a name typed by hand:
	// a rename must not silently break a trigger, and the user must never see the raw id. Ids are
	// also what a profile archive remaps on import. Each names what leaving it empty does, because the
	// options only load when the list is opened and an empty field otherwise reads as "not configured
	// yet" rather than "no filter".
	private static ActionParameter ProfileFilter()
		=> ActionParameter.DynamicChoice("profileId",
			label: AppStrings.Events.Common.ProfileLabel(),
			description: AppStrings.Events.Common.ProfileFilterDescription(),
			optionsSourceId: DeckOptionsSourceIds.Profiles,
			placeholder: AppStrings.Events.Common.AnyProfilePlaceholder());

	private static ActionParameter FolderFilter()
		=> ActionParameter.DynamicChoice("folderId",
			label: AppStrings.Events.Common.FolderLabel(),
			description: AppStrings.Events.Common.FolderFilterDescription(),
			optionsSourceId: DeckOptionsSourceIds.Folders,
			placeholder: AppStrings.Events.Common.AnyFolderPlaceholder());

	private static ActionParameter IntegrationFilter()
		=> ActionParameter.DynamicChoice("integrationId",
			label: AppStrings.Events.Common.IntegrationLabel(),
			description: AppStrings.Events.Common.IntegrationFilterDescription(),
			optionsSourceId: DeckOptionsSourceIds.Integrations,
			placeholder: AppStrings.Events.Common.AnyIntegrationPlaceholder());
}
