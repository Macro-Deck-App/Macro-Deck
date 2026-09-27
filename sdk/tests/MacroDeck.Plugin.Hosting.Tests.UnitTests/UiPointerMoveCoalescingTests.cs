using System.Text.Json;
using MacroDeck.Plugin.Hosting.Capabilities;
using MacroDeck.Plugin.Hosting.Capabilities.ConfigFlow;
using MacroDeck.Plugin.Hosting.Capabilities.Ui;
using MacroDeck.Plugin.Hosting.Transport;
using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Capabilities.Ui;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Components;
using MacroDeck.Ui.Model.Events;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Patches;
using MacroDeck.Ui.Model.Surfaces;

namespace MacroDeck.Plugin.Hosting.Tests.UnitTests;

[TestFixture]
public class UiPointerMoveCoalescingTests
{
	private static readonly string[] _delivered = ["press", "d1", "d2", "m2", "up", "m4", "x2", "y2", "y3", "p"];

	private static readonly string[] _everyMove = ["m0", "m1", "m2", "m3", "m4"];

	[Test]
	public async Task A_slow_view_receives_the_newest_move_per_client_and_node_and_every_other_event_in_order()
	{
		var session = new SlowSession();
		await using var handler = await OpenAsync(session);

		await SendAsync(handler, UiComponentEvents.Press, "press", "a");
		Assert.That(session.Blocked.Wait(TimeSpan.FromSeconds(5)), Is.True, "The first event never reached the view.");

		await SendAsync(handler, UiComponentEvents.Drag, "d1", "a");
		await SendAsync(handler, UiComponentEvents.Drag, "d2", "a");
		await SendAsync(handler, UiComponentEvents.PointerMove, "m1", "a");
		await SendAsync(handler, UiComponentEvents.PointerMove, "m2", "a");
		await SendAsync(handler, UiComponentEvents.PointerUp, "up", "a");
		await SendAsync(handler, UiComponentEvents.PointerMove, "m3", "a");
		await SendAsync(handler, UiComponentEvents.PointerMove, "m4", "a");
		await SendAsync(handler, UiComponentEvents.PointerMove, "x1", null);
		await SendAsync(handler, UiComponentEvents.PointerMove, "x2", null);
		await SendAsync(handler, UiComponentEvents.PointerMove, "y1", "b");
		await SendAsync(handler, UiComponentEvents.PointerMove, "y2", "b");
		session.Emit();
		await SendAsync(handler, UiComponentEvents.PointerMove, "y3", "b");
		await SendAsync(handler, UiComponentEvents.Press, "p", "a");

		session.Release.Set();

		Assert.That(await session.WaitForAsync(_delivered.Length), Is.EqualTo(_delivered));
	}

	[Test]
	public async Task A_view_that_keeps_up_receives_every_move()
	{
		var session = new SlowSession();
		session.Release.Set();
		await using var handler = await OpenAsync(session);

		for (var step = 0; step < 5; step++)
		{
			await SendAsync(handler, UiComponentEvents.PointerMove, "m" + step, "a");
			await session.WaitForAsync(step + 1);
		}

		Assert.That(await session.WaitForAsync(5), Is.EqualTo(_everyMove));
	}

	private static async Task<UiCapabilityHandler> OpenAsync(SlowSession session)
	{
		var handler = new UiCapabilityHandler([new Integration(session)],
			new NoOpHostInvoker(),
			Serilog.Core.Logger.None,
			new PluginConfigFlowSessions(TimeProvider.System),
			new ModalResultStore());

		await handler.InvokeAsync(Invocation(CapabilityOperations.Ui.SessionOpen,
				new UiSessionOpenArguments
				{
					SessionId = "s1",
					SurfaceKind = UiSurfaceKinds.Widget,
					SessionMode = UiSessionModes.Shared,
					UiModelVersion = 1
				}),
			CancellationToken.None);

		return handler;
	}

	private static Task<CapabilityInvocationResult> SendAsync(UiCapabilityHandler handler,
		string name,
		string tag,
		string? clientId)
		=> handler.InvokeAsync(Invocation(CapabilityOperations.Ui.SessionEvent,
				new UiSessionEventArguments
				{
					SessionId = "s1",
					NodeId = "pad",
					Name = name,
					Data = JsonSerializer.SerializeToElement(tag),
					ClientId = clientId
				}),
			CancellationToken.None);

	private static CapabilityInvocation Invocation(string operation, object arguments)
		=> new()
		{
			Kind = CapabilityKinds.Ui,
			LocalId = ProviderCapabilityId.LocalId,
			Operation = operation,
			Arguments = JsonSerializer.SerializeToElement(arguments, PluginProtocolJson.Options),
			CorrelationId = "c1",
			Services = new EmptyServiceProvider()
		};

	private sealed class EmptyServiceProvider : IServiceProvider
	{
		public object? GetService(Type serviceType) => null;
	}

	private sealed class NoOpHostInvoker : IHostInvoker
	{
		public Task<JsonElement?> InvokeAsync(string api,
			string operation,
			object? arguments,
			CancellationToken cancellationToken)
			=> Task.FromResult<JsonElement?>(null);

		public bool TryComplete(ProtocolEnvelope result) => false;
	}

	private sealed class Integration(SlowSession session) : IPluginIntegration, IUiProvider
	{
		public IReadOnlyList<IActionDefinition> Actions => [];

		public IReadOnlyList<UiSurfaceDeclaration> Surfaces =>
			[new() { Kind = UiSurfaceKinds.Widget, SessionMode = UiSessionModes.Shared }];

		public Task InitializeAsync(IIntegrationContext context) => Task.CompletedTask;

		public Task ShutdownAsync() => Task.CompletedTask;

		public Task<IUiSession?> CreateSessionAsync(UiSessionRequest request, CancellationToken cancellationToken)
			=> Task.FromResult<IUiSession?>(session);
	}

	private sealed class SlowSession : IUiSession
	{
		private readonly Lock _gate = new();
		private readonly List<string> _dispatched = [];

		public ManualResetEventSlim Blocked { get; } = new();

		public ManualResetEventSlim Release { get; } = new();

		public event EventHandler? Changed;

		public event EventHandler<UiSessionFaultedEventArgs>? Faulted
		{
			add { }
			remove { }
		}

		public UiTree BuildTree() => new()
		{
			Revision = 1,
			Surface = new UiSurface { Kind = UiSurfaceKinds.Widget, SessionMode = UiSessionModes.Shared },
			Root = new UiNode { Id = "pad", Type = UiComponents.Stack }
		};

		public IReadOnlyList<UiPatch> DrainPatches() => [];

		public void Dispatch(UiEvent uiEvent)
		{
			Blocked.Set();
			Release.Wait();

			lock (_gate)
			{
				_dispatched.Add(uiEvent.Data!.Value.GetString()!);
			}
		}

		public void Emit() => Changed?.Invoke(this, EventArgs.Empty);

		public async Task<IReadOnlyList<string>> WaitForAsync(int count)
		{
			for (var attempt = 0; attempt < 500; attempt++)
			{
				lock (_gate)
				{
					if (_dispatched.Count >= count)
					{
						return _dispatched.ToArray();
					}
				}

				await Task.Delay(10);
			}

			lock (_gate)
			{
				return _dispatched.ToArray();
			}
		}

		public ValueTask DisposeAsync() => ValueTask.CompletedTask;
	}
}
