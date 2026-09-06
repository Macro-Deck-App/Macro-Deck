using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Events.Handlers;
using MacroDeckHost.Application.Rendering;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Widgets;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Widgets;
using MacroDeckHost.Tests.UnitTests.TestSupport;

namespace MacroDeckHost.Tests.UnitTests.Ui;

[TestFixture]
public class WidgetEventDtoTests
{
	private RecordingTransport _transport = null!;
	private LabelRenderChannel _renderQueue = null!;

	[SetUp]
	public void SetUp()
	{
		_transport = new RecordingTransport();
		_renderQueue = new LabelRenderChannel();
	}

	[Test]
	public async Task WidgetUpdatedEvent_CarriesThePinFlag()
	{
		var handler = new WidgetUpdatedNotificationHandler(_transport,
			_renderQueue,
			new WidgetRenderSignals(),
			new RecordingUiSessionBroker());

		await handler.Handle(new WidgetUpdatedNotification(PinnedWidget()), CancellationToken.None);

		var evt = _transport.Sent.OfType<WidgetUpdatedEvent>().Single();
		Assert.That(evt.Widget!.IsPinned, Is.True);
	}

	[Test]
	public async Task WidgetUpdatedEvent_CarriesTheWidgetData()
	{
		var widget = PinnedWidget();
		widget.Data = """{"mode":"momentary","label":"changed"}""";
		var handler = new WidgetUpdatedNotificationHandler(_transport,
			_renderQueue,
			new WidgetRenderSignals(),
			new RecordingUiSessionBroker());

		await handler.Handle(new WidgetUpdatedNotification(widget), CancellationToken.None);

		var evt = _transport.Sent.OfType<WidgetUpdatedEvent>().Single();
		Assert.That(evt.Widget!.Data, Is.EqualTo(widget.Data));
	}

	/// <summary>
	/// A widget type whose session builds its configuration into its own tree - Slider, Clock, Weather,
	/// HistoryGraph and every plugin widget, none of which subscribe to the stored-data signal - has no way
	/// to apply a saved edit. Its sessions are invalidated so its clients reopen and are served a tree
	/// built from the configuration as it now stands; without that, a slider renamed in the editor went on
	/// reading its old label on every deck until the page was reloaded.
	/// </summary>
	[Test]
	public async Task WidgetUpdated_RebuildsTheSessionsOfAWidgetNothingAppliedTheChangeFor()
	{
		var broker = new RecordingUiSessionBroker();
		var widget = PinnedWidget();
		var handler = new WidgetUpdatedNotificationHandler(_transport,
			_renderQueue,
			new WidgetRenderSignals(),
			broker);

		await handler.Handle(new WidgetUpdatedNotification(widget), CancellationToken.None);

		Assert.That(broker.InvalidatedWidgets, Is.EqualTo(new[] { widget.Id }));
	}

	[Test]
	public async Task WidgetUpdated_LeavesASessionThatAppliedTheChangeItselfAlone()
	{
		var broker = new RecordingUiSessionBroker();
		var signals = new WidgetRenderSignals();
		var widget = PinnedWidget();
		using var subscription = signals.SubscribeDataChanged(widget.Id.ToString(), _ => { });
		var handler = new WidgetUpdatedNotificationHandler(_transport, _renderQueue, signals, broker);

		await handler.Handle(new WidgetUpdatedNotification(widget), CancellationToken.None);

		Assert.That(broker.InvalidatedWidgets, Is.Empty);
	}

	/// <summary>Dragging a widget publishes this on every step, and a move changes nothing a session drew.
	/// Tearing its tree down for one would cost the deck its artwork and its animations mid-drag.</summary>
	[Test]
	public async Task WidgetUpdated_LeavesTheSessionsAloneWhenOnlyThePlacementChanged()
	{
		var broker = new RecordingUiSessionBroker();
		var widget = PinnedWidget();
		var handler = new WidgetUpdatedNotificationHandler(_transport,
			_renderQueue,
			new WidgetRenderSignals(),
			broker);

		await handler.Handle(new WidgetUpdatedNotification(widget, DataChanged: false), CancellationToken.None);

		Assert.That(broker.InvalidatedWidgets, Is.Empty);
	}

	/// <summary>
	/// Every widget in a folder builds its own edge clearance from that folder's corner radius (ADR
	/// 0083), so a radius change is one of the few folder edits that has to reach the widgets. Without
	/// this the deck would keep the clearance it was opened with until something else reopened it - the
	/// stale-tree bug the admitted key would otherwise have introduced.
	/// </summary>
	[Test]
	public async Task FolderUpdated_RebuildsItsWidgetsWhenTheCornerRadiusChanged()
	{
		var broker = new RecordingUiSessionBroker();
		var folder = FolderWithOneWidget();
		var handler = new FolderUpdatedNotificationHandler(_transport, broker);

		await handler.Handle(new FolderUpdatedNotification(folder, CornerRadiusChanged: true),
			CancellationToken.None);

		Assert.That(broker.InvalidatedWidgets, Is.EqualTo(new[] { folder.Widgets[0].Id }));
	}

	/// <summary>A rename or a background change moves nothing a widget drew, and rebuilding its tree for
	/// one would cost the deck its artwork and its animations for nothing.</summary>
	[Test]
	public async Task FolderUpdated_LeavesItsWidgetsAloneForEveryOtherEdit()
	{
		var broker = new RecordingUiSessionBroker();
		var handler = new FolderUpdatedNotificationHandler(_transport, broker);

		await handler.Handle(new FolderUpdatedNotification(FolderWithOneWidget()), CancellationToken.None);

		Assert.That(broker.InvalidatedWidgets, Is.Empty);
	}

	private static FolderEntity FolderWithOneWidget()
	{
		var folderId = Guid.NewGuid();

		return new FolderEntity
		{
			Id = folderId,
			ProfileId = Guid.NewGuid(),
			Name = "Home",
			Order = 0,
			Widgets =
			[
				new WidgetEntity
				{
					Id = Guid.NewGuid(),
					FolderId = folderId,
					Type = WidgetTypeIds.Slider,
					PositionX = 0,
					PositionY = 0,
					Width = 1,
					Height = 1
				}
			]
		};
	}

	// The render-signal seam beside the client push in WidgetUpdatedNotificationHandler.Handle: an open
	// in-process session must be told about exactly the same widget a legacy client is pushed.
	[Test]
	public async Task WidgetUpdatedEvent_RaisesTheDataChangedRenderSignal_WithTheExactWidgetPushedToClients()
	{
		var renderSignals = new RecordingRenderSignals();
		var widget = PinnedWidget();
		widget.Data = """{"mode":"momentary","label":"changed"}""";
		var handler = new WidgetUpdatedNotificationHandler(_transport,
			_renderQueue,
			renderSignals,
			new RecordingUiSessionBroker());

		await handler.Handle(new WidgetUpdatedNotification(widget), CancellationToken.None);

		var pushed = _transport.Sent.OfType<WidgetUpdatedEvent>().Single().Widget!;
		Assert.That(renderSignals.DataChanges, Has.Count.EqualTo(1), "the render signal must fire once");

		var raised = renderSignals.DataChanges[0];
		Assert.Multiple(() =>
		{
			Assert.That(raised.Id.ToString(), Is.EqualTo(pushed.Id));
			Assert.That(raised.Data, Is.EqualTo(pushed.Data));
			Assert.That(raised.IsPinned, Is.EqualTo(pushed.IsPinned));
		});
	}

	[Test]
	public async Task WidgetCreatedEvent_CarriesThePinFlag()
	{
		var handler = new WidgetCreatedNotificationHandler(_transport);

		await handler.Handle(new WidgetCreatedNotification(PinnedWidget()), CancellationToken.None);

		var evt = _transport.Sent.OfType<WidgetCreatedEvent>().Single();
		Assert.That(evt.Widget!.IsPinned, Is.True);
	}

	[Test]
	public async Task WidgetPositionsUpdatedEvent_CarriesThePinFlag()
	{
		var handler = new WidgetPositionsUpdatedNotificationHandler(_transport, _renderQueue);
		var widget = PinnedWidget();

		await handler.Handle(new WidgetPositionsUpdatedNotification(widget.FolderId, [widget], []),
			CancellationToken.None);

		var evt = _transport.Sent.OfType<WidgetPositionsUpdatedEvent>().Single();
		Assert.That(evt.Widgets.Single().IsPinned, Is.True);
	}

	private static WidgetEntity PinnedWidget() => new()
	{
		Id = Guid.NewGuid(),
		FolderId = Guid.NewGuid(),
		Type = WidgetTypeIds.Clock,
		PositionX = 1,
		PositionY = 2,
		Width = 1,
		Height = 1,
		IsPinned = true
	};

	private sealed class RecordingTransport : IUiTransport
	{
		public Task SendToConnection<T>(string connectionId, T message, CancellationToken cancellationToken = default)
			where T : class
			=> Task.CompletedTask;

		public Task AddToGroup(string connectionId, string group, CancellationToken cancellationToken = default)
			=> Task.CompletedTask;

		public Task RemoveFromGroup(string connectionId, string group, CancellationToken cancellationToken = default)
			=> Task.CompletedTask;

		public List<object> Sent { get; } = [];

		public Task Send<T>(T message, CancellationToken cancellationToken = default)
			where T : class
		{
			Sent.Add(message);
			return Task.CompletedTask;
		}

		public Task SendToGroup<T>(string group, T message, CancellationToken cancellationToken = default)
			where T : class
		{
			Sent.Add(message);
			return Task.CompletedTask;
		}
	}
}
