using MacroDeckHost.Application.Notifications;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Notifications;
using MacroDeckHost.Infrastructure.BackgroundServices;
using MacroDeckHost.Infrastructure.Notifications;

namespace MacroDeckHost.Tests.UnitTests.Notifications;

public class UserNotificationBroadcastBackgroundServiceTests
{
	private UserNotificationStore _store = null!;
	private RecordingTransport _transport = null!;
	private UserNotificationBroadcastBackgroundService _service = null!;

	[SetUp]
	public void SetUp()
	{
		_store = new UserNotificationStore(100);
		_transport = new RecordingTransport();
		_service = new UserNotificationBroadcastBackgroundService(_store, _transport);
	}

	[TearDown]
	public async Task TearDown()
	{
		await Task.Run(() => _service.StopAsync(CancellationToken.None));
		_service.Dispose();
	}

	[Test]
	public async Task A_Burst_Of_Raises_Coalesces_Into_One_Push()
	{
		await StartService();

		for (var i = 0; i < 5; i++)
		{
			_store.Raise(Draft($"entry-{i}"));
		}

		await WaitForPushes(1);
		await WaitPastTheCoalesceWindow();

		Assert.That(_transport.Pushes, Has.Count.EqualTo(1));
	}

	[Test]
	public async Task The_Push_Lands_In_The_Admin_Notifications_Group_And_Never_In_A_Plain_Broadcast()
	{
		await StartService();

		_store.Raise(Draft("one"));
		await WaitForPushes(1);

		Assert.Multiple(() =>
		{
			Assert.That(_transport.Groups, Has.All.EqualTo(UiAdminGroups.Admin));
			Assert.That(_transport.BroadcastCount, Is.EqualTo(0));
		});
	}

	[Test]
	public async Task The_Payload_Is_The_Full_Snapshot()
	{
		await StartService();

		_store.Raise(Draft("one"));
		_store.Raise(Draft("two"));
		await WaitForPushes(1);
		await WaitPastTheCoalesceWindow();

		var lastPush = _transport.Pushes[^1];
		var expectedTitles = _store.Snapshot().Select(e => e.Title).ToList();

		Assert.That(lastPush.Notifications.Select(n => n.Title), Is.EqualTo(expectedTitles));
	}

	[Test]
	public async Task A_Transport_That_Throws_Is_Swallowed()
	{
		_transport.ThrowOnSend = true;
		await StartService();

		_store.Raise(Draft("one"));

		await WaitPastTheCoalesceWindow();
	}

	[Test]
	public async Task Nothing_Is_Sent_When_Nothing_Changed()
	{
		await StartService();

		await WaitPastTheCoalesceWindow();

		Assert.That(_transport.Pushes, Is.Empty);
	}

	private Task StartService() => Task.Run(() => _service.StartAsync(CancellationToken.None));

	private static Task WaitPastTheCoalesceWindow() => Task.Delay(TimeSpan.FromMilliseconds(600));

	private async Task WaitForPushes(int expected)
	{
		for (var attempt = 0; attempt < 60 && _transport.Pushes.Count < expected; attempt++)
		{
			await Task.Delay(50);
		}
	}

	private static UserNotificationDraft Draft(string title)
		=> new()
		{
			Severity = UserNotificationSeverity.Info,
			Kind = UserNotificationKind.General,
			Title = title
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

		private readonly object _lock = new();

		private readonly List<UserNotificationsChangedEvent> _pushes = [];
		private readonly List<string> _groups = [];

		public bool ThrowOnSend { get; set; }

		public int BroadcastCount { get; private set; }

		public List<UserNotificationsChangedEvent> Pushes
		{
			get
			{
				lock (_lock)
				{
					return _pushes.ToList();
				}
			}
		}

		public List<string> Groups
		{
			get
			{
				lock (_lock)
				{
					return _groups.ToList();
				}
			}
		}

		public Task Send<T>(T message, CancellationToken cancellationToken = default)
			where T : class
		{
			lock (_lock)
			{
				BroadcastCount++;
			}

			return Task.CompletedTask;
		}

		public Task SendToGroup<T>(string group, T message, CancellationToken cancellationToken = default)
			where T : class
		{
			if (ThrowOnSend)
			{
				throw new InvalidOperationException("transport failure");
			}

			lock (_lock)
			{
				_groups.Add(group);
				if (message is UserNotificationsChangedEvent notificationsChanged)
				{
					_pushes.Add(notificationsChanged);
				}
			}

			return Task.CompletedTask;
		}
	}
}
