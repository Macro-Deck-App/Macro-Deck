using System.Text.Json;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Model.Resources;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Runtime;
using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.HostLocking;
using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.Rendering;
using MacroDeckHost.Application.Ui.Resources;
using MacroDeckHost.Application.Ui.Sessions.InProcess;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Widgets;
using MacroDeckHost.Application.Widgets;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Widgets;
using MacroDeckHost.Widgets.Configuration;
using MacroDeckHost.Widgets.Preview;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Widgets.ActionButton;

public sealed class ActionButtonWidgetUiProvider : IBuiltInWidgetUiProvider
{
	// The owner id every image resource decoded from a legacy imageUrl data URI is registered under.
	private const string ImageResourceOwnerId = "app.macro-deck.widget-action-button-image";

	private readonly IFolderCache _folderCache;
	private readonly IWidgetIconResources _iconResources;
	private readonly IUiResourceStore _resourceStore;
	private readonly IWidgetTriggerService _triggerService;
	private readonly IHostLockState _lockState;
	private readonly WidgetStateSubscriptionTracker _stateSubscriptions;
	private readonly LabelSubscriptionTracker _labelSubscriptions;
	private readonly IWidgetRenderSignals _renderSignals;
	private readonly IUiTransport _uiTransport;
	private readonly IWidgetSampleTextResolver _sampleText;
	private readonly IServiceScopeFactory _scopeFactory;
	private readonly IIntegrationRegistry _integrations;
	private readonly IFontCatalog _fonts;

	public ActionButtonWidgetUiProvider(
		IFolderCache folderCache,
		IWidgetIconResources iconResources,
		IUiResourceStore resourceStore,
		IWidgetTriggerService triggerService,
		IHostLockState lockState,
		WidgetStateSubscriptionTracker stateSubscriptions,
		LabelSubscriptionTracker labelSubscriptions,
		IWidgetRenderSignals renderSignals,
		IUiTransport uiTransport,
		IWidgetSampleTextResolver sampleText,
		IServiceScopeFactory scopeFactory,
		IIntegrationRegistry integrations,
		IFontCatalog fonts)
	{
		_folderCache = folderCache;
		_iconResources = iconResources;
		_resourceStore = resourceStore;
		_triggerService = triggerService;
		_lockState = lockState;
		_stateSubscriptions = stateSubscriptions;
		_labelSubscriptions = labelSubscriptions;
		_renderSignals = renderSignals;
		_uiTransport = uiTransport;
		_sampleText = sampleText;
		_scopeFactory = scopeFactory;
		_integrations = integrations;
		_fonts = fonts;
	}

	public string WidgetTypeId => WidgetTypeIds.ActionButton;

	public IReadOnlyList<UiSurfaceDeclaration> Surfaces { get; } =
	[
		new()
			{ Kind = UiSurfaceKinds.Widget, SessionMode = UiSessionModes.Shared },
		new()
			{ Kind = UiSurfaceKinds.Preview, SessionMode = UiSessionModes.Shared },
		new()
			{ Kind = UiSurfaceKinds.Config, SessionMode = UiSessionModes.Exclusive },
	];

	public async Task<IUiSession?> CreateSessionAsync(UiSessionRequest request, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);

		if (request.Surface.Kind == UiSurfaceKinds.Config)
		{
			if (!WidgetConfigSurfaces.IsFor(request.Surface, WidgetTypeId))
			{
				return null;
			}

			var configData = WidgetConfigSurfaces.Data(request.Surface);
			var liveState
				= await ResolveLiveStateAsync(WidgetConfigSurfaces.WidgetId(request.Surface), cancellationToken)
					.ConfigureAwait(false);
			var configView = new UiView(request.Surface,
				ActionButtonWidgetConfigView.Build(configData,
					WidgetConfigSurfaces.AspectRatio(request.Surface),
					_integrations,
					_fonts,
					liveState));

			return new WidgetConfigSession(configView);
		}

		if (request.Surface.Kind is not (UiSurfaceKinds.Widget or UiSurfaceKinds.Preview))
		{
			return null;
		}

		var data = WidgetSamplePreview.IsRequested(request.Surface)
			? await ActionButtonWidgetSample.BuildDataAsync(_sampleText).ConfigureAwait(false)
			: DataElement(request.Surface);

		var config = ActionButtonWidgetData.Parse(data);

		var iconResources = new Dictionary<WidgetIconReference, UiResource>();

		foreach (var reference in config.AllIconReferences())
		{
			var resolved = await _iconResources.ResolveAsync(reference, cancellationToken).ConfigureAwait(false);

			if (resolved is not null)
			{
				iconResources[reference] = resolved;
			}
		}

		var iconResourcesState = new UiState<IReadOnlyDictionary<WidgetIconReference, UiResource>>(iconResources);

		var imageResource = ActionButtonImageResource.TryParse(config.ImageUrl, out var content, out var mediaType)
			? _resourceStore.Register(new UiResourceRegistration
			{
				OwnerId = ImageResourceOwnerId,
				Name = ImageResourceName(request.Surface),
				MediaType = mediaType,
				Content = content,
			})
			: null;

		var interactive = request.Surface.Kind == UiSurfaceKinds.Widget;
		var widget = interactive ? FindWidget(request.Surface) : _previewWidgetPlaceholder;
		var variableScopeWidgetId = interactive ? null : VariableScopeWidgetId(request.Surface);

		if (widget is null)
		{
			return null;
		}

		// The Preview surface has no stored widget (widget.Id is Guid.Empty for it - see
		// _previewWidgetPlaceholder), so this always resolves to Inactive there: the preview keeps
		// showing the configured icon, matching IWidgetStateService.Resolve's own established blind spot
		// for the same surface.
		var iconProviderState
			= new UiState<WidgetIconResolution>(await ResolveInitialIconProviderAsync(widget.Id, cancellationToken)
				.ConfigureAwait(false));

		// The state the button is actually in, not the one its data last stored: a mapping or a provider
		// decides that, and the stored activeStateId it falls back to is only ever meaningful while
		// neither governs. Without this a freshly opened session paints the stored face until the next
		// transition happens to push a new one - so a mapped button reopened after its variable moved
		// (or a deck simply navigated back to) shows the wrong state indefinitely.
		var resolvedState = interactive
			? await ResolveLiveStateAsync(widget.Id, cancellationToken).ConfigureAwait(false)
			: null;
		var activeState = new UiState<string?>(resolvedState?.Id ?? config.InitialStateId);
		var configState = new UiState<ActionButtonWidgetData>(config);
		var initialLabel
			= await ResolveInitialLabelAsync(config,
					activeState.Peek(),
					interactive,
					widget,
					variableScopeWidgetId,
					cancellationToken)
				.ConfigureAwait(false);
		var labelText = new UiState<string?>(initialLabel);

		var session = new ActionButtonWidgetSession(configState,
			activeState,
			labelText,
			iconResourcesState,
			iconProviderState,
			widget,
			_folderCache,
			_triggerService,
			_lockState,
			_iconResources,
			_scopeFactory,
			_stateSubscriptions,
			_labelSubscriptions,
			_renderSignals,
			_uiTransport,
			interactive,
			variableScopeWidgetId);

		var element = ActionButtonWidgetView.Build(configState,
			activeState,
			labelText,
			iconResourcesState,
			iconProviderState,
			imageResource,
			session.BuildEvents(),
			WidgetSafeArea.RadiusOf(request.Surface));
		var view = new UiView(request.Surface, element);

		session.Attach(view);

		return session;
	}

	/// <summary>Resolves the state the widget is showing right now: the face a Widget-surface session opens
	/// on, and the config editor's "Currently &lt;state&gt;" line (issue #837), from the one source both must
	/// agree with. Null for a widget the config surface names no id for and for one with no state at all
	/// (not an action button, or State Mode disabled). Uses its own scope -
	/// <see cref="IWidgetStateService" /> is scoped, and this provider is a singleton.</summary>
	private async Task<WidgetStateOption?> ResolveLiveStateAsync(Guid? widgetId, CancellationToken cancellationToken)
	{
		if (widgetId is not { } id)
		{
			return null;
		}

		await using var scope = _scopeFactory.CreateAsyncScope();
		var stateService = scope.ServiceProvider.GetRequiredService<IWidgetStateService>();
		var resolution = await stateService.Resolve(id, cancellationToken).ConfigureAwait(false);

		return resolution is null ? null : new WidgetStateOption(resolution.StateId, resolution.StateLabel);
	}

	/// <summary>Resolves what the widget's icon-provider action currently contributes, so a session opens
	/// already showing the right image rather than the configured icon for one frame. Uses its own scope -
	/// <see cref="IWidgetIconService" /> is scoped, and this provider is a singleton.</summary>
	private async Task<WidgetIconResolution> ResolveInitialIconProviderAsync(
		Guid widgetId,
		CancellationToken cancellationToken)
	{
		await using var scope = _scopeFactory.CreateAsyncScope();
		var iconService = scope.ServiceProvider.GetRequiredService<IWidgetIconService>();
		return await iconService.Resolve(widgetId, cancellationToken).ConfigureAwait(false);
	}

	/// <summary>Resolves the label's initial display text host-side, so a Liquid template never reaches the
	/// wire unrendered even in the very first snapshot: a real widget resolves through the same
	/// <see cref="ILabelTextService" /> a legacy client's subscription used; a Preview draft has no stored
	/// data to resolve, so its own raw label goes through the preview path instead - against
	/// <paramref name="variableScopeWidgetId" />'s widget-scoped variables when the surface named one, and
	/// against global variables alone when it did not.</summary>
	private async Task<string?> ResolveInitialLabelAsync(
		ActionButtonWidgetData config,
		string? stateId,
		bool interactive,
		WidgetEntity widget,
		string? variableScopeWidgetId,
		CancellationToken cancellationToken)
	{
		var rawLabel = config.Resolve(stateId).Label;

		await using var scope = _scopeFactory.CreateAsyncScope();
		var labelTextService = scope.ServiceProvider.GetRequiredService<ILabelTextService>();

		return interactive
			? await labelTextService.ResolveText(widget.Id, stateId ?? "off", cancellationToken).ConfigureAwait(false)
			: await labelTextService
				.ResolvePreview(new LabelImagePreviewRequest { Label = rawLabel, ScopeRefId = variableScopeWidgetId },
					cancellationToken)
				.ConfigureAwait(false);
	}

	private static string? VariableScopeWidgetId(UiSurface surface)
		=> surface.Attributes.TryGetValue(UiWidgetSurfaceAttributes.VariableScopeWidgetId, out var element) &&
			element.ValueKind == JsonValueKind.String
				? element.GetString()
				: null;

	// The Preview surface has no stored widget - see UiWidgetSurfaceAttributes.WidgetId - and never
	// dispatches a press (its session is built with interactive: false), so its widget reference is never
	// actually used by IWidgetTriggerService.
	private static readonly WidgetEntity _previewWidgetPlaceholder
		= new() { Id = Guid.Empty, Type = WidgetTypeIds.ActionButton };

	private WidgetEntity? FindWidget(UiSurface surface)
	{
		if (!surface.Attributes.TryGetValue(UiWidgetSurfaceAttributes.WidgetId, out var idElement) ||
			idElement.ValueKind != JsonValueKind.String ||
			!Guid.TryParse(idElement.GetString(), out var widgetId))
		{
			return null;
		}

		foreach (var folder in _folderCache.GetAllFolders())
		{
			var widget = folder.Widgets.FirstOrDefault(w => w.Id == widgetId);

			if (widget is not null)
			{
				return widget;
			}
		}

		return null;
	}

	private static JsonElement DataElement(UiSurface surface)
		=> surface.Attributes.TryGetValue(UiWidgetSurfaceAttributes.Data, out var data) ? data : default;

	private static string ImageResourceName(UiSurface surface)
		=> surface.Attributes.TryGetValue(UiWidgetSurfaceAttributes.WidgetId, out var idElement) &&
			idElement.ValueKind == JsonValueKind.String
				? idElement.GetString() ?? "preview"
				: "preview";
}
