using MacroDeck.Plugin.Hosting.Tests.UnitTests.Support;
using MacroDeck.Plugin.Hosting.Transport;
using Microsoft.Extensions.DependencyInjection;
using MacroDeck.Plugin.Testing;

namespace MacroDeck.Plugin.Hosting.Tests.UnitTests;

/// <summary>
/// What the reconnect loop schedules, against a host that really drops the socket.
///
/// <para>
/// Asserted through <see cref="PluginConnectionState.ReconnectAttempt" /> - the number
/// <c>GET /_macrodeck/diagnostics</c> reports and the number
/// <see cref="MacroDeck.Plugin.Protocol.Reconnection.ReconnectPolicy.DelayFor" /> is asked for - rather
/// than through the delay itself. Full jitter means a single delay is a sample from <c>[0, cap]</c>, so
/// a drawn value can be anything from near zero upwards whatever the schedule says; the attempt number
/// is the schedule.
/// </para>
/// </summary>
[TestFixture]
public class ReconnectSchedulingTests
{
	/// <summary>
	/// How far a pumping loop advances the clock on each poll.
	///
	/// <para>
	/// Large enough that <see cref="Wait.UntilAsync" />'s own ten-second budget, polling every twenty
	/// milliseconds, can still outrun a near-maximum full-jitter draw for the attempts this fixture
	/// waits through: a one-millisecond step cannot, since even a few hundred polls at one millisecond
	/// each falls short of the first attempt's own one-second cap, let alone the two attempts'
	/// combined cap that <see cref="Attempts_that_never_reach_a_session_keep_backing_off" /> waits
	/// through. Small enough that the cumulative advance across that same budget stays well short of
	/// the keep-alive timeout and the resume window, a minute each, so neither fires as a side effect
	/// of pumping the clock.
	/// </para>
	/// </summary>
	private static readonly TimeSpan _clockStep = TimeSpan.FromMilliseconds(50);

	private FakePluginHost _host = null!;
	private string _stateDirectory = string.Empty;

	[SetUp]
	public async Task SetUp()
	{
		_host = await FakePluginHost.StartAsync();
		_stateDirectory = Directory.CreateTempSubdirectory("macro-deck-plugin-reconnect").FullName;
	}

	[TearDown]
	public async Task TearDown()
	{
		await _host.DisposeAsync();
		Directory.Delete(_stateDirectory, recursive: true);
	}

	[Test]
	public async Task A_drop_after_a_successful_session_retries_from_the_initial_delay()
	{
		var time = new ManualTimeProvider(DateTimeOffset.UnixEpoch);

		await using var plugin = Builder(time).Build();
		await plugin.StartAsync();

		var state = plugin.Services.GetRequiredService<PluginConnectionState>();
		await Wait.UntilAsync(() => state.IsReady);

		// A first blip, early in the plugin's life.
		_host.DropCurrentConnection();
		await Wait.UntilAsync(() => state.ReconnectAttempt != 0);

		Assert.That(state.ReconnectAttempt,
			Is.EqualTo(1),
			"the first retry after a session is the first attempt");

		await ReconnectAsync(time, state, connections: 2);

		// A second blip, an arbitrary amount of healthy running later. The schedule must not remember
		// the first one: the session that opened in between is what says the trouble is over.
		_host.DropCurrentConnection();
		await Wait.UntilAsync(() => state.ReconnectAttempt != 0);

		Assert.That(state.ReconnectAttempt,
			Is.EqualTo(1),
			"a healthy session resets the backoff, so this retry is scheduled at the initial delay and " +
			"not further along the curve towards the 30 s cap");
	}

	[Test]
	public async Task Attempts_that_never_reach_a_session_keep_backing_off()
	{
		var time = new ManualTimeProvider(DateTimeOffset.UnixEpoch);

		await using var plugin = Builder(time).Build();
		await plugin.StartAsync();

		var state = plugin.Services.GetRequiredService<PluginConnectionState>();
		await Wait.UntilAsync(() => state.IsReady);

		// From here the host answers the upgrade and then abandons the socket. Reaching a socket is not
		// reaching a session, so these attempts must climb the curve rather than retry at the initial
		// delay forever - which is what a reset on every returned attempt, rather than on a welcome,
		// would do.
		_host.RefuseHandshake = true;
		_host.DropCurrentConnection();

		await Wait.UntilAsync(() =>
		{
			time.Advance(_clockStep);
			return state.ReconnectAttempt >= 3;
		});

		Assert.That(state.ReconnectAttempt, Is.GreaterThanOrEqualTo(3));
	}

	/// <summary>
	/// Runs the clock forward in <see cref="_clockStep" /> steps until the plugin is connected again.
	///
	/// <para>
	/// Stepping rather than advancing once, because the reconnect delay is registered on the clock a
	/// moment after the attempt is scheduled: a single advance can land in between and find no timer to
	/// fire.
	/// </para>
	/// </summary>
	private Task ReconnectAsync(ManualTimeProvider time, PluginConnectionState state, int connections)
		=> Wait.UntilAsync(() =>
		{
			time.Advance(_clockStep);
			return state.IsReady && _host.Connections == connections;
		});

	private PluginHostBuilder Builder(TimeProvider time)
	{
		var builder = MacroDeckPlugin.CreatePlugin()
			.RegisterIntegration(_ => new TestIntegration())
			.ConfigureServices((_, services) =>
			{
				// Registered after the SDK's own TryAddSingleton, so this is the one that resolves.
				services.AddSingleton(time);
			});

		builder.Configuration["MacroDeck:Plugin:HostUrl"] = _host.Url;
		builder.Configuration["MacroDeck:Plugin:StateDirectory"] = _stateDirectory;
		builder.Configuration["MacroDeck:Plugin:EnrollmentToken"] = "enrollment-token";

		return builder;
	}
}
