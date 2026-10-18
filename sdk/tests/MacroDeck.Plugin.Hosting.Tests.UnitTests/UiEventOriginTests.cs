using System.Text.Json;
using MacroDeck.Localization;
using MacroDeck.Plugin.Hosting.Capabilities;
using MacroDeck.Plugin.Hosting.Capabilities.ConfigFlow;
using MacroDeck.Plugin.Hosting.Capabilities.Ui;
using MacroDeck.Plugin.Hosting.Transport;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Callbacks.Ui;
using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Capabilities.Ui;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Errors;
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
public class UiEventOriginTests
{
	[Test]
	public async Task A_session_is_handed_the_client_that_raised_the_event()
	{
		var host = new ScriptedHost();
		var session = new OriginSession();
		await using var handler = await OpenAsync(session, host);

		await PressAsync(handler, "client-a");
		var origin = await session.NextOriginAsync();

		Assert.That(origin.ClientId, Is.EqualTo("client-a"));
	}

	[Test]
	public async Task A_failure_reported_after_the_handler_returned_names_the_session_and_the_client()
	{
		var host = new ScriptedHost { Answer = new UiOriginReportFailureResult { Delivered = true } };
		var session = new OriginSession();
		await using var handler = await OpenAsync(session, host);
		await PressAsync(handler, "client-a");
		var origin = await session.NextOriginAsync();

		var delivered = await origin.ReportFailureAsync(LocalizedText.FromLiteral("Offline."));

		var call = host.Calls.Single(entry => entry.Operation == HostOperations.Ui.OriginReportFailure);
		var arguments = (UiOriginReportFailureArguments)call.Arguments!;
		Assert.Multiple(() =>
		{
			Assert.That(delivered, Is.True);
			Assert.That(call.Api, Is.EqualTo(HostApis.Ui));
			Assert.That(arguments.SessionId, Is.EqualTo("s1"));
			Assert.That(arguments.ClientId, Is.EqualTo("client-a"));
			Assert.That(arguments.EventName, Is.EqualTo(UiComponentEvents.Press));
			Assert.That(arguments.Message.Literal, Is.EqualTo("Offline."));
		});
	}

	[Test]
	public async Task A_macro_deck_without_origins_answers_false_and_a_cancellation()
	{
		var host = new ScriptedHost
		{
			Failure = HostInvocationException.CreateNonRetryable(ProtocolErrorCodes.CapabilityUnsupported, "no")
		};
		var session = new OriginSession();
		await using var handler = await OpenAsync(session, host);
		await PressAsync(handler, "client-a");
		var origin = await session.NextOriginAsync();

		var reported = await origin.ReportFailureAsync(LocalizedText.FromLiteral("Offline."));
		var opened = await origin.ShowModalAsync(new ModalDefinition { ViewId = "details" });
		var answered = await origin.ShowModalAsync<string>(new ModalDefinition { ViewId = "details" });
		var linked = await origin.OpenUrlAsync(new Uri("https://example.com/article"));

		Assert.Multiple(() =>
		{
			Assert.That(reported, Is.False);
			Assert.That(opened, Is.False);
			Assert.That(answered.Cancelled, Is.True);
			Assert.That(linked, Is.False);
		});
	}

	[Test]
	public async Task A_link_opened_through_the_origin_names_the_session_the_client_and_the_url()
	{
		var host = new ScriptedHost { Answer = new UiOriginOpenUrlResult { Delivered = true } };
		var session = new OriginSession();
		await using var handler = await OpenAsync(session, host);
		await PressAsync(handler, "client-a");
		var origin = await session.NextOriginAsync();

		var delivered = await origin.OpenUrlAsync(new Uri("https://example.com/article?id=1"));

		var call = host.Calls.Single(entry => entry.Operation == HostOperations.Ui.OriginOpenUrl);
		var arguments = (UiOriginOpenUrlArguments)call.Arguments!;
		Assert.Multiple(() =>
		{
			Assert.That(delivered, Is.True);
			Assert.That(call.Api, Is.EqualTo(HostApis.Ui));
			Assert.That(arguments.SessionId, Is.EqualTo("s1"));
			Assert.That(arguments.ClientId, Is.EqualTo("client-a"));
			Assert.That(arguments.Url, Is.EqualTo("https://example.com/article?id=1"));
		});
	}

	[Test]
	public async Task A_link_that_is_not_http_is_refused_before_it_reaches_the_host()
	{
		var host = new ScriptedHost { Answer = new UiOriginOpenUrlResult { Delivered = true } };
		var session = new OriginSession();
		await using var handler = await OpenAsync(session, host);
		await PressAsync(handler, "client-a");
		var origin = await session.NextOriginAsync();

		Assert.Multiple(() =>
		{
			Assert.ThrowsAsync<ArgumentException>(() => origin.OpenUrlAsync(new Uri("javascript:alert(1)")));
			Assert.ThrowsAsync<ArgumentException>(() => origin.OpenUrlAsync(new Uri("file:///etc/passwd")));
			Assert.That(host.Calls.Where(call => call.Operation == HostOperations.Ui.OriginOpenUrl), Is.Empty);
		});
	}

	[Test]
	public async Task An_event_without_a_client_has_an_origin_that_reaches_nobody()
	{
		var host = new ScriptedHost();
		var session = new OriginSession();
		await using var handler = await OpenAsync(session, host);
		await PressAsync(handler, null);
		var origin = await session.NextOriginAsync();

		var reported = await origin.ReportFailureAsync(LocalizedText.FromLiteral("Offline."));

		Assert.Multiple(() =>
		{
			Assert.That(origin.ClientId, Is.Null);
			Assert.That(reported, Is.False);
			Assert.That(host.Calls.Where(call => call.Operation == HostOperations.Ui.OriginReportFailure), Is.Empty);
		});
	}

	[Test]
	public async Task A_dialog_opened_through_the_origin_waits_for_the_answer_modal_result_brings()
	{
		var results = new ModalResultStore();
		var host = new ScriptedHost { Answer = new UiOriginShowModalResult { Opened = true, ModalId = "m1" } };
		var session = new OriginSession();
		await using var handler = await OpenAsync(session, host, results);
		await PressAsync(handler, "client-a");
		var origin = await session.NextOriginAsync();

		var answer = origin.ShowModalAsync<string>(new ModalDefinition { ViewId = "details" });
		await host.WaitForAsync(HostOperations.Ui.OriginShowModal);
		results.Complete(new UiModalResultArguments
		{
			ModalId = "m1", Cancelled = false, Value = JsonSerializer.SerializeToElement("chosen")
		});

		var result = await answer.WaitAsync(TimeSpan.FromSeconds(5));
		var arguments = (UiOriginShowModalArguments)host.Calls
			.Single(call => call.Operation == HostOperations.Ui.OriginShowModal)
			.Arguments!;
		Assert.Multiple(() =>
		{
			Assert.That(result.Value, Is.EqualTo("chosen"));
			Assert.That(arguments.AwaitResult, Is.True);
			Assert.That(arguments.ViewId, Is.EqualTo("details"));
		});
	}

	private static async Task<UiCapabilityHandler> OpenAsync(
		OriginSession session,
		ScriptedHost host,
		ModalResultStore? results = null)
	{
		var handler = new UiCapabilityHandler([new Integration(session)],
			host,
			Serilog.Core.Logger.None,
			new PluginConfigFlowSessions(TimeProvider.System),
			results ?? new ModalResultStore());

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

	private static Task<CapabilityInvocationResult> PressAsync(UiCapabilityHandler handler, string? clientId)
	{
		return handler.InvokeAsync(Invocation(CapabilityOperations.Ui.SessionEvent,
				new UiSessionEventArguments
				{
					SessionId = "s1", NodeId = "root", Name = UiComponentEvents.Press, ClientId = clientId
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

	private sealed class ScriptedHost : IHostInvoker
	{
		private readonly Lock _gate = new();
		private readonly List<(string Api, string Operation, object? Arguments)> _calls = [];

		public object? Answer { get; init; }

		public Exception? Failure { get; init; }

		public IReadOnlyList<(string Api, string Operation, object? Arguments)> Calls
		{
			get
			{
				lock (_gate)
				{
					return [.. _calls];
				}
			}
		}

		public Task<JsonElement?> InvokeAsync(
			string api,
			string operation,
			object? arguments,
			CancellationToken cancellationToken)
		{
			lock (_gate)
			{
				_calls.Add((api, operation, arguments));
			}

			if (!operation.StartsWith("origin.", StringComparison.Ordinal))
			{
				return Task.FromResult<JsonElement?>(null);
			}

			if (Failure is { } failure)
			{
				return Task.FromException<JsonElement?>(failure);
			}

			return Task.FromResult<JsonElement?>(Answer is null
				? null
				: JsonSerializer.SerializeToElement(Answer, Answer.GetType(), PluginProtocolJson.Options));
		}

		public bool TryComplete(ProtocolEnvelope result)
		{
			return false;
		}

		public async Task WaitForAsync(string operation)
		{
			for (var attempt = 0; attempt < 500 && Calls.All(call => call.Operation != operation); attempt++)
			{
				await Task.Delay(10);
			}
		}
	}

	private sealed class Integration(OriginSession session) : IPluginIntegration, IUiProvider
	{
		public IReadOnlyList<IActionDefinition> Actions => [];

		public IReadOnlyList<UiSurfaceDeclaration> Surfaces =>
			[new() { Kind = UiSurfaceKinds.Widget, SessionMode = UiSessionModes.Shared }];

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
			return Task.FromResult<IUiSession?>(session);
		}
	}

	private sealed class OriginSession : IUiSession
	{
		private readonly TaskCompletionSource<IUiEventOrigin> _origin =
			new(TaskCreationOptions.RunContinuationsAsynchronously);

		public event EventHandler? Changed
		{
			add { }
			remove { }
		}

		public event EventHandler<UiSessionFaultedEventArgs>? Faulted
		{
			add { }
			remove { }
		}

		public UiTree BuildTree()
		{
			return new UiTree
			{
				Revision = 1,
				Surface = new UiSurface { Kind = UiSurfaceKinds.Widget, SessionMode = UiSessionModes.Shared },
				Root = new UiNode { Id = "root", Type = UiComponents.Stack }
			};
		}

		public IReadOnlyList<UiPatch> DrainPatches()
		{
			return [];
		}

		public void Dispatch(UiEvent uiEvent)
		{
			throw new InvalidOperationException("The origin overload is always used.");
		}

		public void Dispatch(UiEvent uiEvent, IUiEventOrigin origin)
		{
			_origin.TrySetResult(origin);
		}

		public Task<IUiEventOrigin> NextOriginAsync()
		{
			return _origin.Task.WaitAsync(TimeSpan.FromSeconds(5));
		}

		public ValueTask DisposeAsync()
		{
			return ValueTask.CompletedTask;
		}
	}
}
