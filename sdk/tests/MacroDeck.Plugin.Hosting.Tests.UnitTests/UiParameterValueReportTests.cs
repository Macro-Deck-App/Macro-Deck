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
public class UiParameterValueReportTests
{
	[Test]
	public async Task A_flush_sent_right_after_an_event_answers_only_once_the_host_took_the_value_that_event_reported()
	{
		var host = new RecordingHost { ReportDelay = TimeSpan.FromMilliseconds(150) };
		var action = new ComposeAction();
		await using var handler = Handler(action, host);
		await OpenAsync(handler, "s1");

		var dispatched = await ChangeAsync(handler, "s1", "hello");
		var flushed = await FlushAsync(handler, "s1");

		var report = host.CompletedReports.Single();
		Assert.Multiple(() =>
		{
			Assert.That(dispatched.IsFailure, Is.False);
			Assert.That(flushed.IsFailure, Is.False);
			Assert.That(report.SessionId, Is.EqualTo("s1"));
			Assert.That(report.Values["message"].GetProperty("content").GetString(), Is.EqualTo("hello"));
		});
	}

	[Test]
	public async Task A_session_reopened_after_a_reload_reports_through_the_new_session_and_the_old_one_is_silent()
	{
		var host = new RecordingHost();
		var action = new ComposeAction();
		await using var handler = Handler(action, host);

		await OpenAsync(handler, "s1");
		var replaced = action.Sessions.Single();
		await handler.InvokeAsync(Invocation(CapabilityOperations.Ui.SessionClose,
				new UiSessionCloseArguments { SessionId = "s1", Reason = "reload" }),
			CancellationToken.None);
		await OpenAsync(handler, "s2");

		replaced.Report("message", Message("stale"));
		await ChangeAsync(handler, "s2", "fresh");
		await FlushAsync(handler, "s2");

		Assert.That(host.CompletedReports.Select(report => report.SessionId), Is.EqualTo(new[] { "s2" }));
	}

	[Test]
	public async Task A_host_that_predates_reports_is_asked_once_and_the_session_keeps_working()
	{
		var host = new RecordingHost
		{
			ReportFailure = HostInvocationException.CreateNonRetryable(ProtocolErrorCodes.CapabilityUnsupported,
				"no")
		};
		var action = new ComposeAction();
		await using var handler = Handler(action, host);
		await OpenAsync(handler, "s1");

		await ChangeAsync(handler, "s1", "one");
		await ChangeAsync(handler, "s1", "two");
		var flushed = await FlushAsync(handler, "s1");

		Assert.Multiple(() =>
		{
			Assert.That(flushed.IsFailure, Is.False);
			Assert.That(host.ReportAttempts, Is.EqualTo(1));
			Assert.That(action.Sessions.Single().Dispatched, Is.EqualTo(2));
		});
	}

	private static UiCapabilityHandler Handler(ComposeAction action, RecordingHost host)
	{
		return new UiCapabilityHandler([new ComposeIntegration(action)],
			host,
			Serilog.Core.Logger.None,
			new PluginConfigFlowSessions(TimeProvider.System),
			new ModalResultStore());
	}

	private static Task<CapabilityInvocationResult> OpenAsync(UiCapabilityHandler handler, string sessionId)
	{
		var attributes = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
		{
			[UiConfigSurfaceAttributes.EntryPoint]
				= JsonSerializer.SerializeToElement(UiConfigEntryPoints.ActionConfig),
			[UiConfigSurfaceAttributes.ActionId] = JsonSerializer.SerializeToElement(ComposeAction.ActionId)
		};

		return handler.InvokeAsync(Invocation(CapabilityOperations.Ui.SessionOpen,
				new UiSessionOpenArguments
				{
					SessionId = sessionId,
					SurfaceKind = UiSurfaceKinds.Config,
					SessionMode = UiSessionModes.Exclusive,
					SurfaceAttributes = JsonSerializer.SerializeToElement(attributes),
					UiModelVersion = 1
				}),
			CancellationToken.None);
	}

	private static Task<CapabilityInvocationResult> ChangeAsync(
		UiCapabilityHandler handler,
		string sessionId,
		string content)
	{
		return handler.InvokeAsync(Invocation(CapabilityOperations.Ui.SessionEvent,
				new UiSessionEventArguments
				{
					SessionId = sessionId,
					NodeId = "content",
					Name = UiComponentEvents.Change,
					Data = JsonSerializer.SerializeToElement(content)
				}),
			CancellationToken.None);
	}

	private static Task<CapabilityInvocationResult> FlushAsync(UiCapabilityHandler handler, string sessionId)
	{
		return handler.InvokeAsync(Invocation(CapabilityOperations.Ui.SessionFlush,
				new UiSessionFlushArguments { SessionId = sessionId }),
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

	private static JsonElement Message(string content)
	{
		return JsonSerializer.SerializeToElement(new Dictionary<string, string> { ["content"] = content });
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
		private readonly List<UiReportParameterValuesArguments> _completed = [];
		private int _reportAttempts;

		public TimeSpan ReportDelay { get; init; }

		public Exception? ReportFailure { get; init; }

		public int ReportAttempts => Volatile.Read(ref _reportAttempts);

		public IReadOnlyList<UiReportParameterValuesArguments> CompletedReports
		{
			get
			{
				lock (_gate)
				{
					return [.. _completed];
				}
			}
		}

		public async Task<JsonElement?> InvokeAsync(
			string api,
			string operation,
			object? arguments,
			CancellationToken cancellationToken)
		{
			if (operation != HostOperations.Ui.ReportParameterValues)
			{
				return null;
			}

			Interlocked.Increment(ref _reportAttempts);
			await Task.Delay(ReportDelay, cancellationToken);

			if (ReportFailure is { } failure)
			{
				throw failure;
			}

			lock (_gate)
			{
				_completed.Add((UiReportParameterValuesArguments)arguments!);
			}

			return null;
		}

		public bool TryComplete(ProtocolEnvelope result)
		{
			return false;
		}
	}

	private sealed class ComposeIntegration(ComposeAction action) : IPluginIntegration
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

	private sealed class ComposeAction : IUiConfigurableActionDefinition
	{
		public const string ActionId = "compose";

		public List<ComposeSession> Sessions { get; } = [];

		public string Id => ActionId;

		public LocalizedText Name => "Compose";

		public LocalizedText Description => "Composes a message.";

		public IReadOnlyList<ActionParameter> Parameters { get; } =
		[
			new()
				{ Name = "message", Type = ActionParameterType.KeyValue }
		];

		public IActionExecutor CreateExecutor()
		{
			throw new NotSupportedException();
		}

		public Task<IUiSession?> CreateConfigurationSessionAsync(
			ActionConfigurationRequest request,
			CancellationToken cancellationToken)
		{
			var session = new ComposeSession();
			Sessions.Add(session);
			return Task.FromResult<IUiSession?>(session.Configuration);
		}
	}

	private sealed class ComposeSession : IUiSession
	{
		private int _dispatched;

		public ComposeSession()
		{
			Configuration = new ActionConfigurationSession(this);
		}

		public ActionConfigurationSession Configuration { get; }

		public int Dispatched => Volatile.Read(ref _dispatched);

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
				Surface = new UiSurface { Kind = UiSurfaceKinds.Config, SessionMode = UiSessionModes.Exclusive },
				Root = new UiNode { Id = "root", Type = "panel" }
			};
		}

		public IReadOnlyList<UiPatch> DrainPatches()
		{
			return [];
		}

		public void Dispatch(UiEvent uiEvent)
		{
			Interlocked.Increment(ref _dispatched);
			Report("message", Message(uiEvent.Data?.GetString() ?? string.Empty));
		}

		public void Report(string name, JsonElement value)
		{
			Configuration.Report(name, value);
		}

		public ValueTask DisposeAsync()
		{
			return ValueTask.CompletedTask;
		}
	}
}
