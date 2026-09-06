using System.Text.Json;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeckHost.Application.Caching;
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

[TestFixture]
internal sealed class WidgetUiSessionTests
{
	private const string DeviceA = "device-a";
	private const string DeviceB = "device-b";

	private ManualTimeProvider _time = null!;
	private RecordingUiSessionTransport _transport = null!;
	private UiSessionRegistry _registry = null!;
	private FakeFolderCache _folders = null!;
	private WidgetUiProviderRegistry _widgetProviders = null!;
	private UiSessionBroker _broker = null!;
	private WidgetUiSessionOpener _opener = null!;

	[SetUp]
	public void SetUp()
	{
		_time = new ManualTimeProvider();
		_transport = new RecordingUiSessionTransport();
		_registry = new UiSessionRegistry(_time);
		_folders = new FakeFolderCache();

		_broker = new UiSessionBroker(new WidgetOnlyResolver(() => _widgetProviders),
			_transport,
			_registry,
			new PluginSessionRegistry(_time, Serilog.Core.Logger.None),
			new StubIntegrationRegistry(),
			_time,
			Serilog.Core.Logger.None);

		_widgetProviders = new WidgetUiProviderRegistry(_folders,
			[
				new StubBuiltInWidgetUiProvider(WidgetTypeIds.Weather),
				new StubBuiltInWidgetUiProvider(WidgetTypeIds.ActionButton)
			],
			() => _broker,
			Serilog.Core.Logger.None);

		_opener = new WidgetUiSessionOpener(_folders,
			new EmptyProfileCache(),
			new WidgetDataSchemaProvider(new WidgetTypeRegistry(new RecordingMediator())),
			new WidgetTypeRegistry(new RecordingMediator()),
			_registry,
			_broker);
	}

	[TearDown]
	public void TearDown()
	{
		_broker.Dispose();
		_registry.Dispose();
	}

	[Test]
	public async Task Two_devices_open_their_own_session_for_the_same_widget()
	{
		var widget = AddWidget(WidgetTypeIds.Weather, "{}");

		var ticketA = _opener.Open(new OpenWidgetUiSessionRequest { WidgetId = widget.Id.ToString() },
			DeviceA,
			isAdmin: false);
		var ticketB = _opener.Open(new OpenWidgetUiSessionRequest { WidgetId = widget.Id.ToString() },
			DeviceB,
			isAdmin: false);

		await ticketA.Ready;
		await ticketB.Ready;

		Assert.Multiple(() =>
		{
			Assert.That(ticketA.Accepted, Is.True);
			Assert.That(ticketB.Accepted, Is.True);
			Assert.That(ticketB.SessionId,
				Is.Not.EqualTo(ticketA.SessionId),
				"Two devices ended up sharing one session.");
			Assert.That(_registry.Find(ticketA.SessionId), Is.Not.Null);
			Assert.That(_registry.Find(ticketB.SessionId), Is.Not.Null);
		});
	}

	[Test]
	public async Task Closing_one_devices_session_leaves_the_other_open()
	{
		var widget = AddWidget(WidgetTypeIds.Weather, "{}");

		var ticketA = _opener.Open(new OpenWidgetUiSessionRequest { WidgetId = widget.Id.ToString() },
			DeviceA,
			isAdmin: false);
		var ticketB = _opener.Open(new OpenWidgetUiSessionRequest { WidgetId = widget.Id.ToString() },
			DeviceB,
			isAdmin: false);
		await ticketA.Ready;
		await ticketB.Ready;

		await _broker.CloseAsync(ticketA.SessionId, "device A closed its tile", CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(_registry.Find(ticketA.SessionId), Is.Null, "Device A's session should be gone.");
			Assert.That(_registry.Find(ticketB.SessionId), Is.Not.Null, "Device B's session was torn down too.");
		});
	}

	[Test]
	public async Task A_drag_ghost_gets_its_own_session_separate_from_the_live_tile()
	{
		var widget = AddWidget(WidgetTypeIds.Weather, "{}");

		var live = _opener.Open(new OpenWidgetUiSessionRequest { WidgetId = widget.Id.ToString() },
			DeviceA,
			isAdmin: false);
		var ghost = _opener.Open(new OpenWidgetUiSessionRequest { WidgetId = widget.Id.ToString(), Ghost = true },
			DeviceA,
			isAdmin: false);
		await live.Ready;
		await ghost.Ready;

		Assert.That(ghost.SessionId, Is.Not.EqualTo(live.SessionId));

		await _broker.CloseAsync(ghost.SessionId, "drag cancelled", CancellationToken.None);

		Assert.That(_registry.Find(live.SessionId),
			Is.Not.Null,
			"Closing the ghost's session tore down the live tile's session.");
	}

	/// <summary>
	/// The whole chain a saved edit travels for a widget whose session cannot re-read its own
	/// configuration: the broker has to find that widget's open sessions from the surface alone, and every
	/// device drawing it has to be told to build its view again. Asserted against a real registry and a
	/// real broker rather than a fake, because the part that can silently do nothing is the surface lookup.
	/// </summary>
	[Test]
	public async Task Reconfiguring_a_widget_tells_every_device_drawing_it_to_rebuild()
	{
		var widget = AddWidget(WidgetTypeIds.Weather, "{}");
		var other = AddWidget(WidgetTypeIds.Weather, "{}");

		var ticketA = _opener.Open(new OpenWidgetUiSessionRequest { WidgetId = widget.Id.ToString() },
			DeviceA,
			isAdmin: false);
		var ticketB = _opener.Open(new OpenWidgetUiSessionRequest { WidgetId = widget.Id.ToString() },
			DeviceB,
			isAdmin: false);
		var untouched = _opener.Open(new OpenWidgetUiSessionRequest { WidgetId = other.Id.ToString() },
			DeviceA,
			isAdmin: false);
		await ticketA.Ready;
		await ticketB.Ready;
		await untouched.Ready;

		// Attached, because a session nobody is watching has nobody to tell - which is what makes the
		// difference between the registry forgetting a session and a deck being asked to draw again.
		_broker.Attach(ticketA.SessionId, "connection-a", DeviceA);
		_broker.Attach(ticketB.SessionId, "connection-b", DeviceB);
		_broker.Attach(untouched.SessionId, "connection-a", DeviceA);

		_broker.InvalidateWidgetSessions(widget.Id);

		// Each session hands its terminal message to its own outbound pump, so the two arrive
		// independently of one another rather than by the time the call returns.
		var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
		List<UiSessionInvalidatedEvent> rebuilt;
		do
		{
			rebuilt = _transport.All
				.Select(recorded => recorded.Message)
				.OfType<UiSessionInvalidatedEvent>()
				.ToList();
			if (rebuilt.Count >= 2)
			{
				break;
			}

			await Task.Delay(5);
		} while (DateTime.UtcNow < deadline);

		Assert.Multiple(() =>
		{
			Assert.That(rebuilt.Select(evt => evt.SessionId),
				Is.EquivalentTo(new[] { ticketA.SessionId, ticketB.SessionId }),
				"Every device drawing the widget has to be told, and no device drawing another one.");
			Assert.That(rebuilt.Select(evt => evt.Retryable),
				Is.All.True,
				"Reopening is exactly what is being asked for, so this must not read as a dead session.");
			Assert.That(_registry.Find(untouched.SessionId), Is.Not.Null);
		});
	}

	[Test]
	public async Task A_live_widgets_surface_carries_exactly_widgetId_widgetType_and_the_stored_data()
	{
		var widget = AddWidget(WidgetTypeIds.Weather, "{\"forecastDays\":3}");

		var ticket = _opener.Open(new OpenWidgetUiSessionRequest
			{
				WidgetId = widget.Id.ToString(), Data = JsonSerializer.SerializeToElement(new { forecastDays = 999 })
			},
			DeviceA,
			isAdmin: false);
		await ticket.Ready;

		var session = _registry.Find(ticket.SessionId)!;

		Assert.Multiple(() =>
		{
			Assert.That(session.Surface.Kind, Is.EqualTo(UiSurfaceKinds.Widget));
			Assert.That(session.SessionMode, Is.EqualTo(UiSessionModes.Shared));
			// cornerRadius joined these deliberately (ADR 0064): it is not a size - a widget spanning four
			// cells is drawn with the same corner as one spanning one - and it is the one fact about the
			// box the reader draws that no length in the tree can express. The span and the pixel size
			// are still refused.
			Assert.That(session.Surface.Attributes.Keys,
				Is.EquivalentTo(new[]
				{
					UiWidgetSurfaceAttributes.WidgetId, UiWidgetSurfaceAttributes.WidgetType,
					UiWidgetSurfaceAttributes.Data, UiWidgetSurfaceAttributes.CornerRadius
				}),
				"The surface carried a key beyond widgetId/widgetType/data/cornerRadius - a grid span or pixel size leaked in.");
			Assert.That(session.Surface.Attributes[UiWidgetSurfaceAttributes.WidgetId].GetString(),
				Is.EqualTo(widget.Id.ToString()));
			Assert.That(session.Surface.Attributes[UiWidgetSurfaceAttributes.WidgetType].GetString(),
				Is.EqualTo(WidgetTypeIds.Weather));
			Assert.That(session.Surface.Attributes[UiWidgetSurfaceAttributes.Data]
					.GetProperty("forecastDays")
					.GetInt32(),
				Is.EqualTo(3),
				"The stored data did not win over the client-supplied draft.");
		});
	}

	[TestCase("action-button")]
	[TestCase("ActionButton")]
	[TestCase("weather")]
	public async Task An_admin_preview_opens_for_a_widget_type_spelled_the_way_a_client_spells_it(
		string requestedType)
	{
		// A client sends the spelling from its own vocabulary - kebab-case, "action-button" - not the
		// enum's member name. Accepting only the member name left every multi-word type unpreviewable,
		// which is what kept the Action Button's editor preview blank while the single-word Slider worked.
		var ticket = _opener.Open(new OpenWidgetUiSessionRequest { WidgetType = requestedType },
			DeviceA,
			isAdmin: true);

		Assert.That(ticket.Accepted, Is.True, ticket.Message);
		await ticket.Ready;

		var session = _registry.Find(ticket.SessionId)!;

		Assert.That(session.Surface.Kind, Is.EqualTo(UiSurfaceKinds.Preview));
	}

	[TestCase("not-a-widget")]
	[TestCase("action button")]
	[TestCase("")]
	public void An_admin_preview_for_a_type_that_does_not_exist_is_still_rejected(string requestedType)
	{
		// Accepting a client's spelling must not turn into accepting anything: the separators come out
		// before the parse, nothing else does.
		var ticket = _opener.Open(new OpenWidgetUiSessionRequest { WidgetType = requestedType },
			DeviceA,
			isAdmin: true);

		Assert.Multiple(() =>
		{
			Assert.That(ticket.Accepted, Is.False);
			Assert.That(ticket.Code, Is.EqualTo(UiSessionErrorCodes.ProviderUnavailable));
		});
	}

	[Test]
	public async Task A_sample_preview_is_its_own_session_and_says_so_on_the_surface()
	{
		// The widget picker's sample and an editor's draft preview are both previews of the same type for
		// the same principal (issue #758). Sharing one session would hand whichever opened second the
		// other's tree - the picker showing a half-typed draft, or the editor showing the sample.
		var draft = _opener.Open(new OpenWidgetUiSessionRequest { WidgetType = WidgetTypeIds.Weather },
			DeviceA,
			isAdmin: true);
		var sample = _opener.Open(new OpenWidgetUiSessionRequest { WidgetType = WidgetTypeIds.Weather, Sample = true },
			DeviceA,
			isAdmin: true);

		Assert.That(draft.Accepted, Is.True, draft.Message);
		Assert.That(sample.Accepted, Is.True, sample.Message);
		await draft.Ready;
		await sample.Ready;

		Assert.Multiple(() =>
		{
			Assert.That(sample.SessionId, Is.Not.EqualTo(draft.SessionId));
			Assert.That(
				_registry.Find(sample.SessionId)!.Surface.Attributes.ContainsKey(UiWidgetSurfaceAttributes.Sample),
				Is.True,
				"a provider has no other way to tell a sample from a draft preview");
			Assert.That(
				_registry.Find(draft.SessionId)!.Surface.Attributes.ContainsKey(UiWidgetSurfaceAttributes.Sample),
				Is.False);
		});
	}

	[Test]
	public async Task Previews_of_the_same_type_scoped_to_different_widgets_are_separate_sessions()
	{
		// A session is reused per provider id. Two editors previewing two Action Buttons would otherwise
		// share one session, so the second one's draft would resolve its variables against the first
		// widget's values - the wrong tile's numbers, silently.
		var first = AddWidget(WidgetTypeIds.Weather, "{}");
		var second = AddWidget(WidgetTypeIds.Weather, "{}");

		var firstPreview = _opener.Open(new OpenWidgetUiSessionRequest
				{ WidgetType = WidgetTypeIds.Weather, VariableScopeWidgetId = first.Id.ToString() },
			DeviceA,
			isAdmin: true);
		var secondPreview = _opener.Open(new OpenWidgetUiSessionRequest
				{ WidgetType = WidgetTypeIds.Weather, VariableScopeWidgetId = second.Id.ToString() },
			DeviceA,
			isAdmin: true);

		Assert.That(firstPreview.Accepted, Is.True, firstPreview.Message);
		Assert.That(secondPreview.Accepted, Is.True, secondPreview.Message);
		await firstPreview.Ready;
		await secondPreview.Ready;

		Assert.Multiple(() =>
		{
			Assert.That(secondPreview.SessionId, Is.Not.EqualTo(firstPreview.SessionId));
			Assert.That(ScopeOf(firstPreview.SessionId), Is.EqualTo(first.Id.ToString()));
			Assert.That(ScopeOf(secondPreview.SessionId), Is.EqualTo(second.Id.ToString()));
		});
	}

	[Test]
	public async Task A_preview_scoped_to_a_widget_carries_that_scope_on_the_surface()
	{
		var widget = AddWidget(WidgetTypeIds.Weather, "{}");

		var ticket = _opener.Open(new OpenWidgetUiSessionRequest
				{ WidgetType = WidgetTypeIds.Weather, VariableScopeWidgetId = widget.Id.ToString() },
			DeviceA,
			isAdmin: true);

		Assert.That(ticket.Accepted, Is.True, ticket.Message);
		await ticket.Ready;

		Assert.That(ScopeOf(ticket.SessionId),
			Is.EqualTo(widget.Id.ToString()),
			"a provider has no other way to know which widget's variables the draft resolves against");
	}

	[TestCase("not-a-guid")]
	[TestCase("")]
	[TestCase(null)]
	public async Task A_preview_whose_scope_names_no_stored_widget_still_opens_without_one(string? scope)
	{
		// A widget being created has no id to name yet, and a client may send a stale one. Refusing
		// either would leave the editor with no preview at all, which is worse than an unscoped one.
		var ticket = _opener.Open(new OpenWidgetUiSessionRequest
				{ WidgetType = WidgetTypeIds.Weather, VariableScopeWidgetId = scope },
			DeviceA,
			isAdmin: true);

		Assert.That(ticket.Accepted, Is.True, ticket.Message);
		await ticket.Ready;

		Assert.That(ScopeOf(ticket.SessionId), Is.Null);
	}

	[Test]
	public async Task A_preview_scoped_to_a_widget_that_does_not_exist_still_opens_without_one()
	{
		var ticket = _opener.Open(new OpenWidgetUiSessionRequest
				{ WidgetType = WidgetTypeIds.Weather, VariableScopeWidgetId = Guid.NewGuid().ToString() },
			DeviceA,
			isAdmin: true);

		Assert.That(ticket.Accepted, Is.True, ticket.Message);
		await ticket.Ready;

		Assert.That(ScopeOf(ticket.SessionId), Is.Null);
	}

	private string? ScopeOf(string sessionId)
		=> _registry.Find(sessionId)!.Surface.Attributes
			.TryGetValue(UiWidgetSurfaceAttributes.VariableScopeWidgetId, out var scope)
			? scope.GetString()
			: null;

	[Test]
	public void A_non_admin_preview_is_rejected_with_a_forbidden_code_and_mints_no_session()
	{
		var providerId = WidgetUiProviderRegistry.PreviewProviderIdFor(WidgetTypeIds.Weather, DeviceA);

		var ticket = _opener.Open(new OpenWidgetUiSessionRequest { WidgetType = WidgetTypeIds.Weather },
			DeviceA,
			isAdmin: false);

		Assert.Multiple(() =>
		{
			Assert.That(ticket.Accepted, Is.False);
			Assert.That(ticket.Code, Is.EqualTo(UiSessionErrorCodes.SessionForbidden));
			Assert.That(_registry.SessionsForProvider(providerId), Is.Empty);
		});
	}

	[TestCase(9)]
	[TestCase(0)]
	public void An_admin_preview_with_an_out_of_range_forecastDays_is_rejected(int forecastDays)
	{
		var ticket = _opener.Open(new OpenWidgetUiSessionRequest
			{
				WidgetType = WidgetTypeIds.Weather,
				Data = JsonSerializer.SerializeToElement(new { forecastDays })
			},
			DeviceA,
			isAdmin: true);

		Assert.Multiple(() =>
		{
			Assert.That(ticket.Accepted, Is.False);
			Assert.That(ticket.Code, Is.EqualTo(UiSessionErrorCodes.InvalidPayload));
		});
	}

	[Test]
	public void An_admin_preview_with_a_wrong_typed_forecastDays_is_rejected()
	{
		using var data = JsonDocument.Parse("{\"forecastDays\":\"5\"}");

		var ticket = _opener.Open(new OpenWidgetUiSessionRequest
				{ WidgetType = WidgetTypeIds.Weather, Data = data.RootElement },
			DeviceA,
			isAdmin: true);

		Assert.Multiple(() =>
		{
			Assert.That(ticket.Accepted, Is.False);
			Assert.That(ticket.Code, Is.EqualTo(UiSessionErrorCodes.InvalidPayload));
		});
	}

	[Test]
	public async Task An_admin_preview_with_an_unknown_extension_key_is_accepted_and_carries_the_draft()
	{
		using var data = JsonDocument.Parse("{\"forecastDays\":7,\"someFutureKey\":1}");

		var ticket = _opener.Open(new OpenWidgetUiSessionRequest
				{ WidgetType = WidgetTypeIds.Weather, Data = data.RootElement },
			DeviceA,
			isAdmin: true);
		await ticket.Ready;

		var session = _registry.Find(ticket.SessionId)!;

		Assert.Multiple(() =>
		{
			Assert.That(ticket.Accepted, Is.True, "A widget saved by a newer client must stay renderable.");
			Assert.That(session.Surface.Kind, Is.EqualTo(UiSurfaceKinds.Preview));
			Assert.That(session.Surface.Attributes[UiWidgetSurfaceAttributes.Data].GetProperty("someFutureKey")
					.GetInt32(),
				Is.EqualTo(1),
				"The preview surface did not carry the client's own draft data.");
		});
	}

	[Test]
	public async Task Two_opens_for_the_same_widget_and_principal_return_one_session_id()
	{
		var widget = AddWidget(WidgetTypeIds.Weather, "{}");
		var request = new OpenWidgetUiSessionRequest { WidgetId = widget.Id.ToString() };

		var first = _opener.Open(request, DeviceA, isAdmin: false);
		await first.Ready;
		var second = _opener.Open(request, DeviceA, isAdmin: false);

		Assert.Multiple(() =>
		{
			Assert.That(second.Accepted, Is.True);
			Assert.That(second.SessionId, Is.EqualTo(first.SessionId));
		});
	}

	[Test]
	public void An_unknown_widget_id_is_rejected_and_mints_no_session()
	{
		var widgetId = Guid.NewGuid();
		var providerId = WidgetUiProviderRegistry.ProviderIdFor(widgetId, DeviceA);

		var ticket = _opener.Open(new OpenWidgetUiSessionRequest { WidgetId = widgetId.ToString() },
			DeviceA,
			isAdmin: false);

		Assert.Multiple(() =>
		{
			Assert.That(ticket.Accepted, Is.False);
			Assert.That(ticket.Code, Is.EqualTo(UiSessionErrorCodes.ProviderUnavailable));
			Assert.That(_registry.SessionsForProvider(providerId), Is.Empty);
		});
	}

	[Test]
	public void A_widget_type_with_no_registered_provider_is_rejected_and_mints_no_session()
	{
		var widget = AddWidget(WidgetTypeIds.Clock, "{}");
		var providerId = WidgetUiProviderRegistry.ProviderIdFor(widget.Id, DeviceA);

		var ticket = _opener.Open(new OpenWidgetUiSessionRequest { WidgetId = widget.Id.ToString() },
			DeviceA,
			isAdmin: false);

		Assert.Multiple(() =>
		{
			Assert.That(ticket.Accepted, Is.False);
			Assert.That(ticket.Code, Is.EqualTo(UiSessionErrorCodes.ProviderUnavailable));
			Assert.That(_registry.SessionsForProvider(providerId), Is.Empty);
		});
	}

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

	private sealed class StubBuiltInWidgetUiProvider : IBuiltInWidgetUiProvider
	{
		public StubBuiltInWidgetUiProvider(string widgetType) => WidgetTypeId = widgetType;

		public string WidgetTypeId { get; }

		public IReadOnlyList<UiSurfaceDeclaration> Surfaces { get; } =
		[
			new()
				{ Kind = UiSurfaceKinds.Widget, SessionMode = UiSessionModes.Shared },
			new()
				{ Kind = UiSurfaceKinds.Preview, SessionMode = UiSessionModes.Shared }
		];

		public Task<IUiSession?> CreateSessionAsync(UiSessionRequest request, CancellationToken cancellationToken)
		{
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
