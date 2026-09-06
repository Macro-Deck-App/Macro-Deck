using System.Collections.Concurrent;
using MacroDeckHost.Integrations.Twitch.Protocol;
using Serilog;
using Serilog.Core;

namespace MacroDeckHost.Tests.UnitTests.Twitch;

[TestFixture]
internal sealed class TwitchEventSubSessionTests
{
	private static readonly string[] _firstSession = ["session-1"];
	private static readonly bool[] _connectedOnce = [true];
	private static readonly bool[] _reconnectedOnce = [true, false, true];
	private static readonly string[] _twoMessageIds = ["msg-1", "msg-2"];

	private ConcurrentQueue<FakeEventSubClient> _clients = null!;
	private List<string> _subscribedSessions = null!;
	private List<TwitchEventSubMessage> _notifications = null!;
	private List<(string Type, string Status)> _revocations = null!;
	private List<bool> _connectionChanges = null!;
	private List<string> _authorizationLost = null!;
	private List<TimeSpan> _delays = null!;
	private DateTimeOffset _now;
	private TwitchEventSubSession? _session;

	[SetUp]
	public void SetUp()
	{
		_clients = new ConcurrentQueue<FakeEventSubClient>();
		_subscribedSessions = [];
		_notifications = [];
		_revocations = [];
		_connectionChanges = [];
		_authorizationLost = [];
		_delays = [];
		_now = new DateTimeOffset(2026, 7, 30, 12, 0, 0, TimeSpan.Zero);
	}

	[TearDown]
	public async Task TearDown()
	{
		if (_session is null)
		{
			return;
		}

		_session.Dispose();
		try
		{
			await _session.Completion.WaitAsync(TimeSpan.FromSeconds(5));
		}
		catch (TimeoutException)
		{
		}
	}

	[Test]
	public async Task A_welcomed_session_subscribes_once_with_its_session_id()
	{
		var client = Enqueue();
		Start();

		await WaitFor(() => _subscribedSessions.Count == 1);

		Assert.Multiple(() =>
		{
			Assert.That(_subscribedSessions, Is.EqualTo(_firstSession));
			Assert.That(_connectionChanges, Is.EqualTo(_connectedOnce));
			Assert.That(client.ConnectedTo!.ToString(), Does.Contain("eventsub.wss.twitch.tv"));
		});
	}

	[Test]
	public async Task A_reconnect_swaps_before_it_closes_and_does_not_resubscribe()
	{
		var first = Enqueue();
		var second = Enqueue();
		Start();
		await WaitFor(() => _subscribedSessions.Count == 1);

		first.Raise(FakeEventSubClient.Reconnect("wss://eventsub.wss.twitch.tv/ws?reconnect=1"));
		await WaitFor(() => second.ConnectedTo is not null);

		// Disposed, not DisconnectCount: retiring the old socket disconnects it, drops it from the owned
		// set under the lock and only then disposes it, so waiting on the disconnect and asserting the
		// dispose is a race the assertion loses on a loaded machine.
		await WaitFor(() => first.Disposed, "the retired socket is closed only after the new welcome");

		Assert.Multiple(() =>
		{
			Assert.That(_subscribedSessions, Has.Count.EqualTo(1), "Twitch carries the subscriptions over itself");
			Assert.That(second.ConnectedTo!.ToString(), Does.Contain("reconnect=1"));
			Assert.That(_connectionChanges, Is.EqualTo(_connectedOnce), "the session never went down");
		});
	}

	[Test]
	public async Task A_reconnect_that_cannot_be_followed_falls_back_to_a_fresh_session()
	{
		var first = Enqueue();
		var failing = Enqueue();
		failing.ConnectFailure = new TwitchEventSubException("reconnect url refused");
		var third = Enqueue();
		Start();
		await WaitFor(() => _subscribedSessions.Count == 1);

		first.Raise(FakeEventSubClient.Reconnect("wss://eventsub.wss.twitch.tv/ws?reconnect=1"));

		await WaitFor(() => _subscribedSessions.Count == 2);
		Assert.That(third.ConnectedTo!.ToString(), Does.Contain("eventsub.wss.twitch.tv/ws?keepalive"));
	}

	[Test]
	public async Task A_revoked_authorization_stops_the_session_for_good()
	{
		var client = Enqueue();
		Enqueue();
		Start();
		await WaitFor(() => _subscribedSessions.Count == 1);

		client.Raise(FakeEventSubClient.Revocation("channel.follow", "authorization_revoked"));

		await _session!.Completion.WaitAsync(TimeSpan.FromSeconds(5));
		Assert.Multiple(() =>
		{
			Assert.That(_authorizationLost, Has.Count.EqualTo(1));
			Assert.That(_subscribedSessions, Has.Count.EqualTo(1), "reconnecting cannot fix a revoked token");
			Assert.That(_revocations, Is.EqualTo(new[] { ("channel.follow", "authorization_revoked") }));
		});
	}

	[Test]
	public async Task A_removed_subscription_version_does_not_stop_the_session()
	{
		var client = Enqueue();
		Start();
		await WaitFor(() => _subscribedSessions.Count == 1);

		client.Raise(FakeEventSubClient.Revocation("channel.follow", "version_removed"));
		client.Raise(FakeEventSubClient.Notification("msg-1", "stream.online"));

		await WaitFor(() => _notifications.Count == 1);
		Assert.Multiple(() =>
		{
			Assert.That(_authorizationLost, Is.Empty);
			Assert.That(_revocations, Is.EqualTo(new[] { ("channel.follow", "version_removed") }));
		});
	}

	[Test]
	public async Task A_repeated_message_id_is_delivered_once()
	{
		var client = Enqueue();
		Start();
		await WaitFor(() => _subscribedSessions.Count == 1);

		client.Raise(FakeEventSubClient.Notification("msg-1", "channel.follow"));
		client.Raise(FakeEventSubClient.Notification("msg-1", "channel.follow"));
		client.Raise(FakeEventSubClient.Notification("msg-2", "channel.follow"));

		await WaitFor(() => _notifications.Count == 2);
		Assert.That(_notifications.Select(n => n.MessageId), Is.EqualTo(_twoMessageIds));
	}

	[Test]
	public async Task A_replay_older_than_the_cutoff_is_dropped()
	{
		var client = Enqueue();
		Start();
		await WaitFor(() => _subscribedSessions.Count == 1);

		client.Raise(FakeEventSubClient.Notification("old", "channel.follow", timestamp: _now.AddMinutes(-30)));
		client.Raise(FakeEventSubClient.Notification("fresh", "channel.follow", timestamp: _now));

		await WaitFor(() => _notifications.Count == 1);
		Assert.That(_notifications[0].MessageId, Is.EqualTo("fresh"));
	}

	[Test]
	public async Task Silence_past_two_keepalives_reconnects()
	{
		Enqueue();
		Enqueue();
		Start();
		await WaitFor(() => _subscribedSessions.Count == 1);

		_now = _now.AddMinutes(5);

		await WaitFor(() => _subscribedSessions.Count == 2);
		Assert.That(_connectionChanges, Is.EqualTo(_reconnectedOnce));
	}

	[Test]
	public async Task A_keepalive_holds_the_watchdog_off()
	{
		var client = Enqueue();
		Enqueue();
		Start();
		await WaitFor(() => _subscribedSessions.Count == 1);

		for (var i = 0; i < 20; i++)
		{
			_now = _now.AddSeconds(20);
			client.Raise(FakeEventSubClient.Keepalive());
			await Task.Yield();
		}

		Assert.That(_subscribedSessions, Has.Count.EqualTo(1));
	}

	[Test]
	public async Task A_failed_connect_backs_off_and_a_welcome_resets_it()
	{
		for (var i = 0; i < 3; i++)
		{
			Enqueue().ConnectFailure = new TwitchEventSubException("refused");
		}

		Enqueue();
		Start();

		await WaitFor(() => _subscribedSessions.Count == 1);

		List<TimeSpan> backoff;
		lock (_delays)
		{
			backoff = _delays.Take(3).ToList();
		}

		Assert.Multiple(() =>
		{
			Assert.That(backoff, Has.Count.EqualTo(3));
			Assert.That(backoff[0], Is.EqualTo(TimeSpan.FromSeconds(5)));
			Assert.That(backoff[1], Is.EqualTo(TimeSpan.FromSeconds(10)), "the backoff doubles while nothing connects");
			Assert.That(backoff[2], Is.EqualTo(TimeSpan.FromSeconds(20)));
		});
	}

	[Test]
	public async Task A_dropped_session_reconnects_without_backing_off()
	{
		var client = Enqueue();
		Enqueue();
		Start();
		await WaitFor(() => _subscribedSessions.Count == 1);
		_delays.Clear();

		client.Drop("4000");

		await WaitFor(() => _subscribedSessions.Count == 2);

		List<TimeSpan> waits;
		lock (_delays)
		{
			waits = [.. _delays];
		}

		Assert.Multiple(() =>
		{
			Assert.That(waits, Does.Contain(TimeSpan.FromSeconds(5)));

			Assert.That(waits, Does.Not.Contain(TimeSpan.FromSeconds(10)));
		});
	}

	private FakeEventSubClient Enqueue()
	{
		var client = new FakeEventSubClient();
		_clients.Enqueue(client);
		return client;
	}

	private void Start()
	{
		// Captured into locals so a session that outlives its test cannot append to the next one's
		// lists - the fields are replaced in SetUp, the closures are not.
		var subscribed = _subscribedSessions;
		var notifications = _notifications;
		var revocations = _revocations;
		var connectionChanges = _connectionChanges;
		var authorizationLost = _authorizationLost;
		var delays = _delays;

		_session = new TwitchEventSubSession(
			() => _clients.TryDequeue(out var client) ? client : new FakeEventSubClient(),
			new TwitchEventSubCallbacks((sessionId, _) =>
				{
					lock (subscribed)
					{
						subscribed.Add(sessionId);
					}

					return Task.CompletedTask;
				},
				message =>
				{
					lock (notifications)
					{
						notifications.Add(message);
					}
				},
				(type, status) =>
				{
					lock (revocations)
					{
						revocations.Add((type, status));
					}
				},
				connected =>
				{
					lock (connectionChanges)
					{
						connectionChanges.Add(connected);
					}
				},
				reason =>
				{
					lock (authorizationLost)
					{
						authorizationLost.Add(reason);
					}
				}),
			SilentLogger(),
			new TwitchEventSubOptions
			{
				Delay = (delay, _) =>
				{
					lock (delays)
					{
						delays.Add(delay);
					}

					return Task.Delay(1, CancellationToken.None);
				},
				Now = () => _now
			});

		_session.Start();
	}

	private static async Task WaitFor(Func<bool> condition, string? because = null)
	{
		var deadline = DateTime.UtcNow.AddSeconds(5);
		while (DateTime.UtcNow < deadline)
		{
			if (condition())
			{
				return;
			}

			await Task.Delay(5);
		}

		Assert.Fail(because ?? "the expected state was never reached");
	}

	private static Logger SilentLogger() => new LoggerConfiguration().CreateLogger();
}
