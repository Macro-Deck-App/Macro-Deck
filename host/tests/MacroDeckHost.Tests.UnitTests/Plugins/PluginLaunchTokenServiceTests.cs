using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Plugin.Protocol.Versioning;
using MacroDeckHost.Application.Plugins;
using MacroDeckHost.Tests.UnitTests.Auth;

namespace MacroDeckHost.Tests.UnitTests.Plugins;

[TestFixture]
public class PluginLaunchTokenServiceTests
{
	private ManualTimeProvider _time = null!;
	private PluginSessionRegistry _sessions = null!;
	private PluginLaunchTokenService _service = null!;

	[SetUp]
	public void SetUp()
	{
		_time = new ManualTimeProvider();
		_sessions = new PluginSessionRegistry(_time, Serilog.Core.Logger.None);
		_service = new PluginLaunchTokenService(_time, _sessions);
	}

	[Test]
	public void Mint_Then_Acquire_Succeeds()
	{
		var raw = _service.Mint("com.example.plugin", "launch-1", "Test Plugin", "1.0.0");

		var acquired = _service.TryAcquire("com.example.plugin", raw, out var launch);

		Assert.Multiple(() =>
		{
			Assert.That(acquired, Is.True);
			Assert.That(launch!.PluginId, Is.EqualTo("com.example.plugin"));
			Assert.That(launch.LaunchId, Is.EqualTo("launch-1"));
		});
	}

	[Test]
	public void An_acquired_launch_carries_the_name_and_version_it_was_minted_with()
	{
		var raw = _service.Mint("com.example.plugin", "launch-1", "Weather Widget", "2.3.1");

		var acquired = _service.TryAcquire("com.example.plugin", raw, out var launch);

		Assert.Multiple(() =>
		{
			Assert.That(acquired, Is.True);
			Assert.That(launch!.DisplayName, Is.EqualTo("Weather Widget"));
			Assert.That(launch.Version, Is.EqualTo("2.3.1"));
		});
	}

	[Test]
	public async Task N_Parallel_Acquires_Of_The_Same_Token_Yield_Exactly_One_Success()
	{
		var raw = _service.Mint("com.example.plugin", "launch-1", "Test Plugin", "1.0.0");

		var results = await Task.WhenAll(Enumerable.Range(0, 32)
			.Select(i => Task.Run(() => _service.TryAcquire("com.example.plugin", raw, out _))));

		Assert.That(results.Count(success => success), Is.EqualTo(1));
	}

	[Test]
	public void Acquiring_While_A_Session_Is_Live_Fails()
	{
		var raw = _service.Mint("com.example.plugin", "launch-1", "Test Plugin", "1.0.0");
		Assert.That(_service.TryAcquire("com.example.plugin", raw, out _), Is.True);

		Assert.That(_service.TryAcquire("com.example.plugin", raw, out _), Is.False);
	}

	[Test]
	public void After_Release_The_Same_Token_Can_Be_Acquired_Again()
	{
		var raw = _service.Mint("com.example.plugin", "launch-1", "Test Plugin", "1.0.0");
		Assert.That(_service.TryAcquire("com.example.plugin", raw, out _), Is.True);

		_service.Release("launch-1");

		Assert.That(_service.TryAcquire("com.example.plugin", raw, out _), Is.True);
	}

	[Test]
	public void Expired_Unused_Token_Fails()
	{
		var raw = _service.Mint("com.example.plugin", "launch-1", "Test Plugin", "1.0.0");

		_time.Advance(TimeSpan.FromMinutes(3));

		Assert.That(_service.TryAcquire("com.example.plugin", raw, out _), Is.False);
	}

	[Test]
	public void A_Token_Already_Used_Once_Survives_Its_Original_Expiry()
	{
		var raw = _service.Mint("com.example.plugin", "launch-1", "Test Plugin", "1.0.0");
		Assert.That(_service.TryAcquire("com.example.plugin", raw, out _), Is.True);
		_service.Release("launch-1");

		_time.Advance(TimeSpan.FromMinutes(3));

		Assert.That(_service.TryAcquire("com.example.plugin", raw, out _), Is.True);
	}

	[Test]
	public async Task After_The_Bound_Session_Drops_And_Its_Resume_Window_Lapses_The_Token_Works_Again()
	{
		var raw = _service.Mint("com.example.plugin", "launch-1", "Test Plugin", "1.0.0");
		Assert.That(_service.TryAcquire("com.example.plugin", raw, out _), Is.True);

		await _sessions.Create(Session("session-1", "com.example.plugin"));
		_service.Bind("launch-1", "session-1");

		// Dropped but still resumable: the launch stays held, because the plugin may still come back
		// to that very session.
		_sessions.Detach("session-1", _time.GetUtcNow());
		Assert.That(_service.TryAcquire("com.example.plugin", raw, out _), Is.False);

		_time.Advance(ProtocolTimeouts.SessionResumeWindow + TimeSpan.FromSeconds(1));

		Assert.That(_service.TryAcquire("com.example.plugin", raw, out _), Is.True);
	}

	[Test]
	public async Task A_Bound_Session_That_Is_Terminated_Frees_The_Token()
	{
		var raw = _service.Mint("com.example.plugin", "launch-1", "Test Plugin", "1.0.0");
		Assert.That(_service.TryAcquire("com.example.plugin", raw, out _), Is.True);

		await _sessions.Create(Session("session-1", "com.example.plugin"));
		_service.Bind("launch-1", "session-1");
		Assert.That(_service.TryAcquire("com.example.plugin", raw, out _), Is.False);

		await _sessions.Terminate("session-1", 1000, "Terminated.");

		Assert.That(_service.TryAcquire("com.example.plugin", raw, out _), Is.True);
	}

	[Test]
	public async Task A_Connected_Bound_Session_Keeps_The_Token_Held()
	{
		var raw = _service.Mint("com.example.plugin", "launch-1", "Test Plugin", "1.0.0");
		Assert.That(_service.TryAcquire("com.example.plugin", raw, out _), Is.True);

		await _sessions.Create(Session("session-1", "com.example.plugin"));
		_service.Bind("launch-1", "session-1");
		_sessions.TryAttach("session-1", new FakePluginConnection(), "instance-1");

		_time.Advance(TimeSpan.FromHours(1));

		Assert.That(_service.TryAcquire("com.example.plugin", raw, out _), Is.False);
	}

	[Test]
	public async Task N_Parallel_Reclaims_Of_A_Stale_Launch_Yield_Exactly_One_Success()
	{
		var raw = _service.Mint("com.example.plugin", "launch-1", "Test Plugin", "1.0.0");
		Assert.That(_service.TryAcquire("com.example.plugin", raw, out _), Is.True);
		await _sessions.Create(Session("session-1", "com.example.plugin"));
		_service.Bind("launch-1", "session-1");
		await _sessions.Terminate("session-1", 1000, "Gone.");

		var results = await Task.WhenAll(Enumerable.Range(0, 32)
			.Select(i => Task.Run(() => _service.TryAcquire("com.example.plugin", raw, out _))));

		Assert.That(results.Count(success => success), Is.EqualTo(1));
	}

	[Test]
	public void Wrong_Plugin_Id_Fails()
	{
		var raw = _service.Mint("com.example.plugin", "launch-1", "Test Plugin", "1.0.0");

		Assert.That(_service.TryAcquire("com.example.other", raw, out _), Is.False);
	}

	[Test]
	public void Discarded_Launch_Fails()
	{
		var raw = _service.Mint("com.example.plugin", "launch-1", "Test Plugin", "1.0.0");

		_service.Discard("launch-1");

		Assert.That(_service.TryAcquire("com.example.plugin", raw, out _), Is.False);
	}

	[Test]
	public void A_minted_but_never_acquired_launch_counts_as_active()
	{
		_service.Mint("com.example.plugin", "launch-1", "Test Plugin", "1.0.0");

		Assert.That(_service.HasActiveLaunch("com.example.plugin"), Is.True);
	}

	[Test]
	public void A_discarded_launch_does_not_count_as_active()
	{
		_service.Mint("com.example.plugin", "launch-1", "Test Plugin", "1.0.0");
		_service.Discard("launch-1");

		Assert.That(_service.HasActiveLaunch("com.example.plugin"), Is.False);
	}

	private PluginSessionRecord Session(string sessionId, string pluginId) => new()
	{
		SessionId = sessionId,
		PluginId = pluginId,
		DisplayName = pluginId,
		Origin = PluginSessionOrigin.Managed,
		NegotiatedVersion = 1,
		Capabilities = new Dictionary<string, CapabilityNegotiationResult>(StringComparer.Ordinal),
		DeclaredCapabilities = [],
		State = PluginSessionState.Awaiting,
		CreatedAt = _time.GetUtcNow()
	};
}
