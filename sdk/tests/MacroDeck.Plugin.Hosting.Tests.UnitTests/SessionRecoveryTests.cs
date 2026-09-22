using MacroDeck.Plugin.Hosting.Tests.UnitTests.Support;
using MacroDeck.Plugin.Hosting.Transport;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Limits;
using Microsoft.Extensions.DependencyInjection;
using MacroDeck.Plugin.Testing;

namespace MacroDeck.Plugin.Hosting.Tests.UnitTests;

[TestFixture]
public class SessionRecoveryTests
{
	private static readonly TimeSpan _shortStep = TimeSpan.FromMilliseconds(50);
	private static readonly TimeSpan _longStep = TimeSpan.FromSeconds(1);
	private static readonly TimeSpan _pumpBudget = TimeSpan.FromSeconds(30);

	private FakePluginHost _host = null!;
	private string _stateDirectory = string.Empty;

	[SetUp]
	public async Task SetUp()
	{
		_host = await FakePluginHost.StartAsync();
		_stateDirectory = Directory.CreateTempSubdirectory("macro-deck-plugin-session-recovery").FullName;
	}

	[TearDown]
	public async Task TearDown()
	{
		await _host.DisposeAsync();
		Directory.Delete(_stateDirectory, recursive: true);
	}

	[Test]
	public async Task Each_host_restart_is_followed_by_a_new_session()
	{
		var time = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
		await using var plugin = Builder(time).Build();
		await plugin.StartAsync();

		var state = plugin.Services.GetRequiredService<PluginConnectionState>();
		await Wait.UntilAsync(() => state.IsReady);

		for (var restart = 1; restart <= new PluginHostOptions().MaxAuthenticationFailures + 1; restart++)
		{
			var sessions = restart + 1;
			_host.Restart();

			await PumpUntilAsync(time, _shortStep, () => state.IsReady && _host.Sessions.Count == sessions);

			Assert.That(LastHello(),
				Is.EqualTo(($"session-{sessions}", (string?)null)),
				$"restart {restart}: a new session, attached without presenting the forgotten one");
		}

		Assert.That(state.Status, Is.EqualTo(PluginConnectionStatus.Connected));
	}

	[TestCase(false, TestName = "Failed upgrades do not keep the resume window open")]
	[TestCase(true, TestName = "Refused handshakes do not keep the resume window open")]
	public async Task Attempts_that_fail_past_the_resume_window_end_in_a_new_session(bool refuseHandshake)
	{
		var time = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
		await using var plugin = Builder(time).Build();
		await plugin.StartAsync();

		var state = plugin.Services.GetRequiredService<PluginConnectionState>();
		await Wait.UntilAsync(() => state.IsReady);

		SetOutage(refuseHandshake, true);
		_host.DropCurrentConnection();
		var droppedAt = time.GetUtcNow();

		await PumpUntilAsync(time,
			_longStep,
			() => time.GetUtcNow() - droppedAt > ProtocolTimeouts.SessionResumeWindow + _longStep &&
				state.ReconnectAttempt >= 3);

		SetOutage(refuseHandshake, false);
		await PumpUntilAsync(time, _shortStep, () => state.IsReady);

		Assert.Multiple(() =>
		{
			Assert.That(_host.Sessions, Has.Count.EqualTo(2));
			Assert.That(LastHello(), Is.EqualTo(("session-2", (string?)null)));
		});
	}

	[Test]
	public async Task A_session_whose_socket_never_opened_is_attached_once_the_socket_does()
	{
		var time = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
		_host.WebSocketUnavailable = true;

		await using var plugin = Builder(time).Build();
		await plugin.StartAsync();

		var state = plugin.Services.GetRequiredService<PluginConnectionState>();
		await PumpUntilAsync(time, _shortStep, () => state.ReconnectAttempt >= 3);

		_host.WebSocketUnavailable = false;
		await PumpUntilAsync(time, _shortStep, () => state.IsReady);

		Assert.Multiple(() =>
		{
			Assert.That(_host.Sessions, Has.Count.EqualTo(1));
			Assert.That(LastHello(), Is.EqualTo(("session-1", (string?)null)));
		});
	}

	[Test]
	public async Task A_host_restart_before_the_first_socket_opens_is_followed_by_a_new_session()
	{
		var time = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
		_host.WebSocketUnavailable = true;

		await using var plugin = Builder(time).Build();
		await plugin.StartAsync();

		var state = plugin.Services.GetRequiredService<PluginConnectionState>();
		await PumpUntilAsync(time, _shortStep, () => state.ReconnectAttempt >= 2);

		_host.Restart();
		_host.WebSocketUnavailable = false;
		await PumpUntilAsync(time, _shortStep, () => state.IsReady);

		Assert.Multiple(() =>
		{
			Assert.That(_host.Sessions, Has.Count.EqualTo(2));
			Assert.That(LastHello(), Is.EqualTo(("session-2", (string?)null)));
		});
	}

	[Test]
	public async Task A_drop_inside_the_resume_window_resumes_the_same_session()
	{
		var time = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
		await using var plugin = Builder(time).Build();
		await plugin.StartAsync();

		var state = plugin.Services.GetRequiredService<PluginConnectionState>();
		await Wait.UntilAsync(() => state.IsReady);

		_host.DropCurrentConnection();
		await PumpUntilAsync(time, _shortStep, () => state.IsReady && _host.Connections == 2);

		Assert.Multiple(() =>
		{
			Assert.That(_host.Sessions, Has.Count.EqualTo(1));
			Assert.That(LastHello(), Is.EqualTo(("session-1", (string?)"session-1")));
		});
	}

	[Test]
	public async Task A_host_that_refuses_every_new_session_token_faults_the_plugin()
	{
		var time = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
		_host.RejectEveryUpgrade = true;

		await using var plugin = Builder(time).Build();
		await plugin.StartAsync();

		var state = plugin.Services.GetRequiredService<PluginConnectionState>();
		await PumpUntilAsync(time, _shortStep, () => state.Status == PluginConnectionStatus.Faulted);

		Assert.That(_host.Sessions,
			Has.Count.EqualTo(new PluginHostOptions().MaxAuthenticationFailures + 1),
			"one session per tolerated failure plus the one that exhausts the budget");
	}

	[Test]
	public async Task A_welcomed_connection_clears_the_refused_token_budget()
	{
		var budget = new PluginHostOptions().MaxAuthenticationFailures;
		var time = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
		_host.RejectEveryUpgrade = true;

		await using var plugin = Builder(time).Build();
		await plugin.StartAsync();

		var state = plugin.Services.GetRequiredService<PluginConnectionState>();
		await PumpUntilAsync(time, _shortStep, () => _host.Sessions.Count == budget - 1);

		_host.RejectEveryUpgrade = false;
		await PumpUntilAsync(time, _shortStep, () => state.IsReady);
		var welcomedAfter = _host.Sessions.Count;

		_host.RejectEveryUpgrade = true;
		_host.Restart();
		await PumpUntilAsync(time, _shortStep, () => state.Status == PluginConnectionStatus.Faulted);

		Assert.That(_host.Sessions.Count - welcomedAfter,
			Is.EqualTo(budget + 1),
			"the budget starts again after the welcome, so the earlier refusals do not shorten it");
	}

	[Test]
	public async Task Repeated_authentication_failures_on_a_welcomed_socket_fault_the_plugin()
	{
		var budget = new PluginHostOptions().MaxAuthenticationFailures;
		var time = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
		await using var plugin = Builder(time).Build();
		await plugin.StartAsync();

		var state = plugin.Services.GetRequiredService<PluginConnectionState>();

		for (var failure = 1; failure <= budget + 1; failure++)
		{
			await PumpUntilAsync(time, _shortStep, () => state.IsReady);
			await _host.CloseCurrentConnectionAsync(ProtocolCloseCodes.AuthenticationFailed,
				"The session token is no longer valid.");

			if (failure <= budget)
			{
				await PumpUntilAsync(time, _shortStep, () => !state.IsReady);
			}
		}

		await PumpUntilAsync(time, _shortStep, () => state.Status == PluginConnectionStatus.Faulted);
		Assert.That(state.FaultReason, Is.Not.Null);
	}

	private (string SessionId, string? ResumeSessionId) LastHello()
	{
		var hello = _host.Hellos.Last();
		return (hello.SessionId, hello.ResumeSessionId);
	}

	private void SetOutage(bool refuseHandshake, bool on)
	{
		if (refuseHandshake)
		{
			_host.RefuseHandshake = on;
		}
		else
		{
			_host.WebSocketUnavailable = on;
		}
	}

	private static Task PumpUntilAsync(ManualTimeProvider time, TimeSpan step, Func<bool> condition)
		=> Wait.UntilAsync(() =>
			{
				time.Advance(step);
				return condition();
			},
			_pumpBudget);

	private PluginHostBuilder Builder(TimeProvider time)
	{
		var builder = MacroDeckPlugin.CreatePlugin()
			.RegisterIntegration(_ => new TestIntegration())
			.ConfigureServices((_, services) => services.AddSingleton(time));

		builder.Configuration["MacroDeck:Plugin:HostUrl"] = _host.Url;
		builder.Configuration["MacroDeck:Plugin:StateDirectory"] = _stateDirectory;
		builder.Configuration["MacroDeck:Plugin:EnrollmentToken"] = "enrollment-token";

		return builder;
	}
}
