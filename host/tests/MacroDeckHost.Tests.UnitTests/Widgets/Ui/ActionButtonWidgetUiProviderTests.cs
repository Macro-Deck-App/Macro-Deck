using System.Text.Json;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Model.Events;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Components;
using MacroDeckHost.Application.Actions;
using MacroDeckHost.Application.HostLocking;
using MacroDeckHost.Application.Rendering;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Resources;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Widgets;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Application.Widgets;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Domain.Widgets;
using MacroDeckHost.Localization;
using MacroDeckHost.Tests.UnitTests.Devices.Surfaces;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeckHost.Tests.UnitTests.Triggers;
using MacroDeckHost.Widgets.ActionButton;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Tests.UnitTests.Widgets.Ui;

/// <summary>
/// Composition-root tests for <see cref="ActionButtonWidgetUiProvider" /> itself, as distinct from
/// <see cref="ActionButtonWidgetSessionTests" />: those construct <see cref="ActionButtonWidgetSession" />
/// directly and pass <c>interactive</c> in as a constructor argument, so they can never catch a provider
/// bug in how <c>interactive</c> is computed from the request's surface kind, or in how the Widget surface
/// resolves <see cref="UiWidgetSurfaceAttributes.WidgetId" /> to a stored widget. These tests go through
/// <see cref="ActionButtonWidgetUiProvider.CreateSessionAsync" /> exactly as the host does.
/// </summary>
[TestFixture]
public class ActionButtonWidgetUiProviderTests
{
	// Two states with no explicit flows - CanAdvanceState is true, so a press is declared and, per
	// IWidgetTriggerService's contract, dispatches "onShortPress" - see ActionButtonWidgetSessionTests'
	// identical constant for the same reasoning.
	private const string TwoStateData =
		"{\"stateMode\":true,\"activeStateId\":\"a\",\"states\":[" +
		"{\"id\":\"a\",\"label\":\"A\",\"appearance\":{\"label\":\"Off\",\"backgroundColor\":\"#111111\"}}," +
		"{\"id\":\"b\",\"label\":\"B\",\"appearance\":{\"label\":\"On\",\"backgroundColor\":\"#222222\"}}]," +
		"\"flows\":\"[]\"}";

	/// <summary>Long enough that a fake, entirely synchronous trigger/transport chain has finished its
	/// registered pending work, short enough that this stays quick. The provider hands back a plain
	/// <see cref="IUiSession" />, with no view or host exposed to await idleness through directly - the
	/// same reason <c>UiSessionFixture.SettleAsync</c> in the broker-level composition-root tests waits
	/// this way instead.</summary>
	private static Task SettleAsync() => Task.Delay(TimeSpan.FromMilliseconds(200));

	[Test]
	public async Task A_state_mode_off_button_serves_a_resolved_label_on_the_widget_surface()
	{
		// The stored shape of a freshly created Action Button: state mode off, no states, a plain root
		// label. The deck tile is served from this session, so an absent label here is a blank button.
		const string singleStateData =
			"{\"label\":\"E2E Action\",\"flows\":\"[]\",\"backgroundColor\":\"var(--color-accent)\",\"stateMode\":false}";
		var widgetId = Guid.NewGuid();
		var folderCache = new StubFolderCache();
		folderCache.AddFolder(new WidgetEntity
			{ Id = widgetId, Type = WidgetTypeIds.ActionButton, Data = singleStateData });

		var provider = Provider(folderCache, new RecordingTriggerService());
		var session = await provider.CreateSessionAsync(Request(UiSurfaceKinds.Widget, widgetId, singleStateData),
			CancellationToken.None);

		Assert.That(session, Is.Not.Null);

		var label = FindById(session!.BuildTree().Root, "actionButton.label")!;
		// The text comes from ILabelTextService, never from the raw stored label, so a Liquid template
		// can never reach the wire unrendered. An absent key here is a button with no label at all.
		Assert.That(label.Properties.ContainsKey("text"), Is.True, "the label node carried no text at all");
		Assert.That(label.Properties["text"].ToString(), Does.Contain("resolved-label"));

		await session.DisposeAsync();
	}

	[Test]
	public async Task A_widget_surface_session_declares_press_events_and_a_press_runs_the_trigger()
	{
		var widgetId = Guid.NewGuid();
		var folderCache = new StubFolderCache();
		folderCache.AddFolder(
			new WidgetEntity { Id = widgetId, Type = WidgetTypeIds.ActionButton, Data = TwoStateData });
		var trigger = new RecordingTriggerService();

		var provider = Provider(folderCache, trigger);
		var session = await provider.CreateSessionAsync(Request(UiSurfaceKinds.Widget, widgetId, TwoStateData),
			CancellationToken.None);

		Assert.That(session, Is.Not.Null);

		var button = FindById(session!.BuildTree().Root, "actionButton")!;
		Assert.That(button.Properties.ContainsKey("events"),
			Is.True,
			"a session opened on the Widget surface must be interactive: it declares press handlers");

		session.Dispatch(new UiEvent { NodeId = "actionButton", Name = UiComponentEvents.Press });
		await SettleAsync();

		Assert.That(trigger.Calls.Single().TriggerType,
			Is.EqualTo(WidgetTriggerTypes.ShortPress),
			"an interactive session must actually run the configured trigger/flow on a press");

		await session.DisposeAsync();
	}

	// A mapping or a provider decides which state a button is in; the stored activeStateId it would
	// otherwise open on is only meaningful while neither governs. A session that opened on the stored id
	// painted the wrong face until some later transition happened to push a new one - so a mapped button
	// whose variable moved while no deck was watching stayed wrong for as long as it was left alone.
	[Test]
	public async Task A_widget_surface_session_opens_on_the_state_the_widget_is_actually_in()
	{
		var widgetId = Guid.NewGuid();
		var folderCache = new StubFolderCache();
		folderCache.AddFolder(
			new WidgetEntity { Id = widgetId, Type = WidgetTypeIds.ActionButton, Data = TwoStateData });

		// TwoStateData stores "a"; the mapping the state service speaks for resolves to "b".
		var stateService = new FakeWidgetStateService();
		stateService.Set(widgetId, "b", "B");

		var provider = Provider(folderCache, new RecordingTriggerService(), stateService: stateService);
		var session = await provider.CreateSessionAsync(Request(UiSurfaceKinds.Widget, widgetId, TwoStateData),
			CancellationToken.None);

		Assert.That(session, Is.Not.Null);

		var button = FindById(session!.BuildTree().Root, "actionButton")!;

		Assert.That(button.Properties["background"].ToString(),
			Is.EqualTo("#222222"),
			"the tree must paint state b's appearance, not the stored state a's");

		await session.DisposeAsync();
	}

	[Test]
	public async Task A_widget_surface_session_falls_back_to_the_stored_state_when_nothing_resolves_one()
	{
		// FakeWidgetStateService answers null for an id it was never Set() for - a button no mapping and
		// no provider governs, whose stored activeStateId is exactly what it should open on.
		var widgetId = Guid.NewGuid();
		var folderCache = new StubFolderCache();
		folderCache.AddFolder(
			new WidgetEntity { Id = widgetId, Type = WidgetTypeIds.ActionButton, Data = TwoStateData });

		var provider = Provider(folderCache, new RecordingTriggerService());
		var session = await provider.CreateSessionAsync(Request(UiSurfaceKinds.Widget, widgetId, TwoStateData),
			CancellationToken.None);

		Assert.That(session, Is.Not.Null);

		var button = FindById(session!.BuildTree().Root, "actionButton")!;

		Assert.That(button.Properties["background"].ToString(), Is.EqualTo("#111111"), "the stored state a");

		await session.DisposeAsync();
	}

	[Test]
	public async Task A_preview_surface_session_declares_no_press_events_and_a_press_never_runs_the_trigger()
	{
		var trigger = new RecordingTriggerService();

		// An empty cache: FindWidget is never called for a Preview session (interactive is false), so
		// this has nothing to find and must not matter - see the ThrowingFolderCache test below for the
		// stronger version of the same claim.
		var provider = Provider(new StubFolderCache(), trigger);
		var session = await provider.CreateSessionAsync(Request(UiSurfaceKinds.Preview, widgetId: null, TwoStateData),
			CancellationToken.None);

		Assert.That(session, Is.Not.Null);

		var button = FindById(session!.BuildTree().Root, "actionButton")!;
		Assert.That(button.Properties.ContainsKey("events"),
			Is.False,
			"a session opened on the Preview surface must declare no press handler at all");

		session.Dispatch(new UiEvent { NodeId = "actionButton", Name = UiComponentEvents.Press });
		await SettleAsync();

		Assert.That(trigger.Calls, Is.Empty, "a Preview session must never run the trigger/flow path");

		await session.DisposeAsync();
	}

	[Test]
	public async Task A_widget_surface_session_resolves_the_specific_widget_named_by_the_surfaces_widget_id()
	{
		var otherWidgetId = Guid.NewGuid();
		var targetWidgetId = Guid.NewGuid();
		var folderCache = new StubFolderCache();
		folderCache.AddFolder(new WidgetEntity
				{ Id = otherWidgetId, Type = WidgetTypeIds.ActionButton, Data = TwoStateData },
			new WidgetEntity { Id = targetWidgetId, Type = WidgetTypeIds.ActionButton, Data = TwoStateData });
		var trigger = new RecordingTriggerService();

		var provider = Provider(folderCache, trigger);
		var session = await provider.CreateSessionAsync(Request(UiSurfaceKinds.Widget, targetWidgetId, TwoStateData),
			CancellationToken.None);

		Assert.That(session, Is.Not.Null);

		session!.Dispatch(new UiEvent { NodeId = "actionButton", Name = UiComponentEvents.Press });
		await SettleAsync();

		Assert.That(trigger.Calls.Single().Widget.Id,
			Is.EqualTo(targetWidgetId),
			"the surface named targetWidgetId, not otherWidgetId - the wrong widget must never receive the trigger");

		await session.DisposeAsync();
	}

	[Test]
	public async Task A_widget_surface_naming_an_id_absent_from_the_folder_cache_yields_no_session()
	{
		var provider = Provider(new StubFolderCache(), new RecordingTriggerService());

		var session = await provider.CreateSessionAsync(Request(UiSurfaceKinds.Widget, Guid.NewGuid(), TwoStateData),
			CancellationToken.None);

		Assert.That(session, Is.Null, "FindWidget found nothing, so CreateSessionAsync must decline the session");
	}

	[Test]
	public async Task A_preview_surface_session_never_queries_the_folder_cache_at_all()
	{
		var provider = Provider(new ThrowingFolderCache(), new RecordingTriggerService());

		// A Preview renders the draft configuration carried on the surface, never a stored widget - so even
		// a surface that happens to name a widget id must not be resolved against the folder cache. Any
		// lookup at all fails this test through ThrowingFolderCache.
		var session = await provider.CreateSessionAsync(Request(UiSurfaceKinds.Preview, Guid.NewGuid(), TwoStateData),
			CancellationToken.None);

		Assert.That(session, Is.Not.Null);

		await session!.DisposeAsync();
	}

	[Test]
	public async Task A_sample_preview_is_labelled_even_though_the_surface_carries_no_configuration()
	{
		// What the widget picker asks for (issue #758): an empty draft, because nothing has been
		// configured at the moment somebody is choosing a widget type. Without the sample it would draw a
		// blank tile, which says nothing about what an Action Button looks like.
		var request = Request(UiSurfaceKinds.Preview, widgetId: null, "{}");
		var attributes = new Dictionary<string, JsonElement>(request.Surface.Attributes, StringComparer.Ordinal)
		{
			[UiWidgetSurfaceAttributes.Sample] = JsonSerializer.SerializeToElement(true),
		};

		var provider = Provider(new StubFolderCache(), new RecordingTriggerService());
		var session = await provider.CreateSessionAsync(new UiSessionRequest
			{
				UiModelVersion = request.UiModelVersion,
				Surface = new UiSurface
				{
					Kind = UiSurfaceKinds.Preview, SessionMode = UiSessionModes.Shared, Attributes = attributes,
				},
			},
			CancellationToken.None);

		Assert.That(session, Is.Not.Null);

		var label = FindById(session!.BuildTree().Root, "actionButton.label");

		Assert.That(label, Is.Not.Null, "the sample tile carried no label node at all");
		Assert.That(label!.Properties.ContainsKey("text"), Is.True, "the sample tile's label carried no text");
		Assert.That(label.Properties["text"].ToString(),
			Does.Contain(TestLocalization.Resolve(AppStrings.Widgets.SamplePreview.ActionButtonLabel(), "en")));

		await session.DisposeAsync();
	}

	// Issue #821: the editor's tile is a draft preview - widgetType plus unsaved data, no stored widget -
	// so before the surface could name a widget to resolve against, a label template rendered against
	// nothing and the reader saw "Test:" where the deck shows "Test: 42".
	private const string TemplateLabelData =
		"{\"label\":\"Test: {{ vars.bla }}\",\"flows\":\"[]\",\"stateMode\":false}";

	[Test]
	public async Task A_preview_scoped_to_a_widget_renders_its_draft_label_from_that_widgets_variables()
	{
		var scopeWidgetId = Guid.NewGuid();
		var variables = new VariableRegistry();
		variables.Upsert(WidgetVariable(scopeWidgetId, "bla", "42"));

		var provider = Provider(new StubFolderCache(), new RecordingTriggerService(), variables: variables);
		var session = await provider.CreateSessionAsync(PreviewRequest(TemplateLabelData, scopeWidgetId.ToString()),
			CancellationToken.None);

		Assert.That(session, Is.Not.Null);
		Assert.That(LabelText(session!), Is.EqualTo("Test: 42"));

		await session.DisposeAsync();
	}

	[Test]
	public async Task A_preview_with_no_scope_still_renders_its_draft_label_without_the_widgets_variables()
	{
		var scopeWidgetId = Guid.NewGuid();
		var variables = new VariableRegistry();
		variables.Upsert(WidgetVariable(scopeWidgetId, "bla", "42"));

		// A widget that has never been saved has no id to scope by, and the widget picker's sample has no
		// widget at all: both must keep opening and rendering exactly as they did.
		var provider = Provider(new StubFolderCache(), new RecordingTriggerService(), variables: variables);
		var session = await provider.CreateSessionAsync(PreviewRequest(TemplateLabelData, variableScopeWidgetId: null),
			CancellationToken.None);

		Assert.That(session, Is.Not.Null);
		Assert.That(LabelText(session!), Is.EqualTo("Test:").Or.EqualTo("Test: "));

		await session.DisposeAsync();
	}

	[Test]
	public async Task A_scoped_preview_repaints_its_label_when_the_scope_widgets_variable_changes()
	{
		var scopeWidgetId = Guid.NewGuid();
		var variables = new VariableRegistry();
		variables.Upsert(WidgetVariable(scopeWidgetId, "bla", "42"));
		var renderSignals = new WidgetRenderSignals();

		var provider = Provider(new StubFolderCache(),
			new RecordingTriggerService(),
			variables: variables,
			renderSignals: renderSignals);
		var session = await provider.CreateSessionAsync(PreviewRequest(TemplateLabelData, scopeWidgetId.ToString()),
			CancellationToken.None);

		Assert.That(session, Is.Not.Null);
		Assert.That(LabelText(session!), Is.EqualTo("Test: 42"));

		variables.Upsert(WidgetVariable(scopeWidgetId, "bla", "43"));
		renderSignals.RaiseVariableChanged();
		await SettleAsync();

		// The deck tile follows a variable while it is open; an editor preview of the same widget has to
		// as well, or it goes stale the moment anything writes the variable.
		Assert.That(LabelText(session!), Is.EqualTo("Test: 43"));

		await session.DisposeAsync();
	}

	// Issue #837: the config editor's "Currently <state>" line names the state the widget is actually
	// showing right now, resolved from IWidgetStateService - the same source a rendered button's own live
	// face comes from - by the widget id the config surface names, not from ActionButtonWidgetConfigTests's
	// own direct calls into ActionButtonWidgetConfigView.Build, which never go through the provider at all.
	[Test]
	public async Task A_config_session_resolves_the_widgets_live_state_by_the_surfaces_own_widget_id()
	{
		var widgetId = Guid.NewGuid();
		var stateService = new FakeWidgetStateService();
		stateService.Set(widgetId, "on", "On");

		var provider = Provider(new StubFolderCache(), new RecordingTriggerService(), stateService: stateService);
		var session = await provider.CreateSessionAsync(ConfigRequest(widgetId, "{\"stateMode\":true}"),
			CancellationToken.None);

		Assert.That(session, Is.Not.Null);

		var live = FindById(session!.BuildTree().Root, "root.properties.live-state");

		Assert.That(live, Is.Not.Null, "a widget with a resolvable live state must reach the config tree");

		await session.DisposeAsync();
	}

	[Test]
	public async Task A_config_session_for_a_widget_with_no_resolvable_state_carries_no_live_state_line()
	{
		// FakeWidgetStateService.Resolve answers null for any id it was never Set() for - matching a real
		// widget State Mode disables reports, or one IWidgetStateService otherwise has nothing to say about.
		var provider = Provider(new StubFolderCache(), new RecordingTriggerService());
		var session = await provider.CreateSessionAsync(ConfigRequest(Guid.NewGuid(), "{\"stateMode\":true}"),
			CancellationToken.None);

		Assert.That(session, Is.Not.Null);
		Assert.That(FindById(session!.BuildTree().Root, "root.properties.live-state"), Is.Null);

		await session.DisposeAsync();
	}

	private static UiSessionRequest ConfigRequest(Guid widgetId, string widgetData)
		=> new()
		{
			UiModelVersion = 1,
			Surface = new UiSurface
			{
				Kind = UiSurfaceKinds.Config,
				SessionMode = UiSessionModes.Exclusive,
				Attributes = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
				{
					[UiConfigSurfaceAttributes.EntryPoint]
						= JsonSerializer.SerializeToElement(UiConfigEntryPoints.WidgetConfig),
					[UiConfigSurfaceAttributes.WidgetId] = JsonSerializer.SerializeToElement(widgetId.ToString()),
					[UiConfigSurfaceAttributes.WidgetType]
						= JsonSerializer.SerializeToElement(WidgetTypeIds.ActionButton),
					[UiConfigSurfaceAttributes.WidgetData] = JsonDocument.Parse(widgetData).RootElement.Clone(),
				},
			},
		};

	private static VariableEntity WidgetVariable(Guid widgetId, string name, string value) => new()
	{
		Id = Guid.NewGuid(),
		Name = name,
		Scope = VariableScope.Widget,
		ScopeRefId = widgetId.ToString(),
		Type = VariableType.Text,
		Classification = VariableClassification.User,
		Value = value,
	};

	private static string? LabelText(IUiSession session)
		=> FindById(session.BuildTree().Root, "actionButton.label") is { } label &&
			label.Properties.TryGetValue("text", out var text)
				? text.ToString()
				: null;

	private static ActionButtonWidgetUiProvider Provider(
		MacroDeckHost.Application.Caching.IFolderCache folderCache,
		IWidgetTriggerService triggerService,
		IHostLockState? lockState = null,
		VariableRegistry? variables = null,
		IWidgetRenderSignals? renderSignals = null,
		FakeWidgetStateService? stateService = null)
	{
		var readiness = new StartupReadiness();
		readiness.MarkCachesReady();
		readiness.MarkVariablesReady();

		var services = new ServiceCollection();
		services.AddSingleton<ILabelTextService>(variables is null
			? new FakeLabelTextService()
			// The real service and renderer, so a scoped preview is proved against actual variable
			// resolution rather than against a fake that merely echoes what it was handed.
			: new LabelTextService(folderCache, new VariableTemplateRenderer(variables), readiness));
		services.AddScoped<IWidgetIconService>(_ => new FakeWidgetIconService());
		services.AddScoped<IWidgetStateService>(_ => stateService ?? new FakeWidgetStateService());
		var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

		return new ActionButtonWidgetUiProvider(folderCache,
			new FakeWidgetIconResources(),
			new UiResourceStore(),
			triggerService,
			lockState ?? new FakeHostLockState(),
			new WidgetStateSubscriptionTracker(),
			new LabelSubscriptionTracker(),
			renderSignals ?? new WidgetRenderSignals(),
			new RecordingTransport(),
			TestLocalization.SampleText,
			scopeFactory,
			new FakeIntegrationRegistry(),
			new FakeFontCatalog());
	}

	private static UiSessionRequest PreviewRequest(string data, string? variableScopeWidgetId)
	{
		var request = Request(UiSurfaceKinds.Preview, widgetId: null, data);

		if (variableScopeWidgetId is null)
		{
			return request;
		}

		var attributes = new Dictionary<string, JsonElement>(request.Surface.Attributes, StringComparer.Ordinal)
		{
			[UiWidgetSurfaceAttributes.VariableScopeWidgetId]
				= JsonSerializer.SerializeToElement(variableScopeWidgetId),
		};

		return new UiSessionRequest
		{
			UiModelVersion = request.UiModelVersion,
			Surface = new UiSurface
			{
				Kind = UiSurfaceKinds.Preview, SessionMode = UiSessionModes.Shared, Attributes = attributes,
			},
		};
	}

	private static UiSessionRequest Request(string surfaceKind, Guid? widgetId, string data)
	{
		var attributes = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
		{
			[UiWidgetSurfaceAttributes.WidgetType] = JsonSerializer.SerializeToElement("ActionButton"),
			[UiWidgetSurfaceAttributes.Data] = JsonDocument.Parse(data).RootElement.Clone(),
		};

		if (widgetId is { } id)
		{
			attributes[UiWidgetSurfaceAttributes.WidgetId] = JsonSerializer.SerializeToElement(id.ToString());
		}

		return new UiSessionRequest
		{
			UiModelVersion = 1,
			Surface = new UiSurface
				{ Kind = surfaceKind, SessionMode = UiSessionModes.Shared, Attributes = attributes },
		};
	}

	private static UiNode? FindById(UiNode node, string id)
	{
		if (string.Equals(node.Id, id, StringComparison.Ordinal))
		{
			return node;
		}

		foreach (var child in node.Children)
		{
			if (FindById(child, id) is { } found)
			{
				return found;
			}
		}

		return node.Fallback is not null ? FindById(node.Fallback, id) : null;
	}

	private sealed class RecordingTriggerService : IWidgetTriggerService
	{
		public List<(WidgetEntity Widget, string TriggerType, string? OriginClientId)> Calls { get; } = [];

		public Task<ActionExecutionDispatch> ExecuteAsync(
			WidgetEntity widget,
			string triggerType,
			string? originClientId,
			Guid? originDeviceId,
			CancellationToken cancellationToken)
		{
			Calls.Add((widget, triggerType, originClientId));

			return Task.FromResult(new ActionExecutionDispatch(Guid.NewGuid(), null));
		}
	}

	private sealed class RecordingTransport : IUiTransport
	{
		public Task Send<T>(T message, CancellationToken cancellationToken = default)
			where T : class
			=> Task.CompletedTask;

		public Task SendToGroup<T>(string group, T message, CancellationToken cancellationToken = default)
			where T : class
			=> Task.CompletedTask;

		public Task SendToConnection<T>(string connectionId, T message, CancellationToken cancellationToken = default)
			where T : class
			=> Task.CompletedTask;

		public Task AddToGroup(string connectionId, string group, CancellationToken cancellationToken = default)
			=> Task.CompletedTask;

		public Task RemoveFromGroup(string connectionId, string group, CancellationToken cancellationToken = default)
			=> Task.CompletedTask;
	}

	private sealed class FakeFontCatalog : IFontCatalog
	{
		public IReadOnlyList<FontFaceInfo> GetFaces() => [];

		public byte[]? GetFaceFile(string faceId) => null;
	}

	private sealed class FakeWidgetIconResources : IWidgetIconResources
	{
		public Task<MacroDeck.Ui.Model.Resources.UiResource?> ResolveAsync(WidgetIconReference? reference,
			CancellationToken cancellationToken)
			=> Task.FromResult<MacroDeck.Ui.Model.Resources.UiResource?>(null);

		public void Evict(Guid iconId)
		{
		}
	}

	private sealed class FakeWidgetIconService : IWidgetIconService
	{
		public Task<WidgetIconResolution> Resolve(Guid widgetId, CancellationToken cancellationToken = default)
			=> Task.FromResult(WidgetIconResolution.Inactive);

		public TimeSpan? GetProviderPollInterval(Guid widgetId) => null;
	}

	private sealed class FakeLabelTextService : ILabelTextService
	{
		public Task<string?> ResolveText(Guid widgetId, string state, CancellationToken cancellationToken = default)
			=> Task.FromResult<string?>("resolved-label");

		public Task<string?> ResolvePreview(LabelImagePreviewRequest request,
			CancellationToken cancellationToken = default)
			=> Task.FromResult<string?>(request.Label);
	}

	/// <summary>Proves the Preview path skips widget lookup entirely, rather than merely tolerating an
	/// empty cache: any call at all to <see cref="GetAllFolders" /> fails the test outright.</summary>
	private sealed class ThrowingFolderCache : MacroDeckHost.Application.Caching.IFolderCache
	{
		public List<FolderEntity> GetAllFolders()
			=> throw new InvalidOperationException(
				"A Preview session must never query the folder cache - it has no stored widget to look up.");

		public FolderEntity? GetFolderById(Guid id) => throw new NotSupportedException();

		public Task InitializeCache() => Task.CompletedTask;

		public List<FolderEntity> GetFoldersByParentId(Guid? parentId) => throw new NotSupportedException();

		public List<FolderEntity> GetFoldersByProfileId(Guid profileId) => throw new NotSupportedException();

		public Task AddOrUpdate(FolderEntity folder) => throw new NotSupportedException();

		public Task AddOrUpdateRange(IReadOnlyCollection<FolderEntity> folders) => throw new NotSupportedException();

		public Task<FolderSubtreeRemoval> RemoveSubtree(Guid rootId)
			=> throw new NotSupportedException();

		public void AddWidget(Guid folderId, WidgetEntity widget) => throw new NotSupportedException();

		public void AddWidgets(Guid folderId, IReadOnlyList<WidgetEntity> widgets) => throw new NotSupportedException();

		public void UpdateWidget(Guid folderId, WidgetEntity widget) => throw new NotSupportedException();

		public void UpdateWidgets(Guid folderId, IReadOnlyList<WidgetEntity> widgets) =>
			throw new NotSupportedException();

		public void UpdateWidgetPositions(Guid folderId, IReadOnlyList<WidgetPlacement> placements)
			=> throw new NotSupportedException();

		public void RemoveWidget(Guid folderId, Guid widgetId) => throw new NotSupportedException();

		public void RemoveWidgets(Guid folderId, IReadOnlyList<Guid> widgetIds) => throw new NotSupportedException();

		public void ReplaceWidgets(Guid folderId, IReadOnlyList<Guid> removeIds, IReadOnlyList<WidgetEntity> addWidgets)
			=> throw new NotSupportedException();
	}
}
