using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Plugin.Protocol.Versioning;
using MacroDeckHost.Application.Plugins;
using MacroDeckHost.Tests.UnitTests.Auth;

namespace MacroDeckHost.Tests.UnitTests.Plugins;

[TestFixture]
public class PluginSessionRegistryTests
{
	private ManualTimeProvider _time = null!;
	private PluginSessionRegistry _registry = null!;

	[SetUp]
	public void SetUp()
	{
		_time = new ManualTimeProvider();
		_registry = new PluginSessionRegistry(_time, Serilog.Core.Logger.None);
	}

	private PluginSessionRecord NewRecord(string pluginId = "com.example.plugin", string? sessionId = null)
		=> new()
		{
			SessionId = sessionId ?? Guid.CreateVersion7().ToString("D"),
			PluginId = pluginId,
			DisplayName = "Example",
			Origin = PluginSessionOrigin.Managed,
			NegotiatedVersion = 1,
			Capabilities = new Dictionary<string, CapabilityNegotiationResult>(),
			DeclaredCapabilities = [],
			State = PluginSessionState.Awaiting,
			CreatedAt = _time.GetUtcNow()
		};

	[Test]
	public async Task IsCurrentConnection_Is_True_For_The_Attached_Connection()
	{
		var record = NewRecord();
		await _registry.Create(record);
		var connection = new FakePluginConnection();
		_registry.TryAttach(record.SessionId, connection, null);

		Assert.That(_registry.IsCurrentConnection(record.SessionId, connection), Is.True);
	}

	[Test]
	public async Task IsCurrentConnection_Is_False_Once_A_Newer_Connection_Has_Replaced_It()
	{
		// Regression for issue #413 finding 4: a replaced-but-still-unwinding old connection must be
		// able to tell that a newer connection has already taken over its session, so it does not run
		// cleanup (IPluginAssetReceiver.DropSession, keyed only by plugin id) against state the new
		// connection has already started using.
		var record = NewRecord();
		await _registry.Create(record);
		var oldConnection = new FakePluginConnection("old");
		_registry.TryAttach(record.SessionId, oldConnection, null);

		var newConnection = new FakePluginConnection("new");
		_registry.TryAttach(record.SessionId, newConnection, null);

		Assert.Multiple(() =>
		{
			Assert.That(_registry.IsCurrentConnection(record.SessionId, oldConnection), Is.False);
			Assert.That(_registry.IsCurrentConnection(record.SessionId, newConnection), Is.True);
		});
	}

	[Test]
	public void IsCurrentConnection_Is_False_For_An_Unknown_Session()
	{
		Assert.That(_registry.IsCurrentConnection("unknown-session", new FakePluginConnection()), Is.False);
	}

	[Test]
	public async Task Resume_Inside_The_Window_Succeeds()
	{
		var record = NewRecord();
		await _registry.Create(record);
		_registry.TryAttach(record.SessionId, new FakePluginConnection(), null);
		_registry.Detach(record.SessionId, _time.GetUtcNow());

		_time.Advance(ProtocolTimeouts.SessionResumeWindow - TimeSpan.FromSeconds(1));

		var resumed = _registry.TryResume(record.PluginId, record.SessionId, _time.GetUtcNow(), out var resumedRecord);

		Assert.Multiple(() =>
		{
			Assert.That(resumed, Is.True);
			Assert.That(resumedRecord!.SessionId, Is.EqualTo(record.SessionId));
		});
	}

	[Test]
	public async Task Resume_Outside_The_Window_Is_Refused()
	{
		var record = NewRecord();
		await _registry.Create(record);
		_registry.TryAttach(record.SessionId, new FakePluginConnection(), null);
		_registry.Detach(record.SessionId, _time.GetUtcNow());

		_time.Advance(ProtocolTimeouts.SessionResumeWindow + TimeSpan.FromSeconds(1));

		var resumed = _registry.TryResume(record.PluginId, record.SessionId, _time.GetUtcNow(), out _);

		Assert.That(resumed, Is.False);
	}

	[Test]
	public async Task Resume_Across_Plugins_Is_Refused()
	{
		var record = NewRecord();
		await _registry.Create(record);
		_registry.TryAttach(record.SessionId, new FakePluginConnection(), null);
		_registry.Detach(record.SessionId, _time.GetUtcNow());

		var resumed = _registry.TryResume("com.example.other-plugin", record.SessionId, _time.GetUtcNow(), out _);

		Assert.That(resumed, Is.False);
	}

	[Test]
	public async Task Goodbye_Makes_A_Session_Non_Resumable_Immediately()
	{
		var record = NewRecord();
		await _registry.Create(record);
		_registry.TryAttach(record.SessionId, new FakePluginConnection(), null);

		_registry.MakeNonResumable(record.SessionId);
		_registry.Detach(record.SessionId, _time.GetUtcNow());

		var resumed = _registry.TryResume(record.PluginId, record.SessionId, _time.GetUtcNow(), out _);

		Assert.That(resumed, Is.False);
	}

	[Test]
	public async Task Dropped_Records_Are_Pruned_After_The_Window()
	{
		var record = NewRecord();
		await _registry.Create(record);
		_registry.TryAttach(record.SessionId, new FakePluginConnection(), null);
		_registry.Detach(record.SessionId, _time.GetUtcNow());

		_time.Advance(ProtocolTimeouts.SessionResumeWindow + TimeSpan.FromSeconds(1));

		var snapshot = _registry.Snapshot();

		Assert.That(snapshot, Is.Empty);
	}

	[Test]
	public async Task A_Session_That_Was_Never_Attached_Is_Dropped_Once_It_Can_No_Longer_Be_Attached()
	{
		var abandoned = NewRecord("com.example.ghost");
		await _registry.Create(abandoned);

		_time.Advance(TimeSpan.FromHours(1));

		var snapshot = _registry.Snapshot();

		Assert.Multiple(() =>
		{
			Assert.That(snapshot, Is.Empty);
			Assert.That(_registry.TryAttach(abandoned.SessionId, new FakePluginConnection(), null), Is.False);
		});
	}

	[Test]
	public async Task A_Session_Created_Moments_Ago_Is_Still_Waiting_And_Can_Still_Attach()
	{
		var record = NewRecord();
		await _registry.Create(record);

		_time.Advance(TimeSpan.FromSeconds(5));

		var snapshot = _registry.Snapshot();
		var attached = _registry.TryAttach(record.SessionId, new FakePluginConnection(), null);

		Assert.Multiple(() =>
		{
			Assert.That(snapshot.Single().State, Is.EqualTo(PluginSessionState.Awaiting));
			Assert.That(attached, Is.True);
			Assert.That(_registry.Snapshot().Single().State, Is.EqualTo(PluginSessionState.Connected));
		});
	}

	[Test]
	public async Task Sweeping_An_Abandoned_Session_Leaves_A_Resumable_One_Alone()
	{
		var resumable = NewRecord("com.example.resumable");
		await _registry.Create(resumable);
		_registry.TryAttach(resumable.SessionId, new FakePluginConnection(), null);

		var abandoned = NewRecord("com.example.ghost");
		await _registry.Create(abandoned);

		_time.Advance(TimeSpan.FromHours(1));
		_registry.Detach(resumable.SessionId, _time.GetUtcNow());

		var pluginIds = _registry.Snapshot().Select(session => session.PluginId).ToList();

		Assert.That(pluginIds, Has.Count.EqualTo(1).And.Contains("com.example.resumable"));
	}

	[Test]
	public async Task Second_Session_For_The_Same_Plugin_Replaces_And_Closes_The_First()
	{
		var first = NewRecord();
		await _registry.Create(first);
		var connection = new FakePluginConnection();
		_registry.TryAttach(first.SessionId, connection, null);

		var second = NewRecord(sessionId: Guid.CreateVersion7().ToString("D"));
		await _registry.Create(second);

		Assert.Multiple(() =>
		{
			Assert.That(connection.Closes, Has.Count.EqualTo(1));
			Assert.That(connection.Closes[0].CloseCode, Is.EqualTo(ProtocolCloseCodes.SessionReplaced));
			Assert.That(_registry.Snapshot().Select(s => s.SessionId), Is.EquivalentTo(new[] { second.SessionId }));
		});
	}

	[Test]
	public async Task A_Dropped_Sessions_Token_Stays_Valid_Within_The_Resume_Window()
	{
		var record = NewRecord();
		await _registry.Create(record);
		_registry.TryAttach(record.SessionId, new FakePluginConnection(), null);
		_registry.Detach(record.SessionId, _time.GetUtcNow());

		_time.Advance(ProtocolTimeouts.SessionResumeWindow - TimeSpan.FromSeconds(1));

		Assert.That(_registry.Snapshot().Select(s => s.SessionId), Does.Contain(record.SessionId));
	}

	[Test]
	public async Task Terminate_Removes_The_Session_And_Closes_The_Connection()
	{
		var record = NewRecord();
		await _registry.Create(record);
		var connection = new FakePluginConnection();
		_registry.TryAttach(record.SessionId, connection, null);

		var terminated = await _registry.Terminate(record.SessionId,
			ProtocolCloseCodes.AuthenticationFailed,
			"Test.");

		Assert.Multiple(() =>
		{
			Assert.That(terminated, Is.True);
			Assert.That(connection.Closes, Has.Count.EqualTo(1));
			Assert.That(_registry.Snapshot(), Is.Empty);
		});
	}

	[Test]
	public async Task TerminateForPlugin_Terminates_Whatever_Session_Is_Currently_Live()
	{
		var record = NewRecord();
		await _registry.Create(record);

		var terminated = await _registry.TerminateForPlugin(record.PluginId,
			ProtocolCloseCodes.AuthenticationFailed,
			"Test.");
		var terminatedAgain = await _registry.TerminateForPlugin(record.PluginId,
			ProtocolCloseCodes.AuthenticationFailed,
			"Test.");

		Assert.Multiple(() =>
		{
			Assert.That(terminated, Is.True);
			Assert.That(terminatedAgain, Is.False);
		});
	}

	[Test]
	public async Task Touch_Surfaces_As_LastInboundAt_In_The_Snapshot()
	{
		var record = NewRecord();
		await _registry.Create(record);
		_registry.TryAttach(record.SessionId, new FakePluginConnection(), null);

		_time.Advance(TimeSpan.FromSeconds(5));
		var touchedAt = _time.GetUtcNow();
		_registry.Touch(record.SessionId, touchedAt);

		var snapshot = _registry.Snapshot().Single(s => s.SessionId == record.SessionId);

		Assert.That(snapshot.LastInboundAt, Is.EqualTo(touchedAt));
	}

	[Test]
	public void Touch_On_An_Unknown_Session_Does_Nothing()
		=> Assert.DoesNotThrow(() => _registry.Touch("unknown-session", _time.GetUtcNow()));

	[Test]
	public async Task LastInboundAt_Is_Null_Until_Touched()
	{
		var record = NewRecord();
		await _registry.Create(record);

		var snapshot = _registry.Snapshot().Single(s => s.SessionId == record.SessionId);

		Assert.That(snapshot.LastInboundAt, Is.Null);
	}

	[Test]
	public async Task SendToPlugin_Reaches_The_Attached_Connection()
	{
		var record = NewRecord();
		await _registry.Create(record);
		var connection = new FakePluginConnection();
		_registry.TryAttach(record.SessionId, connection, null);

		var envelope = new ProtocolEnvelope { Type = "session.ping", Id = "1" };
		var sent = await _registry.SendToPlugin(record.PluginId, envelope);

		Assert.Multiple(() =>
		{
			Assert.That(sent, Is.True);
			Assert.That(connection.Sent, Does.Contain(envelope));
		});
	}

	[Test]
	public async Task SendToPlugin_Returns_False_For_An_Unknown_Plugin()
	{
		var envelope = new ProtocolEnvelope { Type = "session.ping", Id = "1" };
		var sent = await _registry.SendToPlugin("com.example.unknown", envelope);

		Assert.That(sent, Is.False);
	}

	[Test]
	public async Task SendToPlugin_Returns_False_For_A_Detached_Plugin()
	{
		var record = NewRecord();
		await _registry.Create(record);
		_registry.TryAttach(record.SessionId, new FakePluginConnection(), null);
		_registry.Detach(record.SessionId, _time.GetUtcNow());

		var envelope = new ProtocolEnvelope { Type = "session.ping", Id = "1" };
		var sent = await _registry.SendToPlugin(record.PluginId, envelope);

		Assert.That(sent, Is.False);
	}

	[Test]
	public async Task SetPaused_Parks_A_Non_Exempt_Send_Until_Resumed()
	{
		var record = NewRecord();
		await _registry.Create(record);
		var connection = new FakePluginConnection();
		_registry.TryAttach(record.SessionId, connection, null);

		_registry.SetPaused(record.SessionId, paused: true);

		var envelope = new ProtocolEnvelope { Type = MessageTypes.EventPublish, Id = "1" };
		var sendTask = _registry.SendToPlugin(record.PluginId, envelope);

		Assert.That(sendTask.IsCompleted, Is.False, "a paused, non-exempt send must not complete immediately");
		Assert.That(connection.Sent, Is.Empty);

		_registry.SetPaused(record.SessionId, paused: false);
		var sent = await sendTask;

		Assert.Multiple(() =>
		{
			Assert.That(sent, Is.True);
			Assert.That(connection.Sent, Does.Contain(envelope));
		});
	}

	[Test]
	public async Task SetPaused_Does_Not_Park_An_Exempt_Reply_Type()
	{
		var record = NewRecord();
		await _registry.Create(record);
		var connection = new FakePluginConnection();
		_registry.TryAttach(record.SessionId, connection, null);

		_registry.SetPaused(record.SessionId, paused: true);

		var envelope = new ProtocolEnvelope { Type = MessageTypes.CapabilityResult, Id = "1" };
		var sent = await _registry.SendToPlugin(record.PluginId, envelope);

		Assert.That(sent, Is.True);
	}

	[Test]
	public void SetPaused_On_An_Unknown_Session_Does_Nothing()
		=> Assert.DoesNotThrow(() => _registry.SetPaused("unknown-session", paused: true));

	[Test]
	public async Task SessionEnded_Fires_With_Detached_On_Detach()
	{
		var record = NewRecord();
		await _registry.Create(record);
		_registry.TryAttach(record.SessionId, new FakePluginConnection(), null);

		PluginSessionEndedEventArgs? raised = null;
		_registry.SessionEnded += (_, e) => raised = e;

		_registry.Detach(record.SessionId, _time.GetUtcNow());

		Assert.Multiple(() =>
		{
			Assert.That(raised, Is.Not.Null);
			Assert.That(raised!.PluginId, Is.EqualTo(record.PluginId));
			Assert.That(raised.SessionId, Is.EqualTo(record.SessionId));
			Assert.That(raised.Reason, Is.EqualTo(PluginSessionEndReason.Detached));
		});
	}

	[Test]
	public async Task SessionEnded_Fires_With_Pruned_Once_The_Resume_Window_Elapses()
	{
		var record = NewRecord();
		await _registry.Create(record);
		_registry.TryAttach(record.SessionId, new FakePluginConnection(), null);
		_registry.Detach(record.SessionId, _time.GetUtcNow());

		var raised = new List<PluginSessionEndedEventArgs>();
		_registry.SessionEnded += (_, e) => raised.Add(e);

		_time.Advance(ProtocolTimeouts.SessionResumeWindow + TimeSpan.FromSeconds(1));
		_registry.Snapshot(); // prune-on-access

		Assert.That(raised.Select(e => e.Reason), Is.EqualTo(new[] { PluginSessionEndReason.Pruned }));
	}

	[Test]
	public async Task SessionEnded_Fires_With_Pruned_On_Terminate()
	{
		var record = NewRecord();
		await _registry.Create(record);
		_registry.TryAttach(record.SessionId, new FakePluginConnection(), null);

		var raised = new List<PluginSessionEndedEventArgs>();
		_registry.SessionEnded += (_, e) => raised.Add(e);

		await _registry.Terminate(record.SessionId, ProtocolCloseCodes.AuthenticationFailed, "Test.");

		Assert.That(raised, Has.Count.EqualTo(1));
		Assert.Multiple(() =>
		{
			Assert.That(raised[0].PluginId, Is.EqualTo(record.PluginId));
			Assert.That(raised[0].SessionId, Is.EqualTo(record.SessionId));
			Assert.That(raised[0].Reason, Is.EqualTo(PluginSessionEndReason.Pruned));
		});
	}

	[Test]
	public async Task SessionEnded_Fires_With_Pruned_On_TerminateForPlugin()
	{
		var record = NewRecord();
		await _registry.Create(record);
		_registry.TryAttach(record.SessionId, new FakePluginConnection(), null);

		var raised = new List<PluginSessionEndedEventArgs>();
		_registry.SessionEnded += (_, e) => raised.Add(e);

		await _registry.TerminateForPlugin(record.PluginId, ProtocolCloseCodes.AuthenticationFailed, "Test.");

		Assert.That(raised.Select(e => e.Reason), Is.EqualTo(new[] { PluginSessionEndReason.Pruned }));
	}

	[Test]
	public async Task A_replaced_session_ends_the_same_way_a_terminated_one_does()
	{
		var first = NewRecord();
		await _registry.Create(first);
		var firstConnection = new FakePluginConnection();
		_registry.TryAttach(first.SessionId, firstConnection, null);

		var raised = new List<PluginSessionEndedEventArgs>();
		_registry.SessionEnded += (_, e) => raised.Add(e);

		var second = NewRecord(first.PluginId);
		await _registry.Create(second);

		Assert.That(raised, Has.Count.EqualTo(1));
		Assert.Multiple(() =>
		{
			Assert.That(raised[0].PluginId, Is.EqualTo(first.PluginId));
			Assert.That(raised[0].SessionId, Is.EqualTo(first.SessionId));
			Assert.That(raised[0].Reason, Is.EqualTo(PluginSessionEndReason.Pruned));
			Assert.That(firstConnection.Closes, Has.Count.EqualTo(1));
			Assert.That(firstConnection.Closes[0].CloseCode, Is.EqualTo(ProtocolCloseCodes.SessionReplaced));
		});
	}

	[Test]
	public async Task Terminate_Does_Not_Double_Raise_When_The_Closed_Connection_Then_Detaches()
	{
		var record = NewRecord();
		await _registry.Create(record);
		_registry.TryAttach(record.SessionId, new FakePluginConnection(), null);

		var raised = new List<PluginSessionEndedEventArgs>();
		_registry.SessionEnded += (_, e) => raised.Add(e);

		await _registry.Terminate(record.SessionId, ProtocolCloseCodes.AuthenticationFailed, "Test.");
		_registry.Detach(record.SessionId, _time.GetUtcNow());

		Assert.That(raised, Has.Count.EqualTo(1));
	}

	[Test]
	public async Task GetCapabilities_Returns_Declared_And_Negotiated_Capabilities()
	{
		var negotiated = new Dictionary<string, CapabilityNegotiationResult>
		{
			["actions"] = CapabilityNegotiationResult.Accept("actions", 1)
		};

		var record = new PluginSessionRecord
		{
			SessionId = Guid.CreateVersion7().ToString("D"),
			PluginId = "com.example.plugin",
			DisplayName = "Example",
			Origin = PluginSessionOrigin.Managed,
			NegotiatedVersion = 1,
			Capabilities = negotiated,
			DeclaredCapabilities =
			[
				new DeclaredCapability
				{
					Kind = "actions", LocalId = "toggle",
					VersionRange = new CapabilityVersionRange { Minimum = 1, Maximum = 1 }
				}
			],
			State = PluginSessionState.Awaiting,
			CreatedAt = _time.GetUtcNow()
		};

		await _registry.Create(record);

		var capabilities = _registry.GetCapabilities(record.PluginId);

		Assert.Multiple(() =>
		{
			Assert.That(capabilities, Is.Not.Null);
			Assert.That(capabilities!.DeclaredCapabilities, Has.Count.EqualTo(1));
			Assert.That(capabilities.Capabilities, Does.ContainKey("actions"));
		});
	}

	[Test]
	public void GetCapabilities_Returns_Null_For_An_Unknown_Plugin()
		=> Assert.That(_registry.GetCapabilities("com.example.unknown"), Is.Null);
}
