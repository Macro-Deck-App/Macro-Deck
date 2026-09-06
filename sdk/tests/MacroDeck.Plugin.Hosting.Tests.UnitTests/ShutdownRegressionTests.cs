using MacroDeck.Plugin.Hosting.Integrations;
using MacroDeck.Plugin.Hosting.Integrations.HostApis;
using MacroDeck.Plugin.Hosting.Tests.UnitTests.Support;
using MacroDeck.Plugin.Hosting.Transport;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Decks;
using MacroDeck.Sdk.Events;
using MacroDeck.Sdk.Notifications;
using MacroDeck.Sdk.Scripts;
using MacroDeck.Sdk.Variables;
using MacroDeck.Sdk.Widgets;
using MacroDeck.Plugin.Testing.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeck.Plugin.Hosting.Tests.UnitTests;

/// <summary>
/// Shutdown has a budget it does not set. A managed plugin is stopped by <c>session.goodbye</c>, close
/// code 4004, then the manifest's <c>shutdown.gracefulTimeoutSeconds</c> - clamped to 1-60, default 10 -
/// and then a process-tree kill; see
/// <c>docs/src/content/docs/guides/hosting.md#what-to-expect-at-shutdown</c>. The SDK's own teardown
/// therefore has to fit inside that budget without help from the plugin author, and it is the SDK, not
/// the author, that decides how long a call to a host that is no longer there waits.
/// </summary>
[TestFixture]
public class ShutdownRegressionTests
{
	/// <summary>
	/// The smallest graceful budget a manifest can ask for. Asserted against rather than the default,
	/// because a plugin whose manifest sits at the floor is entitled to the same clean exit - and a
	/// teardown that only fits inside the default is one manifest edit away from being hard-killed.
	/// </summary>
	private static readonly TimeSpan _smallestGracefulBudget = TimeSpan.FromSeconds(1);

	/// <summary>
	/// The documented lifecycle puts <c>InitializeAsync</c> on every connect that is not a resume, and a
	/// real integration reads from the host there - the fixture's first act is to read its config.
	/// A host that restarts is therefore routinely gone while a plugin is mid-<c>host.invoke</c>, and
	/// that call is holding the same lifecycle gate <c>StopAsync</c> has to take. Nothing about that
	/// ordinary sequence may push the plugin past the supervisor's grace period.
	/// </summary>
	[Test]
	public async Task A_connection_lost_during_InitializeAsync_does_not_delay_shutdown()
	{
		var state = new PluginConnectionState();
		var stateCache = new HostStateCache(new PluginConnectionState());
		var hostInvoker = new HostInvoker(state, TimeProvider.System, Serilog.Core.Logger.None);

		var integration = new ConfigReadingIntegration();
		var lifecycle = new IntegrationLifecycleHostedService([integration],
			TestMetadata.Default,
			new ConfigOnlyContext(new RemoteIntegrationConfig(hostInvoker)),
			new FakeDeviceProviderContext(),
			hostInvoker,
			new ServiceCollection().BuildServiceProvider(),
			new FakeLayoutProviderContext(),
			new FakeFolderViewProviderContext(),
			new FakeWidgetTypeProviderContext(),
			state,
			stateCache,
			Serilog.Core.Logger.None);

		await lifecycle.StartAsync(CancellationToken.None);

		await using var socket = new FakePluginSocket();
		var connection = new PluginSessionConnection(socket,
			TestSession.Create(),
			TestSession.Dispatcher(),
			state,
			TimeProvider.System,
			Serilog.Core.Logger.None,
			hostInvoker,
			stateCache);

		// Normally set by PluginConnectionHostedService once the handshake completes; set directly here
		// since this test drives the connection without that service.
		state.ActiveConnection = connection;

		socket.Push(new ProtocolEnvelope
		{
			Type = MessageTypes.SessionWelcome,
			Id = "welcome",
			Payload = FakePluginSocket.Payload(new SessionWelcomePayload { SessionId = "session-1", Resumed = false })
		});

		var run = connection.RunAsync(null, "instance-1", CancellationToken.None);
		await socket.NextAsync(MessageTypes.SessionHello);

		// The welcome initializes the integration, which reads its config: waiting for the host.invoke
		// is what makes "mid-call" a fact rather than a hope.
		await socket.NextAsync(MessageTypes.HostInvoke);

		// The host goes away without answering - a restart landing on top of an initialization.
		socket.CloseFromHost(1006);
		await run;

		// The supervisor's grace period starts here.
		var stop = lifecycle.StopAsync(CancellationToken.None);

		Assert.DoesNotThrowAsync(async () => await stop.WaitAsync(_smallestGracefulBudget),
			"Shutdown was still waiting on a host.invoke the dropped connection can never answer.");

		Assert.Multiple(() =>
		{
			// Retryable, so an author who wants to try again on the next connect can tell this apart
			// from a request the host refused on its merits.
			Assert.That(integration.Failure, Is.TypeOf<HostInvocationException>());
			Assert.That(((HostInvocationException)integration.Failure!).Retryable, Is.True);

			// Shutdown ran rather than returning early: a StopAsync that skipped the teardown would
			// also have been fast, and fast is not the property under test.
			Assert.That(integration.ShutdownCount, Is.EqualTo(1));
		});
	}

	/// <summary>Reads its config on initialization, the way the fixture does, and records how that
	/// turned out instead of rethrowing - the lifecycle service swallows the exception, so rethrowing
	/// would put the outcome out of the test's reach.</summary>
	private sealed class ConfigReadingIntegration : IPluginIntegration
	{
		public IReadOnlyList<IActionDefinition> Actions { get; } = [];

		public int ShutdownCount { get; private set; }

		/// <summary>What <c>InitializeAsync</c>'s host call threw, or null when it returned.</summary>
		public Exception? Failure { get; private set; }

		public async Task InitializeAsync(IIntegrationContext context)
		{
			try
			{
				await context.Config.GetEntriesAsync();
			}
			catch (Exception exception)
			{
				Failure = exception;
			}
		}

		public Task ShutdownAsync()
		{
			ShutdownCount++;
			return Task.CompletedTask;
		}
	}

	/// <summary>A context with a real config proxy and nothing else, since nothing else is reached.</summary>
	private sealed class ConfigOnlyContext(IIntegrationConfig config) : IIntegrationContext
	{
		public IIntegrationConfig Config { get; } = config;

		public IVariableApi Variables => throw new NotSupportedException();

		public IUserVariableApi UserVariables => throw new NotSupportedException();

		public IDeckNavigator Deck => throw new NotSupportedException();

		public IScriptApi Scripts => throw new NotSupportedException();

		public IWidgetApi Widgets => throw new NotSupportedException();

		public IEventPublisher Events => throw new NotSupportedException();

		public IUserNotifier Notifications => throw new NotSupportedException();
	}
}
