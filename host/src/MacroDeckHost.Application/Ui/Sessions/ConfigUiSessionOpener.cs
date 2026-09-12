using System.Text.Json;
using MacroDeck.Sdk.Actions;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.FolderViews;
using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.Integrations.ConfigFlow;
using MacroDeckHost.Application.Plugins.Capabilities.Adapters;
using MacroDeckHost.Application.Plugins.Capabilities.Adapters.ConfigFlow;
using MacroDeckHost.Application.Ui.Sessions.InProcess;
using MacroDeckHost.Application.Ui.Transport.Messages.UiSessions;
using MacroDeckHost.Application.Widgets;
using MacroDeckHost.Domain.Entities;

namespace MacroDeckHost.Application.Ui.Sessions;

public interface IConfigUiSessionOpener
{
	UiSessionOpenTicket Open(OpenConfigUiSessionRequest request, string ownerPrincipal);
}

/// <summary>
/// Resolves which provider id a config entry point's UI session belongs to, and opens it through
/// <see cref="IUiSessionBroker" />. Kept out of the UI transport dispatcher so it stays a one-line
/// delegation like its siblings - see ADR 0062, which this respects the same way <c>UiSessionBroker.Open</c>
/// does: nothing here awaits a provider.
/// </summary>
public sealed class ConfigUiSessionOpener : IConfigUiSessionOpener
{
	private static readonly JsonElement _emptyObject = JsonDocument.Parse("{}").RootElement;

	private readonly IIntegrationRegistry _integrations;
	private readonly IConfigFlowManager _configFlows;
	private readonly IFolderViewRegistry _folderViews;
	private readonly IFolderCache _folderCache;
	private readonly IWidgetTypeRegistry _widgetTypes;
	private readonly IUiSessionBroker _broker;

	public ConfigUiSessionOpener(IIntegrationRegistry integrations,
		IConfigFlowManager configFlows,
		IFolderViewRegistry folderViews,
		IFolderCache folderCache,
		IWidgetTypeRegistry widgetTypes,
		IUiSessionBroker broker)
	{
		_integrations = integrations;
		_configFlows = configFlows;
		_folderViews = folderViews;
		_folderCache = folderCache;
		_widgetTypes = widgetTypes;
		_broker = broker;
	}

	public UiSessionOpenTicket Open(OpenConfigUiSessionRequest request, string ownerPrincipal)
	{
		ArgumentNullException.ThrowIfNull(request);

		// A widget's configuration surface carries only what a widget provider needs (ADR 0050: the tree
		// renders a transaction it does not own) - entryPoint/widgetId/widgetType/widgetData, never the
		// integrationId every other entry point's surface carries, so this bypasses the shared attribute
		// builder below rather than growing another conditional branch inside it.
		if (string.Equals(request.EntryPoint, UiConfigEntryPoints.WidgetConfig, StringComparison.Ordinal))
		{
			return OpenWidgetConfig(request, ownerPrincipal);
		}

		if (!TryResolveProviderId(request,
			out var providerId,
			out var flowSessionId,
			out var action,
			out var rejection))
		{
			return UiSessionOpenTicket.Rejected(rejection!.Code, rejection.Message);
		}

		var surface = new UiSurface
		{
			Kind = UiSurfaceKinds.Config,
			SessionMode = UiSessionModes.Exclusive,
			Attributes = BuildAttributes(request, flowSessionId, action)
		};

		return _broker.Open(providerId, surface, ownerPrincipal);
	}

	private UiSessionOpenTicket OpenWidgetConfig(OpenConfigUiSessionRequest request, string ownerPrincipal)
	{
		if (!Guid.TryParse(request.WidgetId, out var widgetId) || FindWidget(widgetId) is not { } widget)
		{
			return UiSessionOpenTicket.Rejected(Rejection.NoSuchWidget.Code, Rejection.NoSuchWidget.Message);
		}

		// A type a provider owns configures itself, so the session opens under the provider's own id for
		// the reason WidgetUiSessionOpener gives: the broker's ownership check compares against the real
		// owner, and a synthetic id would fail it on the first patch. What the session is for is already
		// on the surface, which is where a provider reads it from.
		var isPluginType = _widgetTypes.TryResolve(widget.Type, out var entry) && !entry.IsBuiltIn;

		if (isPluginType && !entry!.Descriptor.HasConfiguration)
		{
			return UiSessionOpenTicket.Rejected(Rejection.WidgetTypeHasNoConfiguration.Code,
				Rejection.WidgetTypeHasNoConfiguration.Message);
		}

		var providerId = isPluginType
			? entry!.ProviderId
			: WidgetUiProviderRegistry.ConfigProviderIdFor(widgetId, ownerPrincipal);

		var surface = new UiSurface
		{
			Kind = UiSurfaceKinds.Config,
			SessionMode = UiSessionModes.Exclusive,
			Attributes = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
			{
				[UiConfigSurfaceAttributes.EntryPoint] = JsonSerializer.SerializeToElement(request.EntryPoint),
				[UiConfigSurfaceAttributes.WidgetId] = JsonSerializer.SerializeToElement(widgetId.ToString()),
				[UiConfigSurfaceAttributes.WidgetType] = JsonSerializer.SerializeToElement(widget.Type),
				// The editor's own draft wins over the stored record, the way a folder view's configuration
				// does: the surface renders a transaction the tree does not own, and the draft is what is
				// being edited. Absent or unparseable falls back to what the widget is saved with, so a
				// client that sends nothing keeps getting exactly the tree it got before.
				[UiConfigSurfaceAttributes.WidgetData]
					= TryParseObject(request.WidgetData) ?? ParseConfiguration(widget.Data),
				[UiConfigSurfaceAttributes.WidgetWidth] = JsonSerializer.SerializeToElement(widget.Width),
				[UiConfigSurfaceAttributes.WidgetHeight] = JsonSerializer.SerializeToElement(widget.Height)
			}
		};

		return _broker.Open(providerId, surface, ownerPrincipal);
	}

	private WidgetEntity? FindWidget(Guid widgetId)
		=> _folderCache.GetAllFolders().SelectMany(folder => folder.Widgets).FirstOrDefault(w => w.Id == widgetId);

	private bool TryResolveProviderId(
		OpenConfigUiSessionRequest request,
		out string providerId,
		out string? flowSessionId,
		out IActionDefinition? action,
		out Rejection? rejection)
	{
		providerId = string.Empty;
		flowSessionId = null;
		action = null;
		rejection = null;

		if (string.Equals(request.EntryPoint, UiConfigEntryPoints.IntegrationConfig, StringComparison.Ordinal))
		{
			return TryResolveIntegrationConfig(request, out providerId, out flowSessionId, out rejection);
		}

		if (string.Equals(request.EntryPoint, UiConfigEntryPoints.ActionConfig, StringComparison.Ordinal))
		{
			return TryResolveActionConfig(request, out providerId, out action, out rejection);
		}

		if (string.Equals(request.EntryPoint, UiConfigEntryPoints.FolderViewConfig, StringComparison.Ordinal))
		{
			return TryResolveFolderViewConfig(request, out providerId, out rejection);
		}

		rejection = Rejection.UnknownEntryPoint;
		return false;
	}

	private bool TryResolveIntegrationConfig(
		OpenConfigUiSessionRequest request,
		out string providerId,
		out string? flowSessionId,
		out Rejection? rejection)
	{
		providerId = string.Empty;
		flowSessionId = null;

		if (!Guid.TryParse(request.FlowId, out var flowId) || !_configFlows.TryGetActiveFlow(flowId, out var active))
		{
			rejection = Rejection.NoSuchFlow;
			return false;
		}

		var integration = _integrations.Integrations
			.FirstOrDefault(candidate => string.Equals(candidate.Id, active!.IntegrationId, StringComparison.Ordinal));

		if (integration is RemotePluginIntegration)
		{
			// The plugin already knows this flow by the session id RemoteConfigFlow minted for
			// flow.start - reused here rather than minted a second time, so the plugin-side lookup in
			// UiCapabilityHandler finds the same flow instance.
			if (active!.Flow is not RemoteConfigFlow remote)
			{
				rejection = Rejection.NoSuchFlow;
				return false;
			}

			providerId = active.IntegrationId;
			flowSessionId = remote.SessionId;
			rejection = null;
			return true;
		}

		providerId = ConfigFlowUiProviderRegistry.ProviderIdFor(flowId);
		rejection = null;
		return true;
	}

	private bool TryResolveActionConfig(
		OpenConfigUiSessionRequest request,
		out string providerId,
		out IActionDefinition? action,
		out Rejection? rejection)
	{
		providerId = string.Empty;
		action = string.IsNullOrEmpty(request.ActionId)
			? null
			: _integrations.FindAction(request.IntegrationId, request.ActionId);

		if (action is null)
		{
			rejection = Rejection.NoSuchAction;
			return false;
		}

		var integration = _integrations.Integrations
			.FirstOrDefault(candidate => string.Equals(candidate.Id, request.IntegrationId, StringComparison.Ordinal));

		providerId = integration is RemotePluginIntegration
			? request.IntegrationId
			: ActionConfigUiProviderRegistry.ProviderIdFor(request.IntegrationId, request.ActionId!);

		rejection = null;
		return true;
	}

	// The provider is the integration that registered the view, and it serves the configuration surface
	// through the same IUiProvider that serves the view itself - so unlike an action config there is no
	// synthetic per-provider id to resolve here.
	private bool TryResolveFolderViewConfig(
		OpenConfigUiSessionRequest request,
		out string providerId,
		out Rejection? rejection)
	{
		providerId = string.Empty;

		if (string.IsNullOrEmpty(request.FolderViewId) ||
			!_folderViews.TryResolve(request.FolderViewId, out var entry))
		{
			rejection = Rejection.NoSuchFolderView;
			return false;
		}

		if (!entry.Descriptor.HasConfiguration)
		{
			rejection = Rejection.FolderViewHasNoConfiguration;
			return false;
		}

		providerId = entry.ProviderId;
		rejection = null;
		return true;
	}

	private static Dictionary<string, JsonElement> BuildAttributes(
		OpenConfigUiSessionRequest request,
		string? flowSessionId,
		IActionDefinition? action)
	{
		var attributes = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
		{
			[UiConfigSurfaceAttributes.EntryPoint] = JsonSerializer.SerializeToElement(request.EntryPoint),
			[UiConfigSurfaceAttributes.IntegrationId] = JsonSerializer.SerializeToElement(request.IntegrationId)
		};

		if (flowSessionId is not null)
		{
			attributes[UiConfigSurfaceAttributes.ConfigFlowSessionId]
				= JsonSerializer.SerializeToElement(flowSessionId);
		}

		if (string.Equals(request.EntryPoint, UiConfigEntryPoints.ActionConfig, StringComparison.Ordinal) &&
			action is not null)
		{
			attributes[UiConfigSurfaceAttributes.ActionId]
				= JsonSerializer.SerializeToElement(request.ActionId ?? string.Empty);

			// A card expand is a UI interaction, not an explicit intent to reveal a stored secret - so a
			// Secret/Password-typed parameter never reaches this attribute at its real value, whether the
			// target is an in-process action or one behind a plugin socket.
			var maskedParameters = ActionParameterSecretMasking.Mask(action.Parameters, request.Parameters);
			attributes[UiConfigSurfaceAttributes.Parameters] = JsonSerializer.SerializeToElement(maskedParameters);
		}

		if (string.Equals(request.EntryPoint, UiConfigEntryPoints.FolderViewConfig, StringComparison.Ordinal))
		{
			attributes[UiConfigSurfaceAttributes.FolderId] =
				JsonSerializer.SerializeToElement(request.FolderId ?? string.Empty);
			attributes[UiConfigSurfaceAttributes.FolderViewId] =
				JsonSerializer.SerializeToElement(request.FolderViewId ?? string.Empty);
			attributes[UiConfigSurfaceAttributes.FolderViewConfiguration] =
				ParseConfiguration(request.FolderViewConfiguration);
		}

		return attributes;
	}

	private sealed record Rejection(string Code, string Message)
	{
		public static readonly Rejection UnknownEntryPoint =
			new(UiSessionErrorCodes.ProviderUnavailable, "That config entry point is not recognised.");

		public static readonly Rejection NoSuchFlow =
			new(UiSessionErrorCodes.ProviderUnavailable, "That config flow session does not exist.");

		public static readonly Rejection NoSuchAction =
			new(UiSessionErrorCodes.ProviderUnavailable, "That action does not exist in that integration.");

		public static readonly Rejection NoSuchFolderView =
			new(UiSessionErrorCodes.ProviderUnavailable, "That folder view is not available.");

		public static readonly Rejection FolderViewHasNoConfiguration =
			new(UiSessionErrorCodes.ProviderRejected, "That folder view has nothing to configure.");

		public static readonly Rejection NoSuchWidget =
			new(UiSessionErrorCodes.ProviderUnavailable, "That widget does not exist.");

		public static readonly Rejection WidgetTypeHasNoConfiguration =
			new(UiSessionErrorCodes.ProviderRejected, "That widget type has nothing to configure.");
	}

	/// <summary>The JSON object <paramref name="json" /> holds, or <c>null</c> when it holds none - as
	/// distinct from <see cref="ParseConfiguration" />, whose empty object is itself a usable answer.</summary>
	private static JsonElement? TryParseObject(string? json)
	{
		if (string.IsNullOrWhiteSpace(json))
		{
			return null;
		}

		try
		{
			using var document = JsonDocument.Parse(json);
			return document.RootElement.ValueKind == JsonValueKind.Object ? document.RootElement.Clone() : null;
		}
		catch (JsonException)
		{
			return null;
		}
	}

	private static JsonElement ParseConfiguration(string? json)
	{
		if (string.IsNullOrWhiteSpace(json))
		{
			return _emptyObject;
		}

		try
		{
			using var document = JsonDocument.Parse(json);
			return document.RootElement.ValueKind == JsonValueKind.Object
				? document.RootElement.Clone()
				: _emptyObject;
		}
		catch (JsonException)
		{
			return _emptyObject;
		}
	}
}
