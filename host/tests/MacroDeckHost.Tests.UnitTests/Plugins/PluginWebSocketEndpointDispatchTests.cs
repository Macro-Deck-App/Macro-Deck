using System.Globalization;
using System.Text.Json;
using System.Threading.Channels;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Capabilities.EmailInbox;
using MacroDeck.Plugin.Protocol.Capabilities.Variables;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Events;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Plugin.Protocol.Logging;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Plugin.Protocol.Versioning;
using MacroDeckHost.Application.Auth;
using MacroDeckHost.Application.Email;
using MacroDeckHost.Application.Plugins;
using MacroDeckHost.Application.Plugins.Assets;
using MacroDeckHost.Application.Plugins.Capabilities;
using MacroDeckHost.Application.Plugins.Logging;
using MacroDeckHost.Application.Triggers;
using MacroDeckHost.Plugins;
using MacroDeckHost.Plugins.Capabilities.Callbacks;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeck.Sdk.Decks;
using MacroDeck.Sdk.Scripts;
using MacroDeck.Sdk.Widgets;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;

namespace MacroDeckHost.Tests.UnitTests.Plugins;

[TestFixture]
public partial class PluginWebSocketEndpointDispatchTests
{
	private static (PluginWebSocketEndpoint Endpoint, FakePluginConnection Connection, FakePluginCapabilityInvoker
		Invoker)
		CreateEndpoint(IEventBus? eventBus = null)
	{
		var registry = new PluginSessionRegistry(TimeProvider.System, Serilog.Core.Logger.None);
		var invoker = new FakePluginCapabilityInvoker();
		var throttle = new LoginThrottle(TimeProvider.System);
		var statePusher
			= new HostStatePusher(registry,
				new EmptyDeckNavigator(),
				new EmptyScriptApi(),
				new EmptyWidgetApi(),
				new StubEventBindingTracker(),
				new MacroDeckHost.Application.Deck.DeckClientTracker(Serilog.Core.Logger.None),
				new Adb.FakeAdbManager(),
				new FixedAdbAccessPolicy(),
				Serilog.Core.Logger.None);
		var endpoint = new PluginWebSocketEndpoint(registry,
			invoker,
			new FakeRemotePluginIntegrationRegistrar(),
			new FakePluginCallbackRouter(),
			new PluginAssetReceiver(new InMemoryPluginAssetCache()),
			statePusher,
			eventBus ?? new FakeEventBus(),
			throttle,
			TimeProvider.System,
			new RecordingMediator(),
			new NeverStoppingLifetime(),
			CreateLogIngestor(registry),
			Serilog.Core.Logger.None);

		return (endpoint, new FakePluginConnection(), invoker);
	}

	private static (
		PluginWebSocketEndpoint Endpoint,
		FakePluginConnection Connection,
		PluginSessionRegistry SessionRegistry,
		ScriptedCapabilityInvoker Invoker,
		RecordingRegistrar Registrar,
		InMemorySnapshotStore SnapshotStore) CreateEndpointWithState(
			string pluginId,
			string sessionId,
			IEmailSyncSignal? emailSync = null)
	{
		var registry = new PluginSessionRegistry(TimeProvider.System, Serilog.Core.Logger.None);
		var invoker = new ScriptedCapabilityInvoker();
		var snapshotStore = new InMemorySnapshotStore();
		var registrar = new RecordingRegistrar
			{ Refresher = new RemotePluginSnapshotRefresher(invoker, snapshotStore) };
		var throttle = new LoginThrottle(TimeProvider.System);
		var statePusher
			= new HostStatePusher(registry,
				new EmptyDeckNavigator(),
				new EmptyScriptApi(),
				new EmptyWidgetApi(),
				new StubEventBindingTracker(),
				new MacroDeckHost.Application.Deck.DeckClientTracker(Serilog.Core.Logger.None),
				new Adb.FakeAdbManager(),
				new FixedAdbAccessPolicy(),
				Serilog.Core.Logger.None);
		var endpoint = new PluginWebSocketEndpoint(registry,
			invoker,
			registrar,
			new FakePluginCallbackRouter(),
			new PluginAssetReceiver(new InMemoryPluginAssetCache()),
			statePusher,
			new FakeEventBus(),
			throttle,
			TimeProvider.System,
			new RecordingMediator(),
			new NeverStoppingLifetime(),
			CreateLogIngestor(registry),
			Serilog.Core.Logger.None,
			emailSync: emailSync);

		var connection = new FakePluginConnection();
		var record = new PluginSessionRecord
		{
			SessionId = sessionId,
			PluginId = pluginId,
			DisplayName = "Example Plugin",
			Origin = PluginSessionOrigin.Managed,
			NegotiatedVersion = 1,
			Capabilities = new Dictionary<string, CapabilityNegotiationResult>(StringComparer.Ordinal)
			{
				[CapabilityKinds.Variables] = CapabilityNegotiationResult.Accept(CapabilityKinds.Variables, 1)
			},
			DeclaredCapabilities =
			[
				new DeclaredCapability
				{
					Kind = CapabilityKinds.Variables, LocalId = "cpu-temp",
					VersionRange = new CapabilityVersionRange { Minimum = 1, Maximum = 1 }
				}
			],
			State = PluginSessionState.Awaiting,
			CreatedAt = TimeProvider.System.GetUtcNow()
		};

		registry.Create(record).GetAwaiter().GetResult();
		registry.TryAttach(sessionId, connection, null);

		return (endpoint, connection, registry, invoker, registrar, snapshotStore);
	}

	private static PluginLogIngestor CreateLogIngestor(IPluginSessionRegistry sessionRegistry)
	{
		return new PluginLogIngestor(new PluginLogRateLimiter(TimeProvider.System),
			sessionRegistry,
			new FakePluginSupervisor(),
			TimeProvider.System);
	}

	private sealed class FakePluginCallbackRouter : IPluginCallbackRouter
	{
		public List<string?> RoutedSessionIds { get; } = [];

		public Task<HostCallbackResult> RouteAsync(
			string pluginId,
			string correlationId,
			HostInvokePayload payload,
			CancellationToken cancellationToken)
		{
			return RouteAsync(pluginId, null, correlationId, payload, cancellationToken);
		}

		public Task<HostCallbackResult> RouteAsync(
			string pluginId,
			string? sessionId,
			string correlationId,
			HostInvokePayload payload,
			CancellationToken cancellationToken)
		{
			RoutedSessionIds.Add(sessionId);
			return Task.FromResult(HostCallbackResult.Ok());
		}

		public HostCallbackResult? Admit(string pluginId, HostInvokePayload payload)
		{
			return null;
		}
	}

	private sealed class FakeEventBus : IEventBus
	{
		private readonly Channel<EventOccurrence> _channel = Channel.CreateUnbounded<EventOccurrence>();

		public void Publish(EventOccurrence occurrence)
		{
			_channel.Writer.TryWrite(occurrence);
		}

		public ChannelReader<EventOccurrence> Reader => _channel.Reader;
	}

	private sealed class EmptyDeckNavigator : IDeckNavigator
	{
		public Task ChangeFolderAsync(
			string folderId,
			string? originClientId = null,
			CancellationToken cancellationToken = default)
		{
			return Task.CompletedTask;
		}

		public Task ChangeProfileAsync(
			string profileId,
			string? originClientId = null,
			CancellationToken cancellationToken = default)
		{
			return Task.CompletedTask;
		}

		public Task GoToParentAsync(string? originClientId = null, CancellationToken cancellationToken = default)
		{
			return Task.CompletedTask;
		}

		public Task GoBackAsync(string? originClientId = null, CancellationToken cancellationToken = default)
		{
			return Task.CompletedTask;
		}

		public IReadOnlyList<DeckFolder> GetFolders()
		{
			return [];
		}

		public IReadOnlyList<DeckProfile> GetProfiles()
		{
			return [];
		}
	}

	private sealed class EmptyScriptApi : IScriptApi
	{
		public IReadOnlyList<Script> GetScripts()
		{
			return [];
		}

		public Task<MacroDeck.Sdk.Actions.ActionResult> RunAsync(
			string scriptId,
			IReadOnlyDictionary<string, object?>? inputs = null,
			string? originClientId = null,
			string? ownerWidgetId = null,
			CancellationToken cancellationToken = default)
		{
			return MacroDeck.Sdk.Actions.ActionResult.SucceededTask;
		}
	}

	private sealed class EmptyWidgetApi : IWidgetApi
	{
		public IReadOnlyList<WidgetTargetInfo> GetWidgets()
		{
			return [];
		}

		public bool Exists(string widgetId)
		{
			return false;
		}

		public Task<bool> ApplyAsync(WidgetAppearanceRequest request, CancellationToken cancellationToken = default)
		{
			return Task.FromResult(false);
		}

		public Task<WidgetStateWriteResult> SetStateAsync(
			string widgetId,
			string stateId,
			CancellationToken cancellationToken = default)
		{
			return Task.FromResult(WidgetStateWriteResult.Failed(WidgetStateWriteError.NotFound));
		}

		public Task<WidgetStateWriteResult> AdvanceStateAsync(
			string widgetId,
			CancellationToken cancellationToken = default)
		{
			return Task.FromResult(WidgetStateWriteResult.Failed(WidgetStateWriteError.NotFound));
		}
	}

	[Test]
	public async Task A_host_invoke_is_routed_with_the_session_it_arrived_on()
	{
		var router = new FakePluginCallbackRouter();
		var registry = new PluginSessionRegistry(TimeProvider.System, Serilog.Core.Logger.None);
		var endpoint = new PluginWebSocketEndpoint(registry,
			new FakePluginCapabilityInvoker(),
			new FakeRemotePluginIntegrationRegistrar(),
			router,
			new PluginAssetReceiver(new InMemoryPluginAssetCache()),
			new HostStatePusher(registry,
				new EmptyDeckNavigator(),
				new EmptyScriptApi(),
				new EmptyWidgetApi(),
				new StubEventBindingTracker(),
				new MacroDeckHost.Application.Deck.DeckClientTracker(Serilog.Core.Logger.None),
				new Adb.FakeAdbManager(),
				new FixedAdbAccessPolicy(),
				Serilog.Core.Logger.None),
			new FakeEventBus(),
			new LoginThrottle(TimeProvider.System),
			TimeProvider.System,
			new RecordingMediator(),
			new NeverStoppingLifetime(),
			CreateLogIngestor(registry),
			Serilog.Core.Logger.None);

		await endpoint.HandleHostInvokeAsync(new FakePluginConnection(),
			"com.example.plugin",
			"session-1",
			new ProtocolEnvelope
			{
				Type = MessageTypes.HostInvoke,
				Id = "1",
				Payload = JsonSerializer.SerializeToElement(new HostInvokePayload
						{ Api = HostApis.Ui, Operation = HostOperations.Ui.RegisterResource },
					PluginProtocolJson.Options)
			},
			CancellationToken.None);

		Assert.That(router.RoutedSessionIds, Is.EqualTo(new[] { "session-1" }));
	}

	[Test]
	public async Task
		An_artwork_lookup_waiting_on_a_player_does_not_hold_the_dispatch_loop_and_is_answered_when_it_finishes()
	{
		var router = new BlockingArtworkRouter();
		var endpoint = EndpointWith(router);
		var connection = new FakePluginConnection();

		await endpoint.HandleHostInvokeAsync(connection,
			"com.example.plugin",
			"session-1",
			ArtworkInvoke("1"),
			CancellationToken.None);

		Assert.That(connection.Sent, Is.Empty, "the invoke returned to the dispatch loop before the lookup finished");

		router.Release.SetResult();
		await WaitForAsync(() => connection.Sent.Count == 1);
		Assert.Multiple(() =>
		{
			Assert.That(connection.Sent[0].Type, Is.EqualTo(MessageTypes.HostResult));
			Assert.That(connection.Sent[0].CorrelationId, Is.EqualTo("1"));
			Assert.That(connection.Sent[0].Error, Is.Null);
		});
	}

	[Test]
	public async Task A_plugin_with_four_artwork_lookups_waiting_is_told_to_retry_the_fifth()
	{
		var router = new BlockingArtworkRouter();
		var endpoint = EndpointWith(router);
		var connection = new FakePluginConnection();

		for (var index = 1; index <= 5; index++)
		{
			await endpoint.HandleHostInvokeAsync(connection,
				"com.example.plugin",
				"session-1",
				ArtworkInvoke(index.ToString(CultureInfo.InvariantCulture)),
				CancellationToken.None);
		}

		Assert.Multiple(() =>
		{
			Assert.That(connection.Sent, Has.Count.EqualTo(1));
			Assert.That(connection.Sent[0].CorrelationId, Is.EqualTo("5"));
			Assert.That(connection.Sent[0].Error?.Code, Is.EqualTo(ProtocolErrorCodes.RateLimited));
			Assert.That(connection.Sent[0].Error?.Retryable, Is.True);
		});

		router.Release.SetResult();
		await WaitForAsync(() => connection.Sent.Count == 5);
	}

	[Test]
	public async Task Album_colour_lookups_wait_off_the_dispatch_loop_and_share_the_artwork_lookup_limit()
	{
		var router = new BlockingArtworkRouter();
		var endpoint = EndpointWith(router);
		var connection = new FakePluginConnection();

		for (var index = 1; index <= 5; index++)
		{
			await endpoint.HandleHostInvokeAsync(connection,
				"com.example.plugin",
				"session-1",
				index == 1
					? ArtworkInvoke("1")
					: new ProtocolEnvelope
					{
						Type = MessageTypes.HostInvoke,
						Id = index.ToString(CultureInfo.InvariantCulture),
						Payload = JsonSerializer.SerializeToElement(new HostInvokePayload
							{
								Api = HostApis.MusicPlayers, Operation = HostOperations.MusicPlayers.AlbumColor
							},
							PluginProtocolJson.Options)
					},
				CancellationToken.None);
		}

		Assert.Multiple(() =>
		{
			Assert.That(connection.Sent, Has.Count.EqualTo(1), "the dispatch loop returned while four lookups wait");
			Assert.That(connection.Sent[0].CorrelationId, Is.EqualTo("5"));
			Assert.That(connection.Sent[0].Error?.Code, Is.EqualTo(ProtocolErrorCodes.RateLimited));
		});

		router.Release.SetResult();
		await WaitForAsync(() => connection.Sent.Count == 5);
	}

	[Test]
	public async Task A_finished_artwork_lookup_frees_its_slot_even_when_the_router_threw()
	{
		var router = new BlockingArtworkRouter { ThrowOnRelease = true };
		var endpoint = EndpointWith(router);
		var connection = new FakePluginConnection();

		for (var index = 1; index <= 4; index++)
		{
			await endpoint.HandleHostInvokeAsync(connection,
				"com.example.plugin",
				"session-1",
				ArtworkInvoke(index.ToString(CultureInfo.InvariantCulture)),
				CancellationToken.None);
		}

		router.Release.SetResult();
		await Task.Delay(200);
		var router2Attempt = new FakePluginConnection();
		router.Reset();
		await endpoint.HandleHostInvokeAsync(router2Attempt,
			"com.example.plugin",
			"session-1",
			ArtworkInvoke("9"),
			CancellationToken.None);

		Assert.That(router2Attempt.Sent, Is.Empty, "the fifth call was admitted after the first four ended");

		router.ThrowOnRelease = false;
		router.Release.SetResult();
		await WaitForAsync(() => router2Attempt.Sent.Count == 1);
		Assert.That(router2Attempt.Sent[0].Error, Is.Null);
	}

	private static ProtocolEnvelope ArtworkInvoke(string id)
	{
		return new ProtocolEnvelope
		{
			Type = MessageTypes.HostInvoke,
			Id = id,
			Payload = JsonSerializer.SerializeToElement(new HostInvokePayload
					{ Api = HostApis.Ui, Operation = HostOperations.Ui.RegisterMusicPlayerArtwork },
				PluginProtocolJson.Options)
		};
	}

	private static PluginWebSocketEndpoint EndpointWith(IPluginCallbackRouter router)
	{
		var registry = new PluginSessionRegistry(TimeProvider.System, Serilog.Core.Logger.None);

		return new PluginWebSocketEndpoint(registry,
			new FakePluginCapabilityInvoker(),
			new FakeRemotePluginIntegrationRegistrar(),
			router,
			new PluginAssetReceiver(new InMemoryPluginAssetCache()),
			new HostStatePusher(registry,
				new EmptyDeckNavigator(),
				new EmptyScriptApi(),
				new EmptyWidgetApi(),
				new StubEventBindingTracker(),
				new MacroDeckHost.Application.Deck.DeckClientTracker(Serilog.Core.Logger.None),
				new Adb.FakeAdbManager(),
				new FixedAdbAccessPolicy(),
				Serilog.Core.Logger.None),
			new FakeEventBus(),
			new LoginThrottle(TimeProvider.System),
			TimeProvider.System,
			new RecordingMediator(),
			new NeverStoppingLifetime(),
			CreateLogIngestor(registry),
			Serilog.Core.Logger.None);
	}

	private sealed class BlockingArtworkRouter : IPluginCallbackRouter
	{
		public TaskCompletionSource Release { get; private set; }
			= new(TaskCreationOptions.RunContinuationsAsynchronously);

		public bool ThrowOnRelease { get; set; }

		public void Reset()
		{
			Release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		}

		public Task<HostCallbackResult> RouteAsync(
			string pluginId,
			string correlationId,
			HostInvokePayload payload,
			CancellationToken cancellationToken)
		{
			return RouteAsync(pluginId, null, correlationId, payload, cancellationToken);
		}

		public async Task<HostCallbackResult> RouteAsync(
			string pluginId,
			string? sessionId,
			string correlationId,
			HostInvokePayload payload,
			CancellationToken cancellationToken)
		{
			await Release.Task.WaitAsync(cancellationToken);

			return ThrowOnRelease ? throw new InvalidOperationException("boom") : HostCallbackResult.Ok();
		}

		public HostCallbackResult? Admit(string pluginId, HostInvokePayload payload)
		{
			return null;
		}
	}

	[Test]
	public async Task HandleCapabilityResultAsync_Sends_Malformed_Envelope_When_The_Correlation_Id_Is_Missing()
	{
		var (endpoint, connection, invoker) = CreateEndpoint();
		invoker.CompleteResult = false;

		await endpoint.HandleCapabilityResultAsync(connection,
			"com.example.plugin",
			new ProtocolEnvelope { Type = MessageTypes.CapabilityResult, Id = "1" },
			CancellationToken.None);

		Assert.That(connection.Sent.Single().Error?.Code, Is.EqualTo(ProtocolErrorCodes.MalformedEnvelope));
	}

	[Test]
	public async Task HandleCapabilityResultAsync_Sends_Correlation_Unknown_When_The_Invoker_Does_Not_Recognise_It()
	{
		var (endpoint, connection, invoker) = CreateEndpoint();
		invoker.CompleteResult = false;

		await endpoint.HandleCapabilityResultAsync(connection,
			"com.example.plugin",
			new ProtocolEnvelope { Type = MessageTypes.CapabilityResult, Id = "1", CorrelationId = "unknown" },
			CancellationToken.None);

		Assert.That(connection.Sent.Single().Error?.Code, Is.EqualTo(ProtocolErrorCodes.CorrelationUnknown));
	}

	[Test]
	public async Task HandleCapabilityResultAsync_Sends_Nothing_When_The_Invoker_Accepted_It()
	{
		var (endpoint, connection, invoker) = CreateEndpoint();
		invoker.CompleteResult = true;

		await endpoint.HandleCapabilityResultAsync(connection,
			"com.example.plugin",
			new ProtocolEnvelope { Type = MessageTypes.CapabilityResult, Id = "1", CorrelationId = "known" },
			CancellationToken.None);

		Assert.That(connection.Sent, Is.Empty);
	}

	[Test]
	public async Task HandleStateUpdateAsync_Hands_The_Named_Kind_To_The_Registrar()
	{
		const string pluginId = "com.example.plugin";
		var (endpoint, _, _, _, registrar, _) = CreateEndpointWithState(pluginId, "session-1");

		await endpoint.HandleStateUpdateAsync(pluginId,
			new ProtocolEnvelope
			{
				Type = MessageTypes.StateUpdate,
				Id = "1",
				Payload = JsonSerializer.SerializeToElement(new StateUpdatePayload { Kind = CapabilityKinds.Variables },
					PluginProtocolJson.Options)
			},
			CancellationToken.None);

		Assert.That(registrar.StateUpdates, Is.EqualTo(new[] { (pluginId, CapabilityKinds.Variables) }));
	}

	[Test]
	public async Task An_email_inbox_push_only_asks_for_a_re_read_of_that_inbox()
	{
		const string pluginId = "com.example.mail";
		using var signal = new EmailSyncSignal();
		var (endpoint, _, _, invoker, registrar, _) = CreateEndpointWithState(pluginId, "session-1", signal);

		await endpoint.HandleStateUpdateAsync(pluginId, EmailStateUpdate("work"), CancellationToken.None);

		var pending = signal.TakePending();
		Assert.Multiple(() =>
		{
			Assert.That(pending.Inboxes, Is.EquivalentTo(new[] { (pluginId, "work") }));
			Assert.That(invoker.InvokeCount, Is.Zero, "no snapshot refresh round trip");
			Assert.That(registrar.StateUpdates, Is.Empty, "no state update applied, so no re-registration");
			Assert.That(registrar.AppliedPluginIds, Is.Empty, "no re-registration, so no connected event");
		});
	}

	[Test]
	public async Task An_email_inbox_list_push_goes_through_the_registrar_and_re_reads_every_inbox()
	{
		const string pluginId = "com.example.mail";
		using var signal = new EmailSyncSignal();
		var (endpoint, _, _, invoker, registrar, _) = CreateEndpointWithState(pluginId, "session-1", signal);
		invoker.EmailInboxesResult = new EmailInboxesResult
		{
			Inboxes = [new EmailInboxDto { Id = "work", DisplayName = "Work" }]
		};

		await endpoint.HandleStateUpdateAsync(pluginId, EmailStateUpdate(null), CancellationToken.None);
		var pending = signal.TakePending();

		Assert.Multiple(() =>
		{
			Assert.That(registrar.StateUpdates, Is.EqualTo(new[] { (pluginId, CapabilityKinds.EmailInbox) }));
			Assert.That(pending.Integrations, Is.EquivalentTo(new[] { pluginId }));
		});
	}

	private static ProtocolEnvelope EmailStateUpdate(string? inboxId)
	{
		return new ProtocolEnvelope
		{
			Type = MessageTypes.StateUpdate,
			Id = Guid.NewGuid().ToString("N"),
			Payload = JsonSerializer.SerializeToElement(
				new StateUpdatePayload { Kind = CapabilityKinds.EmailInbox, LocalId = inboxId },
				PluginProtocolJson.Options)
		};
	}

	[Test]
	public void HandleStateUpdateAsync_With_A_Malformed_Payload_Does_Not_Throw()
	{
		const string pluginId = "com.example.plugin";
		var (endpoint, _, _, _, _, _) = CreateEndpointWithState(pluginId, "session-1");

		Assert.DoesNotThrowAsync(async () => await endpoint.HandleStateUpdateAsync(pluginId,
			new ProtocolEnvelope { Type = MessageTypes.StateUpdate, Id = "1" },
			CancellationToken.None));
	}

	[Test]
	public void HandleStateUpdateAsync_For_A_Plugin_With_No_Session_Does_Not_Throw()
	{
		var (endpoint, _, _, _, _, _) = CreateEndpointWithState("com.example.plugin", "session-1");

		Assert.DoesNotThrowAsync(async () => await endpoint.HandleStateUpdateAsync("com.example.unknown",
			new ProtocolEnvelope
			{
				Type = MessageTypes.StateUpdate,
				Id = "1",
				Payload = JsonSerializer.SerializeToElement(new StateUpdatePayload { Kind = CapabilityKinds.Variables },
					PluginProtocolJson.Options)
			},
			CancellationToken.None));
	}

	[Test]
	public async Task Concurrent_state_update_for_the_same_kind_coalesces_rather_than_piling_up()
	{
		// Regression for issue #413 finding 3: state.update was entirely unthrottled, so a plugin
		// sending it back to back could trigger a fresh describe round trip and snapshot save per
		// message. Three state.update messages for the same kind, sent faster than the first round trip
		// completes, should produce at most two describe calls - the one already running, plus one more
		// that reflects everything that arrived while it was in flight.
		const string pluginId = "com.example.plugin";
		var (endpoint, _, _, invoker, _, _) = CreateEndpointWithState(pluginId, "session-1");
		invoker.Gate = new SemaphoreSlim(0);

		var envelope = new ProtocolEnvelope
		{
			Type = MessageTypes.StateUpdate,
			Id = "1",
			Payload = JsonSerializer.SerializeToElement(new StateUpdatePayload { Kind = CapabilityKinds.Variables },
				PluginProtocolJson.Options)
		};

		var first = endpoint.HandleStateUpdateAsync(pluginId, envelope, CancellationToken.None);
		await WaitForAsync(() => invoker.InvokeCount >= 1);

		var second = endpoint.HandleStateUpdateAsync(pluginId, envelope, CancellationToken.None);
		var third = endpoint.HandleStateUpdateAsync(pluginId, envelope, CancellationToken.None);

		await Task.WhenAll(second, third).WaitAsync(TimeSpan.FromSeconds(5));
		Assert.That(invoker.InvokeCount, Is.EqualTo(1), "coalesced callers must not trigger their own round trip");

		invoker.Gate.Release(1);
		await WaitForAsync(() => invoker.InvokeCount >= 2);
		invoker.Gate.Release(1);

		await first;

		Assert.That(invoker.InvokeCount,
			Is.EqualTo(2),
			"three state.update calls in flight together should produce at most two describe round trips");
	}

	[Test]
	public async Task HandleCapabilityDeclareAsync_Reregisters_Through_The_Registrar_And_Acks()
	{
		const string pluginId = "com.example.plugin";
		var (endpoint, connection, _, invoker, registrar, _) = CreateEndpointWithState(pluginId, "session-1");
		invoker.VariablesDescribeResult = new VariableCatalogPayload { DeclaredVariables = [], Variables = [] };

		var declared = new CapabilityDeclarePayload
		{
			Capabilities =
			[
				new DeclaredCapability
				{
					Kind = CapabilityKinds.Variables, LocalId = "gpu-temp",
					VersionRange = new CapabilityVersionRange { Minimum = 1, Maximum = 1 }
				}
			]
		};

		await endpoint.HandleCapabilityDeclareAsync(connection,
			pluginId,
			new ProtocolEnvelope
			{
				Type = MessageTypes.CapabilityDeclare,
				Id = "1",
				Payload = JsonSerializer.SerializeToElement(declared, PluginProtocolJson.Options)
			},
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(connection.Sent.Single().Type, Is.EqualTo(MessageTypes.CapabilityDeclareAck));
			Assert.That(registrar.UnregisteredPluginIds, Does.Contain(pluginId));
			Assert.That(registrar.RegisteredPluginIds, Does.Contain(pluginId));
		});
	}

	[Test]
	public void HandleCapabilityDeclareAsync_For_A_Plugin_With_No_Session_Does_Not_Throw()
	{
		var (endpoint, connection, _, _, registrar, _) = CreateEndpointWithState("com.example.plugin", "session-1");

		var declared = new CapabilityDeclarePayload { Capabilities = [] };

		Assert.DoesNotThrowAsync(async () => await endpoint.HandleCapabilityDeclareAsync(connection,
			"com.example.unknown",
			new ProtocolEnvelope
			{
				Type = MessageTypes.CapabilityDeclare,
				Id = "1",
				Payload = JsonSerializer.SerializeToElement(declared, PluginProtocolJson.Options)
			},
			CancellationToken.None));

		Assert.Multiple(() =>
		{
			Assert.That(connection.Sent, Is.Empty);
			Assert.That(registrar.RegisteredPluginIds, Is.Empty);
		});
	}

	[Test]
	public void Event_publish_delivers_object_and_array_parameters_as_their_json_text()
	{
		var events = new FakeEventBus();
		var (endpoint, _, _) = CreateEndpoint(events);
		var parameters = JsonSerializer.SerializeToElement(new Dictionary<string, object?>
			{
				["combo"] = new { modifiers = new[] { "Ctrl", "Shift" }, key = "F3" },
				["keys"] = new[] { "A", "B" },
				["count"] = 3,
				["label"] = "text",
				["missing"] = null
			},
			PluginProtocolJson.Options);

		endpoint.HandleEventPublish("com.example.plugin",
			new ProtocolEnvelope
			{
				Type = MessageTypes.EventPublish,
				Id = "1",
				Payload = JsonSerializer.SerializeToElement(
					new EventPublishPayload { EventId = "hotkey-pressed", Parameters = parameters },
					PluginProtocolJson.Options)
			});

		Assert.That(events.Reader.TryRead(out var occurrence), Is.True);
		Assert.Multiple(() =>
		{
			Assert.That(occurrence!.Parameters["combo"], Is.EqualTo("""{"modifiers":["Ctrl","Shift"],"key":"F3"}"""));
			Assert.That(occurrence.Parameters["keys"], Is.EqualTo("""["A","B"]"""));
			Assert.That(occurrence.Parameters["count"], Is.EqualTo(3L));
			Assert.That(occurrence.Parameters["label"], Is.EqualTo("text"));
			Assert.That(occurrence.Parameters["missing"], Is.Null);
		});
	}

	[Test]
	public void TryEnqueue_Overflows_Once_MaxInboundQueueDepth_Is_Reached()
	{
		var dispatch = new PluginWebSocketEndpoint.InboundDispatchState();

		for (var i = 0; i < ProtocolLimits.MaxInboundQueueDepth; i++)
		{
			var enqueued = PluginWebSocketEndpoint.TryEnqueue(dispatch,
				new ProtocolEnvelope
					{ Type = MessageTypes.EventPublish, Id = i.ToString(CultureInfo.InvariantCulture) });
			Assert.That(enqueued, Is.True, $"message {i} of {ProtocolLimits.MaxInboundQueueDepth} should have fit");
		}

		var overflowed = PluginWebSocketEndpoint.TryEnqueue(dispatch,
			new ProtocolEnvelope { Type = MessageTypes.EventPublish, Id = "overflow" });

		Assert.That(overflowed, Is.False, "the queue is exactly full and must reject rather than stall");
	}

	[Test]
	public async Task ApplyBackpressureAsync_Sends_FlowPause_Once_The_High_Watermark_Is_Crossed()
	{
		var dispatch = new PluginWebSocketEndpoint.InboundDispatchState { Depth = ProtocolLimits.QueueHighWatermark };
		var connection = new FakePluginConnection();

		await PluginWebSocketEndpoint.ApplyBackpressureAsync(connection, dispatch, CancellationToken.None);

		Assert.That(connection.Sent.Single().Type, Is.EqualTo(MessageTypes.FlowPause));
	}

	[Test]
	public async Task ApplyBackpressureAsync_Does_Not_Repeat_FlowPause_While_Still_Above_The_High_Watermark()
	{
		var dispatch = new PluginWebSocketEndpoint.InboundDispatchState { Depth = ProtocolLimits.QueueHighWatermark };
		var connection = new FakePluginConnection();

		await PluginWebSocketEndpoint.ApplyBackpressureAsync(connection, dispatch, CancellationToken.None);
		dispatch.Depth = ProtocolLimits.QueueHighWatermark + 1;
		await PluginWebSocketEndpoint.ApplyBackpressureAsync(connection, dispatch, CancellationToken.None);

		Assert.That(connection.Sent, Has.Count.EqualTo(1));
	}

	[Test]
	public async Task ApplyBackpressureAsync_Sends_FlowResume_Once_The_Low_Watermark_Is_Reached()
	{
		var dispatch = new PluginWebSocketEndpoint.InboundDispatchState { Depth = ProtocolLimits.QueueHighWatermark };
		var connection = new FakePluginConnection();
		await PluginWebSocketEndpoint.ApplyBackpressureAsync(connection, dispatch, CancellationToken.None);

		dispatch.Depth = ProtocolLimits.QueueLowWatermark;
		await PluginWebSocketEndpoint.ApplyBackpressureAsync(connection, dispatch, CancellationToken.None);

		Assert.That(connection.Sent.Select(e => e.Type),
			Is.EqualTo(new[] { MessageTypes.FlowPause, MessageTypes.FlowResume }));
	}

	[Test]
	public void A_log_flood_neither_grows_without_bound_nor_consumes_capability_queue_depth()
	{
		var dispatch = new PluginWebSocketEndpoint.InboundDispatchState();
		var floodCount = ProtocolLimits.MaxLogInboundQueueDepth * 4;

		for (var i = 0; i < floodCount; i++)
		{
			dispatch.LogQueue.Writer.TryWrite(new ProtocolEnvelope
				{ Type = MessageTypes.LogPublish, Id = i.ToString(CultureInfo.InvariantCulture) });
		}

		Assert.Multiple(() =>
		{
			Assert.That(dispatch.Depth,
				Is.EqualTo(0),
				"the log lane must never touch the capability queue's depth counter");
			Assert.That(dispatch.LogQueue.Reader.Count,
				Is.LessThanOrEqualTo(ProtocolLimits.MaxLogInboundQueueDepth),
				"log traffic must stay bounded even when far more arrives than the queue can hold");

			for (var i = 0; i < ProtocolLimits.MaxInboundQueueDepth; i++)
			{
				Assert.That(PluginWebSocketEndpoint.TryEnqueue(dispatch,
						new ProtocolEnvelope
						{
							Type = MessageTypes.EventPublish, Id = i.ToString(CultureInfo.InvariantCulture)
						}),
					Is.True,
					$"capability queue slot {i} should still be fully available after the log flood");
			}
		});
	}

	[Test]
	public async Task An_oversized_log_batch_is_refused_without_partial_ingestion_and_the_connection_survives()
	{
		var (endpoint, connection, sink) = CreateEndpointWithLogIngestor();
		const string pluginId = "com.example.plugin";

		var oversized = new LogPublishPayload
		{
			Events = Enumerable.Range(0, ProtocolLimits.MaxLogEventsPerBatch + 1)
				.Select(i => NewLogEvent($"event-{i}"))
				.ToList()
		};

		await endpoint.HandleLogPublishAsync(connection,
			pluginId,
			"session-1",
			new ProtocolEnvelope
			{
				Type = MessageTypes.LogPublish,
				Id = "1",
				Payload = JsonSerializer.SerializeToElement(oversized, PluginProtocolJson.Options)
			},
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(connection.Sent.Single().Error?.Code, Is.EqualTo(ProtocolErrorCodes.PayloadTooLarge));
			Assert.That(connection.Closes, Is.Empty, "log volume alone must never close the connection");
			Assert.That(sink.Events, Is.Empty, "an oversized batch must not ingest even a truncated prefix");
		});

		var exact = new LogPublishPayload
		{
			Events = Enumerable.Range(0, ProtocolLimits.MaxLogEventsPerBatch)
				.Select(i => NewLogEvent($"event-{i}"))
				.ToList()
		};

		await endpoint.HandleLogPublishAsync(connection,
			pluginId,
			"session-1",
			new ProtocolEnvelope
			{
				Type = MessageTypes.LogPublish,
				Id = "2",
				Payload = JsonSerializer.SerializeToElement(exact, PluginProtocolJson.Options)
			},
			CancellationToken.None);

		Assert.That(sink.Events,
			Has.Count.EqualTo(ProtocolLimits.MaxLogEventsPerBatch),
			"the legal maximum batch size (the boundary just below the rejected size) must be ingested in full");
	}

	[Test]
	public async Task Forwarded_events_below_the_configured_minimum_are_not_written()
	{
		var (endpoint, connection, sink) = CreateEndpointWithLogIngestor(LogEventLevel.Warning);

		var payload = new LogPublishPayload { Events = [NewLogEvent("below the configured minimum")] };

		await endpoint.HandleLogPublishAsync(connection,
			"com.example.plugin",
			"session-1",
			new ProtocolEnvelope
			{
				Type = MessageTypes.LogPublish,
				Id = "1",
				Payload = JsonSerializer.SerializeToElement(payload, PluginProtocolJson.Options)
			},
			CancellationToken.None);

		Assert.That(sink.Events, Is.Empty, "an Information event must not be written under a Warning minimum");
	}

	private static LogEventDto NewLogEvent(string message, string level = LogLevels.Information)
	{
		return new LogEventDto
		{
			Timestamp = DateTimeOffset.UtcNow, Level = level, MessageTemplate = message, RenderedMessage = message
		};
	}

	private static (PluginWebSocketEndpoint Endpoint, FakePluginConnection Connection, CapturingLogSink Sink)
		CreateEndpointWithLogIngestor(LogEventLevel minimumLevel = LogEventLevel.Verbose)
	{
		var registry = new PluginSessionRegistry(TimeProvider.System, Serilog.Core.Logger.None);
		var invoker = new FakePluginCapabilityInvoker();
		var throttle = new LoginThrottle(TimeProvider.System);
		var statePusher
			= new HostStatePusher(registry,
				new EmptyDeckNavigator(),
				new EmptyScriptApi(),
				new EmptyWidgetApi(),
				new StubEventBindingTracker(),
				new MacroDeckHost.Application.Deck.DeckClientTracker(Serilog.Core.Logger.None),
				new Adb.FakeAdbManager(),
				new FixedAdbAccessPolicy(),
				Serilog.Core.Logger.None);

		var sink = new CapturingLogSink();
		var capturingLogger = new LoggerConfiguration().MinimumLevel.Is(minimumLevel).WriteTo.Sink(sink).CreateLogger();
		var ingestor = new PluginLogIngestor(new PluginLogRateLimiter(TimeProvider.System),
			registry,
			new FakePluginSupervisor(),
			TimeProvider.System,
			() => capturingLogger);

		var endpoint = new PluginWebSocketEndpoint(registry,
			invoker,
			new FakeRemotePluginIntegrationRegistrar(),
			new FakePluginCallbackRouter(),
			new PluginAssetReceiver(new InMemoryPluginAssetCache()),
			statePusher,
			new FakeEventBus(),
			throttle,
			TimeProvider.System,
			new RecordingMediator(),
			new NeverStoppingLifetime(),
			ingestor,
			Serilog.Core.Logger.None);

		return (endpoint, new FakePluginConnection(), sink);
	}

	private sealed class CapturingLogSink : Serilog.Core.ILogEventSink
	{
		private readonly object _gate = new();
		private readonly List<LogEvent> _events = [];

		public List<LogEvent> Events
		{
			get
			{
				lock (_gate)
				{
					return _events.ToList();
				}
			}
		}

		public void Emit(LogEvent logEvent)
		{
			lock (_gate)
			{
				_events.Add(logEvent);
			}
		}
	}

	private sealed class NeverStoppingLifetime : IHostApplicationLifetime
	{
		public CancellationToken ApplicationStarted => CancellationToken.None;
		public CancellationToken ApplicationStopping => CancellationToken.None;
		public CancellationToken ApplicationStopped => CancellationToken.None;

		public void StopApplication()
		{
		}
	}

	private sealed class FakePluginCapabilityInvoker : IPluginCapabilityInvoker
	{
		public bool CompleteResult { get; set; }

		public Task<JsonElement?> InvokeAsync(
			string pluginId,
			CapabilityInvokeRequest request,
			CancellationToken cancellationToken)
		{
			throw new NotSupportedException("Not exercised by these tests.");
		}

		public bool TryComplete(string pluginId, ProtocolEnvelope result)
		{
			return CompleteResult;
		}

		public void AbortAll(string pluginId, ProtocolError reason)
		{
		}

		public bool IsLiveActionExecute(string pluginId, string correlationId)
		{
			return false;
		}
	}

	private sealed class FakeRemotePluginIntegrationRegistrar : IRemotePluginIntegrationRegistrar
	{
		public Task<bool> RegisterAsync(string pluginId, CancellationToken cancellationToken = default)
		{
			return Task.FromResult(true);
		}

		public Task UnregisterAsync(string pluginId, CancellationToken cancellationToken = default)
		{
			return Task.CompletedTask;
		}

		public Task ForgetAsync(string pluginId, CancellationToken cancellationToken = default)
		{
			return Task.CompletedTask;
		}

		public Task RegisterInstalledButStoppedAsync(CancellationToken cancellationToken = default)
		{
			return Task.CompletedTask;
		}

		public Task UnregisterVanishedInstallationsAsync(CancellationToken cancellationToken = default)
		{
			return Task.CompletedTask;
		}

		public Task RegisterInstalledDetachedAsync(
			string pluginId,
			CancellationToken cancellationToken = default)
		{
			return Task.CompletedTask;
		}

		public Task ApplyRefreshedSnapshotAsync(
			string pluginId,
			RemotePluginCapabilitySnapshot snapshot,
			CancellationToken cancellationToken = default)
		{
			return Task.CompletedTask;
		}

		public Task ApplyStateUpdateAsync(
			string pluginId,
			string kind,
			CancellationToken cancellationToken = default)
		{
			return Task.CompletedTask;
		}

		public Task RefreshLocalizationCatalogsAsync(CancellationToken cancellationToken = default)
		{
			return Task.CompletedTask;
		}
	}

	private sealed class RecordingRegistrar : IRemotePluginIntegrationRegistrar
	{
		public List<string> RegisteredPluginIds { get; } = [];

		public List<string> UnregisteredPluginIds { get; } = [];

		public List<string> AppliedPluginIds { get; } = [];

		public RemotePluginSnapshotRefresher? Refresher { get; init; }

		public List<(string PluginId, string Kind)> StateUpdates { get; } = [];

		public Task ApplyStateUpdateAsync(string pluginId, string kind, CancellationToken cancellationToken = default)
		{
			lock (StateUpdates)
			{
				StateUpdates.Add((pluginId, kind));
			}

			return Refresher?.RefreshKindAsync(pluginId, kind, cancellationToken) ?? Task.CompletedTask;
		}

		public Task<bool> RegisterAsync(string pluginId, CancellationToken cancellationToken = default)
		{
			RegisteredPluginIds.Add(pluginId);
			return Task.FromResult(true);
		}

		public Task UnregisterAsync(string pluginId, CancellationToken cancellationToken = default)
		{
			UnregisteredPluginIds.Add(pluginId);
			return Task.CompletedTask;
		}

		public Task ForgetAsync(string pluginId, CancellationToken cancellationToken = default)
		{
			return UnregisterAsync(pluginId, cancellationToken);
		}

		public Task RegisterInstalledButStoppedAsync(CancellationToken cancellationToken = default)
		{
			return Task.CompletedTask;
		}

		public Task UnregisterVanishedInstallationsAsync(CancellationToken cancellationToken = default)
		{
			return Task.CompletedTask;
		}

		public Task RegisterInstalledDetachedAsync(
			string pluginId,
			CancellationToken cancellationToken = default)
		{
			return Task.CompletedTask;
		}

		public Task ApplyRefreshedSnapshotAsync(
			string pluginId,
			RemotePluginCapabilitySnapshot snapshot,
			CancellationToken cancellationToken = default)
		{
			AppliedPluginIds.Add(pluginId);
			return Task.CompletedTask;
		}

		public Task RefreshLocalizationCatalogsAsync(CancellationToken cancellationToken = default)
		{
			return Task.CompletedTask;
		}
	}

	private sealed class ScriptedCapabilityInvoker : IPluginCapabilityInvoker
	{
		private int _invokeCount;

		public VariableCatalogPayload? VariablesDescribeResult { get; set; }

		public EmailInboxesResult? EmailInboxesResult { get; set; }

		public SemaphoreSlim? Gate { get; set; }

		public int InvokeCount => Volatile.Read(ref _invokeCount);

		public async Task<JsonElement?> InvokeAsync(
			string pluginId,
			CapabilityInvokeRequest request,
			CancellationToken cancellationToken)
		{
			Interlocked.Increment(ref _invokeCount);

			if (Gate is { } gate)
			{
				await gate.WaitAsync(cancellationToken);
			}

			if (request.Kind == CapabilityKinds.Variables
				&& request.Operation == CapabilityOperations.Variables.Describe)
			{
				return JsonSerializer.SerializeToElement(
					VariablesDescribeResult ?? new VariableCatalogPayload { DeclaredVariables = [], Variables = [] },
					PluginProtocolJson.Options);
			}

			if (request.Kind == CapabilityKinds.EmailInbox
				&& request.Operation == CapabilityOperations.EmailInbox.Inboxes
				&& EmailInboxesResult is { } inboxes)
			{
				return JsonSerializer.SerializeToElement(inboxes, PluginProtocolJson.Options);
			}

			throw new NotSupportedException($"Not scripted for kind '{request.Kind}'.");
		}

		public bool TryComplete(string pluginId, ProtocolEnvelope result)
		{
			return false;
		}

		public void AbortAll(string pluginId, ProtocolError reason)
		{
		}

		public bool IsLiveActionExecute(string pluginId, string correlationId)
		{
			return false;
		}
	}

	private sealed class InMemorySnapshotStore : IRemotePluginSnapshotStore
	{
		private readonly Dictionary<string, RemotePluginCapabilitySnapshot> _byPluginId = new(StringComparer.Ordinal);

		public RemotePluginCapabilitySnapshot GetSnapshot(string pluginId)
		{
			return _byPluginId.TryGetValue(pluginId, out var snapshot)
				? snapshot
				: RemotePluginCapabilitySnapshot.Empty(pluginId);
		}

		public bool Has(string pluginId)
		{
			return _byPluginId.ContainsKey(pluginId);
		}

		public Task SaveAsync(RemotePluginCapabilitySnapshot snapshot, CancellationToken cancellationToken = default)
		{
			_byPluginId[snapshot.PluginId] = snapshot;
			return Task.CompletedTask;
		}

		public Task RemoveAsync(string pluginId, CancellationToken cancellationToken = default)
		{
			_byPluginId.Remove(pluginId);
			return Task.CompletedTask;
		}
	}

	private static async Task WaitForAsync(Func<bool> condition)
	{
		var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
		while (DateTime.UtcNow < deadline)
		{
			if (condition())
			{
				return;
			}

			await Task.Delay(10);
		}

		Assert.Fail("The expected state was never reached.");
	}
}
