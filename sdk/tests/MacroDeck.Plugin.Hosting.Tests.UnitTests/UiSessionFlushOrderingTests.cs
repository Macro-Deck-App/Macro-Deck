using System.Text.Json;
using MacroDeck.Localization;
using MacroDeck.Plugin.Hosting.Capabilities;
using MacroDeck.Plugin.Hosting.Capabilities.ConfigFlow;
using MacroDeck.Plugin.Hosting.Capabilities.Ui;
using MacroDeck.Plugin.Hosting.Transport;
using MacroDeck.Plugin.Protocol.Callbacks;
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
public class UiSessionFlushOrderingTests
{
	[Test]
	public async Task A_flush_queued_before_its_event_ran_answers_only_after_the_patch_and_value_that_event_produced()
	{
		var host = new RecordingHost();
		var action = new GatedAction();
		await using var handler = new UiCapabilityHandler([new GatedIntegration(action)],
			host,
			Serilog.Core.Logger.None,
			new PluginConfigFlowSessions(TimeProvider.System),
			new ModalResultStore());
		await OpenAsync(handler);

		await handler.InvokeAsync(Invocation(CapabilityOperations.Ui.SessionEvent,
				new UiSessionEventArguments
				{
					SessionId = "s1",
					NodeId = "content",
					Name = UiComponentEvents.Change,
					Data = JsonSerializer.SerializeToElement("hello")
				}),
			CancellationToken.None);
		var session = action.Session!;
		Assert.That(await session.Entered.WaitAsync(TimeSpan.FromSeconds(5)),
			Is.True,
			"The event never reached the session.");

		var flush = handler.InvokeAsync(Invocation(CapabilityOperations.Ui.SessionFlush,
				new UiSessionFlushArguments { SessionId = "s1" }),
			CancellationToken.None);
		session.Gate.Release();
		var flushed = await flush.WaitAsync(TimeSpan.FromSeconds(5));
		var sentBeforeTheAnswer = host.Operations;

		Assert.Multiple(() =>
		{
			Assert.That(flushed.IsFailure, Is.False);
			Assert.That(sentBeforeTheAnswer, Does.Contain(HostOperations.Ui.Patch));
			Assert.That(sentBeforeTheAnswer, Does.Contain(HostOperations.Ui.ReportParameterValues));
		});
	}

	private static Task<CapabilityInvocationResult> OpenAsync(UiCapabilityHandler handler)
	{
		var attributes = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
		{
			[UiConfigSurfaceAttributes.EntryPoint]
				= JsonSerializer.SerializeToElement(UiConfigEntryPoints.ActionConfig),
			[UiConfigSurfaceAttributes.ActionId] = JsonSerializer.SerializeToElement(GatedAction.ActionId)
		};

		return handler.InvokeAsync(Invocation(CapabilityOperations.Ui.SessionOpen,
				new UiSessionOpenArguments
				{
					SessionId = "s1",
					SurfaceKind = UiSurfaceKinds.Config,
					SessionMode = UiSessionModes.Exclusive,
					SurfaceAttributes = JsonSerializer.SerializeToElement(attributes),
					UiModelVersion = 1
				}),
			CancellationToken.None);
	}

	private static CapabilityInvocation Invocation(string operation, object arguments)
	{
		return new CapabilityInvocation
		{
			Kind = CapabilityKinds.Ui,
			LocalId = ProviderCapabilityId.LocalId,
			Operation = operation,
			Arguments = JsonSerializer.SerializeToElement(arguments, PluginProtocolJson.Options),
			CorrelationId = "c1",
			Services = new EmptyServiceProvider()
		};
	}

	private sealed class EmptyServiceProvider : IServiceProvider
	{
		public object? GetService(Type serviceType)
		{
			return null;
		}
	}

	private sealed class RecordingHost : IHostInvoker
	{
		private readonly Lock _gate = new();
		private readonly List<string> _operations = [];

		public IReadOnlyList<string> Operations
		{
			get
			{
				lock (_gate)
				{
					return [.. _operations];
				}
			}
		}

		public async Task<JsonElement?> InvokeAsync(
			string api,
			string operation,
			object? arguments,
			CancellationToken cancellationToken)
		{
			await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken);
			lock (_gate)
			{
				_operations.Add(operation);
			}

			return null;
		}

		public bool TryComplete(ProtocolEnvelope result)
		{
			return false;
		}
	}

	private sealed class GatedIntegration(GatedAction action) : IPluginIntegration
	{
		public IReadOnlyList<IActionDefinition> Actions => [action];

		public Task InitializeAsync(IIntegrationContext context)
		{
			return Task.CompletedTask;
		}

		public Task ShutdownAsync()
		{
			return Task.CompletedTask;
		}
	}

	private sealed class GatedAction : IUiConfigurableActionDefinition
	{
		public const string ActionId = "gated";

		public GatedSession? Session { get; private set; }

		public string Id => ActionId;

		public LocalizedText Name => "Gated";

		public LocalizedText Description => "Holds each event until released.";

		public IReadOnlyList<ActionParameter> Parameters { get; } =
		[
			new()
				{ Name = "message", Type = ActionParameterType.String }
		];

		public IActionExecutor CreateExecutor()
		{
			throw new NotSupportedException();
		}

		public Task<IUiSession?> CreateConfigurationSessionAsync(
			ActionConfigurationRequest request,
			CancellationToken cancellationToken)
		{
			Session = new GatedSession();
			return Task.FromResult<IUiSession?>(Session.Configuration);
		}
	}

	private sealed class GatedSession : IUiSession
	{
		private readonly Lock _gate = new();
		private readonly List<UiPatch> _pending = [];

		public GatedSession()
		{
			Configuration = new ActionConfigurationSession(this);
		}

		public ActionConfigurationSession Configuration { get; }

		public SemaphoreSlim Entered { get; } = new(0);

		public SemaphoreSlim Gate { get; } = new(0);

		public event EventHandler? Changed;

#pragma warning disable CS0067 // Required by IUiSession; this fake never faults.
		public event EventHandler<UiSessionFaultedEventArgs>? Faulted;
#pragma warning restore CS0067

		public UiTree BuildTree()
		{
			return new UiTree
			{
				Revision = 1,
				Surface = new UiSurface { Kind = UiSurfaceKinds.Config, SessionMode = UiSessionModes.Exclusive },
				Root = new UiNode { Id = "root", Type = "panel" }
			};
		}

		public IReadOnlyList<UiPatch> DrainPatches()
		{
			lock (_gate)
			{
				var drained = _pending.ToList();
				_pending.Clear();
				return drained;
			}
		}

		public void Dispatch(UiEvent uiEvent)
		{
			Entered.Release();
			Gate.Wait(TimeSpan.FromSeconds(5));
			lock (_gate)
			{
				_pending.Add(new UiPatch
				{
					FromRevision = 1,
					ToRevision = 2,
					Operations = [new UiPatchOperation { Op = UiPatchOperations.SetProperties, NodeId = "root" }]
				});
			}

			Changed?.Invoke(this, EventArgs.Empty);
			Configuration.Report("message", JsonSerializer.SerializeToElement(uiEvent.Data?.GetString()));
		}

		public ValueTask DisposeAsync()
		{
			return ValueTask.CompletedTask;
		}
	}
}
