using MacroDeck.Sdk.Notifications;
using MacroDeckHost.Application.Notifications;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Notifications;
using MacroDeckHost.Infrastructure.BackgroundServices;
using MacroDeckHost.Infrastructure.Notifications;
using MacroDeckHost.Tests.UnitTests.TestSupport;

namespace MacroDeckHost.Tests.UnitTests.Notifications;

public class NotificationBroadcastBackgroundServiceTests
{
	private static readonly NotificationOwner
		_plugin = new(NotificationSourceCategory.Plugin, "com.example", "Example");

	private static readonly NotificationPrincipal _admin = new(true, null);

	private NotificationManager _manager = null!;
	private CapturingTransport _transport = null!;
	private NotificationBroadcastBackgroundService _service = null!;

	[SetUp]
	public async Task SetUp()
	{
		_manager = new NotificationManager(new UserNotificationStore(),
			TestLocalization.Resolver,
			TestLocalization.ScopeFactory,
			TimeProvider.System,
			Serilog.Core.Logger.None);
		_transport = new CapturingTransport();
		_service = new NotificationBroadcastBackgroundService(_manager, _transport, TimeProvider.System);
		await _service.StartAsync(CancellationToken.None);
	}

	[TearDown]
	public async Task TearDown()
	{
		await _service.StopAsync(CancellationToken.None);
		_service.Dispose();
		_manager.Dispose();
	}

	[Test]
	public async Task A_notification_reaches_exactly_the_clients_it_targets()
	{
		var device = Guid.NewGuid();
		await _manager.ShowAsync(_plugin, Dialog() with { Target = NotificationTarget.All });
		await _manager.ShowAsync(_plugin, Dialog() with { Target = NotificationTarget.AdminUi });
		await _manager.ShowAsync(_plugin, Dialog() with { Target = NotificationTarget.Devices(device) });

		Assert.That(() => _transport.Sent.Count, Is.EqualTo(3).After(2000, 10));
		var routes = _transport.Sent.Select(s => s.Route).ToList();
		Assert.That(routes, Is.EquivalentTo(new[] { "all", UiAdminGroups.Admin, UiDeviceGroups.For(device) }));
	}

	[Test]
	public async Task A_popup_closed_while_updates_are_pending_is_announced_at_once_and_its_updates_are_dropped()
	{
		var shown = await _manager.ShowAsync(_plugin, Dialog());
		Assert.That(() => _transport.Sent.Count, Is.EqualTo(1).After(2000, 10));

		for (var i = 0; i < 5; i++)
		{
			await _manager.UpdateAsync(_plugin, shown.Id, new NotificationUpdate { Message = $"step {i}" });
		}

		_manager.Respond(_admin, shown.Id, "ok", null);

		Assert.That(() => _transport.Sent.Any(s => s.Message is NotificationClosedEvent), Is.True.After(150, 10));
		await Task.Delay(400);
		Assert.That(_transport.Sent.Any(s => s.Message is NotificationUpdatedEvent), Is.False);
	}

	[Test]
	public async Task A_burst_of_updates_goes_out_as_one_message_with_the_latest_revision()
	{
		var shown = await _manager.ShowAsync(_plugin, Dialog());

		for (var i = 0; i < 10; i++)
		{
			await _manager.UpdateAsync(_plugin, shown.Id, new NotificationUpdate { Message = $"step {i}" });
		}

		Assert.That(() => _transport.Sent.Count(s => s.Message is NotificationUpdatedEvent),
			Is.EqualTo(1).After(2000, 10));
		var updated = (NotificationUpdatedEvent)_transport.Sent.Single(s => s.Message is NotificationUpdatedEvent)
			.Message;
		Assert.That(updated.Notification.Revision, Is.EqualTo(_manager.Find(shown.Id)!.Revision));
	}

	private static NotificationOptions Dialog()
	{
		return new NotificationOptions
		{
			Title = "Question",
			Popup = NotificationPopup.Dialog,
			Actions = [new NotificationAction { Id = "ok", Label = "OK" }]
		};
	}

	private sealed class CapturingTransport : IUiTransport
	{
		private readonly Lock _gate = new();
		private readonly List<(string Route, object Message)> _sent = [];

		public IReadOnlyList<(string Route, object Message)> Sent
		{
			get
			{
				lock (_gate)
				{
					return [.. _sent];
				}
			}
		}

		public Task Send<T>(T message, CancellationToken cancellationToken = default)
			where T : class
		{
			return Record("all", message);
		}

		public Task SendToGroup<T>(string group, T message, CancellationToken cancellationToken = default)
			where T : class
		{
			return Record(group, message);
		}

		public Task SendToConnection<T>(string connectionId, T message, CancellationToken cancellationToken = default)
			where T : class
		{
			return Record(connectionId, message);
		}

		public Task AddToGroup(string connectionId, string group, CancellationToken cancellationToken = default)
		{
			return Task.CompletedTask;
		}

		public Task RemoveFromGroup(string connectionId, string group, CancellationToken cancellationToken = default)
		{
			return Task.CompletedTask;
		}

		private Task Record(string route, object message)
		{
			lock (_gate)
			{
				_sent.Add((route, message));
			}

			return Task.CompletedTask;
		}
	}
}
