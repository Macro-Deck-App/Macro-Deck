using System.Text.Json;
using MacroDeck.Localization;
using MacroDeck.Sdk.Ui;
using MacroDeck.Sdk.Widgets;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.Integrations.ConfigFlow;
using MacroDeckHost.Application.Plugins;
using MacroDeckHost.Application.Ui.Sessions;
using MacroDeckHost.Application.Ui.Sessions.InProcess;
using MacroDeckHost.Application.Ui.Transport.Messages.UiSessions;
using MacroDeckHost.Application.Widgets;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Widgets;
using MacroDeckHost.Tests.UnitTests.Auth;
using MacroDeckHost.Tests.UnitTests.TestSupport;

namespace MacroDeckHost.Tests.UnitTests.Ui.Sessions;

/// <summary>
/// Opening the <c>widget-config</c> entry point (issue #837): the surface it hands a widget's provider,
/// and the ways it must never mint or advance one - an unknown widget, an entry point nothing
/// recognises, and a provider that declines. Driven through the real
/// <see cref="WidgetUiProviderRegistry" /> rather than a raw <see cref="IUiSessionProvider" /> stub, so
/// the provider-id scheme added for this issue (widget id, not just type, in <c>widget-config:...</c>)
/// is actually exercised rather than assumed.
/// </summary>
[TestFixture]
internal sealed class WidgetConfigUiSessionTests
{
	private const string DeviceA = "device-a";

	private ManualTimeProvider _time = null!;
	private FakeFolderCache _folders = null!;
	private WidgetUiProviderRegistry _widgetProviders = null!;
	private UiSessionRegistry _registry = null!;
	private UiSessionBroker _broker = null!;
	private ConfigCapableWidgetUiProvider _weatherProvider = null!;
	private ConfigUiSessionOpener _opener = null!;

	[SetUp]
	public void SetUp()
	{
		_time = new ManualTimeProvider();
		_registry = new UiSessionRegistry(_time);
		_folders = new FakeFolderCache();
		_weatherProvider = new ConfigCapableWidgetUiProvider(WidgetTypeIds.Weather);

		_broker = new UiSessionBroker(new WidgetOnlyResolver(() => _widgetProviders),
			new RecordingUiSessionTransport(),
			_registry,
			new PluginSessionRegistry(_time, Serilog.Core.Logger.None),
			new StubIntegrationRegistry(),
			_time,
			Serilog.Core.Logger.None);

		_widgetProviders = new WidgetUiProviderRegistry(_folders,
			[_weatherProvider],
			() => _broker,
			Serilog.Core.Logger.None);

		_opener = new ConfigUiSessionOpener(new StubIntegrationRegistry(),
			new ThrowingConfigFlowManager(),
			TestFolderViewProviders.Registry(),
			_folders,
			new WidgetTypeRegistry(new RecordingMediator()),
			_broker);
	}

	[TearDown]
	public void TearDown()
	{
		_broker.Dispose();
		_registry.Dispose();
	}

	[Test]
	public async Task The_stored_configuration_crosses_verbatim_and_nested()
	{
		const string data = """
							{
							  "border": { "style": "comet", "color": "#ff8800" },
							  "states": [ { "id": "off", "appearance": { "labelColor": "#111111" } } ],
							  "flows": "[{\"trigger\":\"onShortPress\"}]",
							  "historyLength": 120,
							  "x-experimental": { "k": 1 }
							}
							""";
		var widget = AddWidget(WidgetTypeIds.Weather, data);

		var ticket = Open(widget.Id);
		Assert.That(ticket.Accepted, Is.True, ticket.Message);
		await ticket.Ready;

		var widgetData = _weatherProvider.LastSurface!.Attributes[UiConfigSurfaceAttributes.WidgetData];

		Assert.Multiple(() =>
		{
			Assert.That(widgetData.GetProperty("border").GetProperty("style").GetString(), Is.EqualTo("comet"));
			Assert.That(widgetData.GetProperty("states").ValueKind, Is.EqualTo(JsonValueKind.Array));
			Assert.That(widgetData.GetProperty("states").GetArrayLength(), Is.EqualTo(1));
			Assert.That(widgetData.GetProperty("states")[0].GetProperty("appearance").GetProperty("labelColor")
					.GetString(),
				Is.EqualTo("#111111"));
			Assert.That(widgetData.GetProperty("flows").ValueKind,
				Is.EqualTo(JsonValueKind.String),
				"flows must stay the stored JSON string, not be re-parsed into an array");
			Assert.That(widgetData.GetProperty("flows").GetString(), Is.EqualTo("[{\"trigger\":\"onShortPress\"}]"));
			Assert.That(widgetData.GetProperty("historyLength").GetInt32(), Is.EqualTo(120));
			Assert.That(widgetData.GetProperty("x-experimental").GetProperty("k").GetInt32(), Is.EqualTo(1));
		});
	}

	[Test]
	public async Task Absent_stored_data_reads_as_an_empty_object()
	{
		var widget = AddWidget(WidgetTypeIds.Weather, data: null);

		var ticket = Open(widget.Id);
		await ticket.Ready;

		var widgetData = _weatherProvider.LastSurface!.Attributes[UiConfigSurfaceAttributes.WidgetData];

		Assert.Multiple(() =>
		{
			Assert.That(widgetData.ValueKind, Is.EqualTo(JsonValueKind.Object));
			Assert.That(widgetData.EnumerateObject(), Is.Empty);
		});
	}

	[Test]
	public async Task The_editors_own_draft_is_what_the_surface_carries()
	{
		// The editor holds an unsaved draft - a JSON-mode edit, say - and the tree has to render that,
		// not the record the widget was last saved with.
		var widget = AddWidget(WidgetTypeIds.Weather, """{"historyLength":10}""");

		var ticket = Open(widget.Id, draft: """{"historyLength":120}""");
		await ticket.Ready;

		var widgetData = _weatherProvider.LastSurface!.Attributes[UiConfigSurfaceAttributes.WidgetData];

		Assert.That(widgetData.GetProperty("historyLength").GetInt32(), Is.EqualTo(120));
	}

	[TestCase(null)]
	[TestCase("")]
	[TestCase("not json")]
	[TestCase("[1,2]")]
	public async Task A_draft_that_is_not_a_json_object_leaves_the_stored_configuration_in_place(string? draft)
	{
		// A client that sends nothing - every client before this key existed - must keep getting exactly
		// the tree it got before, and so must one whose draft never survived serialization.
		var widget = AddWidget(WidgetTypeIds.Weather, """{"historyLength":10}""");

		var ticket = Open(widget.Id, draft);
		await ticket.Ready;

		var widgetData = _weatherProvider.LastSurface!.Attributes[UiConfigSurfaceAttributes.WidgetData];

		Assert.That(widgetData.GetProperty("historyLength").GetInt32(), Is.EqualTo(10));
	}

	[Test]
	public async Task An_empty_draft_object_is_an_answer_rather_than_a_fallback_to_the_stored_data()
	{
		var widget = AddWidget(WidgetTypeIds.Weather, """{"historyLength":10}""");

		var ticket = Open(widget.Id, draft: "{}");
		await ticket.Ready;

		var widgetData = _weatherProvider.LastSurface!.Attributes[UiConfigSurfaceAttributes.WidgetData];

		Assert.That(widgetData.EnumerateObject(), Is.Empty);
	}

	[Test]
	public async Task The_surface_carries_entryPoint_widgetId_and_widgetType()
	{
		var widget = AddWidget(WidgetTypeIds.Weather, "{}");

		var ticket = Open(widget.Id);
		await ticket.Ready;

		var session = _registry.Find(ticket.SessionId)!;

		Assert.Multiple(() =>
		{
			Assert.That(session.Surface.Kind, Is.EqualTo(UiSurfaceKinds.Config));
			Assert.That(session.SessionMode, Is.EqualTo(UiSessionModes.Exclusive));
			Assert.That(session.Surface.Attributes[UiConfigSurfaceAttributes.EntryPoint].GetString(),
				Is.EqualTo(UiConfigEntryPoints.WidgetConfig));
			Assert.That(session.Surface.Attributes[UiConfigSurfaceAttributes.WidgetId].GetString(),
				Is.EqualTo(widget.Id.ToString()));
			Assert.That(session.Surface.Attributes[UiConfigSurfaceAttributes.WidgetType].GetString(),
				Is.EqualTo(WidgetTypeIds.Weather));
		});
	}

	[Test]
	public void An_unknown_widget_id_is_rejected_and_never_asks_a_provider()
	{
		var ticket = Open(Guid.NewGuid());

		Assert.Multiple(() =>
		{
			Assert.That(ticket.Accepted, Is.False);
			Assert.That(ticket.Code, Is.EqualTo(UiSessionErrorCodes.ProviderUnavailable));
			Assert.That(_weatherProvider.OpenCalls, Is.EqualTo(0));
		});
	}

	[Test]
	public void An_unrecognised_entry_point_is_rejected_before_any_provider_is_consulted()
	{
		var widget = AddWidget(WidgetTypeIds.Weather, "{}");

		var ticket = _opener.Open(new OpenConfigUiSessionRequest
			{
				EntryPoint = "widget-appearance-config", WidgetId = widget.Id.ToString()
			},
			DeviceA);

		Assert.Multiple(() =>
		{
			Assert.That(ticket.Accepted, Is.False);
			Assert.That(ticket.Code, Is.EqualTo(UiSessionErrorCodes.ProviderUnavailable));
			Assert.That(_weatherProvider.OpenCalls, Is.EqualTo(0));
		});
	}

	[Test]
	public async Task A_provider_that_declines_is_not_an_error_and_the_session_never_becomes_attachable()
	{
		_weatherProvider.Decline = true;
		var widget = AddWidget(WidgetTypeIds.Weather, "{}");

		var ticket = Open(widget.Id);
		Assert.That(ticket.Accepted, Is.True, "minting a session id is not the same as the provider accepting it");
		var settled = await ticket.Ready;

		var attach = _broker.Attach(ticket.SessionId, "connection-a", DeviceA);

		Assert.Multiple(() =>
		{
			Assert.That(settled.Accepted, Is.False);
			Assert.That(settled.Code, Is.EqualTo(UiSessionErrorCodes.ProviderRejected));
			Assert.That(attach.Accepted, Is.False, "a declined session must never become attachable");
		});
	}

	[Test]
	public async Task Two_editors_on_two_different_widgets_of_the_same_type_get_two_distinct_sessions()
	{
		// A session is reused per provider id (WidgetUiProviderRegistry). Keying the widget-config
		// provider id on the widget type alone would make two editors on two Weather widgets share one
		// session, so the second editor's tree would silently be the first widget's.
		var first = AddWidget(WidgetTypeIds.Weather, "{}");
		var second = AddWidget(WidgetTypeIds.Weather, "{}");

		var firstTicket = Open(first.Id);
		var secondTicket = Open(second.Id);
		await firstTicket.Ready;
		await secondTicket.Ready;

		Assert.Multiple(() =>
		{
			Assert.That(firstTicket.Accepted, Is.True, firstTicket.Message);
			Assert.That(secondTicket.Accepted, Is.True, secondTicket.Message);
			Assert.That(secondTicket.SessionId,
				Is.Not.EqualTo(firstTicket.SessionId),
				"two widgets of the same type shared one session - the config provider id did not carry the widget id");
		});
	}

	[Test]
	public async Task A_plugin_widget_type_without_configuration_is_refused_before_its_provider_is_asked()
	{
		var widgetTypes = new WidgetTypeRegistry(new RecordingMediator());
		var registration = await widgetTypes.Register("com.example.gauges",
			new WidgetTypeDescriptor("plain", LocalizedText.FromLiteral("Plain"), HasConfiguration: false));
		var opener = new ConfigUiSessionOpener(new StubIntegrationRegistry(),
			new ThrowingConfigFlowManager(),
			TestFolderViewProviders.Registry(),
			_folders,
			widgetTypes,
			_broker);
		var widget = AddWidget(registration.WidgetTypeId, "{}");

		var ticket = opener.Open(new OpenConfigUiSessionRequest
			{
				EntryPoint = UiConfigEntryPoints.WidgetConfig, WidgetId = widget.Id.ToString()
			},
			DeviceA);

		Assert.Multiple(() =>
		{
			Assert.That(ticket.Accepted, Is.False);
			Assert.That(ticket.Code, Is.EqualTo(UiSessionErrorCodes.ProviderRejected));
		});
	}

	private UiSessionOpenTicket Open(Guid widgetId, string? draft = null)
		=> _opener.Open(new OpenConfigUiSessionRequest
			{
				EntryPoint = UiConfigEntryPoints.WidgetConfig, WidgetId = widgetId.ToString(), WidgetData = draft
			},
			DeviceA);

	private WidgetEntity AddWidget(string type, string? data)
	{
		var widget = new WidgetEntity { Id = Guid.NewGuid(), Type = type, Data = data };
		_folders.AddWidget(widget);
		return widget;
	}

	private sealed class WidgetOnlyResolver : IUiSessionProviderResolver
	{
		private readonly Func<WidgetUiProviderRegistry> _widgets;

		public WidgetOnlyResolver(Func<WidgetUiProviderRegistry> widgets) => _widgets = widgets;

		public IUiSessionProvider? Resolve(string providerId) => _widgets().Resolve(providerId);
	}

	/// <summary>Never expected to be called: none of these tests exercise the other three entry points,
	/// so a call here means widget-config leaked into the wrong branch.</summary>
	private sealed class ThrowingConfigFlowManager : IConfigFlowManager
	{
		public Task<ConfigFlowStartOutcome> StartAsync(string integrationId, CancellationToken cancellationToken)
			=> throw new InvalidOperationException("widget-config must never touch the config flow manager.");

		public Task<ConfigFlowStartOutcome> StartAsync(string integrationId,
			string? title,
			Guid? entryId,
			CancellationToken cancellationToken)
			=> throw new InvalidOperationException("widget-config must never touch the config flow manager.");

		public bool TryGetActiveFlow(Guid flowId, out ConfigFlowActiveFlowInfo? info)
			=> throw new InvalidOperationException("widget-config must never touch the config flow manager.");

		public Task<ConfigFlowSubmitOutcome> SubmitAsync(Guid flowId,
			string stepId,
			IReadOnlyDictionary<string, JsonElement> values,
			CancellationToken cancellationToken)
			=> throw new InvalidOperationException("widget-config must never touch the config flow manager.");

		public Task<ConfigFlowSubmitOutcome> SubmitAsync(Guid flowId,
			string stepId,
			IReadOnlyDictionary<string, JsonElement> values,
			IReadOnlyCollection<string> clearedSecretFields,
			CancellationToken cancellationToken)
			=> throw new InvalidOperationException("widget-config must never touch the config flow manager.");

		public Task AbandonAsync(Guid flowId, CancellationToken cancellationToken)
			=> throw new InvalidOperationException("widget-config must never touch the config flow manager.");
	}

	/// <summary>A widget provider that declares (and, unless told to decline, serves) a config surface -
	/// standing in for the workstream that will add this to the real built-in providers. Records what it
	/// was actually asked to render and how many times it was asked.</summary>
	private sealed class ConfigCapableWidgetUiProvider : IBuiltInWidgetUiProvider
	{
		public ConfigCapableWidgetUiProvider(string widgetType) => WidgetTypeId = widgetType;

		public string WidgetTypeId { get; }

		public bool Decline { get; set; }

		public int OpenCalls { get; private set; }

		public UiSurface? LastSurface { get; private set; }

		public IReadOnlyList<UiSurfaceDeclaration> Surfaces { get; } =
		[
			new()
				{ Kind = UiSurfaceKinds.Config, SessionMode = UiSessionModes.Exclusive }
		];

		public Task<IUiSession?> CreateSessionAsync(UiSessionRequest request, CancellationToken cancellationToken)
		{
			OpenCalls++;
			LastSurface = request.Surface;

			if (Decline)
			{
				return Task.FromResult<IUiSession?>(null);
			}

			var surface = request.Surface;
			return Task.FromResult<IUiSession?>(new StubUiSession
			{
				Tree = () => new UiTree
				{
					Revision = 1, Surface = surface, Root = new UiNode { Id = "root", Type = "panel" }
				}
			});
		}
	}

	private sealed class FakeFolderCache : IFolderCache
	{
		private readonly FolderEntity _folder = new() { Id = Guid.NewGuid(), Name = "folder", Order = 0 };

		public void AddWidget(WidgetEntity widget) => _folder.Widgets.Add(widget);

		public List<FolderEntity> GetAllFolders() => [_folder];
		public FolderEntity? GetFolderById(Guid id) => id == _folder.Id ? _folder : null;
		public Task InitializeCache() => Task.CompletedTask;
		public List<FolderEntity> GetFoldersByParentId(Guid? parentId) => [_folder];
		public List<FolderEntity> GetFoldersByProfileId(Guid profileId) => [_folder];
		public Task AddOrUpdate(FolderEntity folder) => Task.CompletedTask;
		public Task AddOrUpdateRange(IReadOnlyCollection<FolderEntity> folders) => Task.CompletedTask;

		public Task<FolderSubtreeRemoval> RemoveSubtree(Guid rootId)
			=> Task.FromResult(new FolderSubtreeRemoval(false, [], []));

		public void AddWidget(Guid folderId, WidgetEntity widget) => _folder.Widgets.Add(widget);

		public void AddWidgets(Guid folderId, IReadOnlyList<WidgetEntity> widgets) => _folder.Widgets.AddRange(widgets);

		public void UpdateWidget(Guid folderId, WidgetEntity widget)
		{
		}

		public void UpdateWidgets(Guid folderId, IReadOnlyList<WidgetEntity> widgets)
		{
		}

		public void UpdateWidgetPositions(Guid folderId, IReadOnlyList<WidgetPlacement> placements)
		{
		}

		public void RemoveWidget(Guid folderId, Guid widgetId) => _folder.Widgets.RemoveAll(w => w.Id == widgetId);

		public void RemoveWidgets(Guid folderId, IReadOnlyList<Guid> widgetIds)
		{
		}

		public void ReplaceWidgets(Guid folderId, IReadOnlyList<Guid> removeIds, IReadOnlyList<WidgetEntity> addWidgets)
		{
		}
	}
}
