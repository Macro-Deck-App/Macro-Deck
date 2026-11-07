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
using MacroDeck.Ui.Config;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Model.Events;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Patches;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Runtime;

namespace MacroDeck.Plugin.Hosting.Tests.UnitTests;

[TestFixture]
public class UiSessionEventNullDataTests
{
	[Test]
	public async Task A_change_to_json_null_clears_a_nullable_input_in_a_plugin_view()
	{
		var icon = new UiState<string>("mouse");
		var session = new ViewSession(surface => Config(surface,
			new UiStringInput
			{
				Key = "icon",
				Label = "Icon",
				Binding = Bind.To(icon)
			}));
		await using var handler = await OpenAsync(session);

		await SendAsync(handler, """{"sessionId":"s1","nodeId":"icon","name":"change","data":null}""");
		var result = await session.NextResultAsync();

		Assert.Multiple(() =>
		{
			Assert.That(result.Outcome, Is.EqualTo(UiDispatchOutcome.Accepted), result.Reason);
			Assert.That(icon.Peek(), Is.Null);
		});
	}

	[Test]
	public async Task A_change_without_data_is_still_refused()
	{
		var icon = new UiState<string>("mouse");
		var session = new ViewSession(surface => Config(surface,
			new UiStringInput
			{
				Key = "icon",
				Label = "Icon",
				Binding = Bind.To(icon)
			}));
		await using var handler = await OpenAsync(session);

		await SendAsync(handler, """{"sessionId":"s1","nodeId":"icon","name":"change"}""");
		var result = await session.NextResultAsync();

		Assert.Multiple(() =>
		{
			Assert.That(result.Outcome, Is.EqualTo(UiDispatchOutcome.Rejected));
			Assert.That(icon.Peek(), Is.EqualTo("mouse"));
		});
	}

	[Test]
	public async Task A_change_to_json_null_is_still_refused_for_a_value_that_cannot_be_null()
	{
		var level = new UiState<double>(0.5);
		var session = new ViewSession(surface => Config(surface,
			new UiNumberInput
			{
				Key = "level",
				Label = "Level",
				Binding = Bind.To(level)
			}));
		await using var handler = await OpenAsync(session);

		await SendAsync(handler, """{"sessionId":"s1","nodeId":"level","name":"change","data":null}""");
		var result = await session.NextResultAsync();

		Assert.Multiple(() =>
		{
			Assert.That(result.Outcome, Is.EqualTo(UiDispatchOutcome.Rejected));
			Assert.That(level.Peek(), Is.EqualTo(0.5));
		});
	}

	private static UiView Config(UiSurface surface, UiElement input)
	{
		return new UiView(surface, new UiConfigStack { Key = "settings", Children = [input] });
	}

	private static async Task<UiCapabilityHandler> OpenAsync(ViewSession session)
	{
		var handler = new UiCapabilityHandler([new Integration(session)],
			new NoOpHostInvoker(),
			Serilog.Core.Logger.None,
			new PluginConfigFlowSessions(TimeProvider.System),
			new ModalResultStore());

		await handler.InvokeAsync(Invocation(CapabilityOperations.Ui.SessionOpen,
				JsonSerializer.SerializeToElement(new UiSessionOpenArguments
					{
						SessionId = "s1",
						SurfaceKind = UiSurfaceKinds.Config,
						SessionMode = UiSessionModes.Exclusive,
						UiModelVersion = 1
					},
					PluginProtocolJson.Options)),
			CancellationToken.None);

		return handler;
	}

	private static Task<CapabilityInvocationResult> SendAsync(UiCapabilityHandler handler, string hostWire)
	{
		using var document = JsonDocument.Parse(hostWire);

		return handler.InvokeAsync(Invocation(CapabilityOperations.Ui.SessionEvent, document.RootElement.Clone()),
			CancellationToken.None);
	}

	private static CapabilityInvocation Invocation(string operation, JsonElement arguments)
	{
		return new CapabilityInvocation
		{
			Kind = CapabilityKinds.Ui,
			LocalId = ProviderCapabilityId.LocalId,
			Operation = operation,
			Arguments = arguments,
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

	private sealed class NoOpHostInvoker : IHostInvoker
	{
		public Task<JsonElement?> InvokeAsync(
			string api,
			string operation,
			object? arguments,
			CancellationToken cancellationToken)
		{
			return Task.FromResult<JsonElement?>(null);
		}

		public bool TryComplete(ProtocolEnvelope result)
		{
			return false;
		}
	}

	private sealed class Integration(ViewSession session) : IPluginIntegration, IUiProvider
	{
		public IReadOnlyList<IActionDefinition> Actions => [];

		public IReadOnlyList<UiSurfaceDeclaration> Surfaces =>
		[
			new()
				{ Kind = UiSurfaceKinds.Config, SessionMode = UiSessionModes.Exclusive }
		];

		public Task InitializeAsync(IIntegrationContext context)
		{
			return Task.CompletedTask;
		}

		public Task ShutdownAsync()
		{
			return Task.CompletedTask;
		}

		public Task<IUiSession?> CreateSessionAsync(UiSessionRequest request, CancellationToken cancellationToken)
		{
			session.Open(request.Surface);
			return Task.FromResult<IUiSession?>(session);
		}
	}

	private sealed class ViewSession(Func<UiSurface, UiView> build) : IUiSession
	{
		private readonly TaskCompletionSource<UiDispatchResult> _result =
			new(TaskCreationOptions.RunContinuationsAsynchronously);

		private UiView? _view;

		public event EventHandler? Changed;

		public event EventHandler<UiSessionFaultedEventArgs>? Faulted
		{
			add { }
			remove { }
		}

		public void Open(UiSurface surface)
		{
			_view = build(surface);
			_view.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
		}

		public Task<UiDispatchResult> NextResultAsync()
		{
			return _result.Task.WaitAsync(TimeSpan.FromSeconds(10));
		}

		public UiTree BuildTree()
		{
			return _view!.Tree;
		}

		public IReadOnlyList<UiPatch> DrainPatches()
		{
			return _view!.DrainPatches();
		}

		public void Dispatch(UiEvent uiEvent)
		{
			_result.TrySetResult(_view!.Dispatch(uiEvent));
		}

		public ValueTask DisposeAsync()
		{
			_view?.Dispose();
			return ValueTask.CompletedTask;
		}
	}
}
