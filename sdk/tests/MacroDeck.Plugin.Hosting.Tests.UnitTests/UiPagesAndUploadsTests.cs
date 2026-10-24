using System.Text;
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
public class UiPagesAndUploadsTests
{
	[Test]
	public async Task Describe_reports_the_pages_a_provider_declares_in_order()
	{
		await using var handler = Handler(new PageSession(),
			new ScriptedHost(),
			null,
			[
				new UiPageDeclaration { Id = "scripts", Title = "Scripts", IconName = "file-code" },
				new UiPageDeclaration { Id = "runs", Title = "Runs" }
			]);

		var described
			= (await handler.InvokeAsync(Invocation(CapabilityOperations.Ui.Describe, new { }), CancellationToken.None))
			.Data!.Value.Deserialize<UiDescribePayload>(PluginProtocolJson.Options)!;

		Assert.Multiple(() =>
		{
			Assert.That(described.Pages!.Select(page => page.Id), Is.EqualTo(new[] { "scripts", "runs" }));
			Assert.That(described.Pages![0].Title, Is.EqualTo((LocalizedText)"Scripts"));
			Assert.That(described.Pages![0].IconName, Is.EqualTo("file-code"));
			Assert.That(described.Pages![1].IconName, Is.Null);
		});
	}

	[Test]
	public async Task A_provider_written_before_pages_describes_none()
	{
		await using var handler = Handler(new PageSession(), new ScriptedHost(), null, null);

		var described
			= (await handler.InvokeAsync(Invocation(CapabilityOperations.Ui.Describe, new { }), CancellationToken.None))
			.Data!.Value.Deserialize<UiDescribePayload>(PluginProtocolJson.Options)!;

		Assert.That(described.Pages, Is.Empty);
	}

	[Test]
	public async Task An_upload_is_read_from_the_session_the_event_came_from_and_arrives_over_the_transfer()
	{
		var text = Encoding.UTF8.GetBytes("MsgBox \"hi\"");
		var host = new ScriptedHost
		{
			Answer = new UiOriginReadUploadResult
			{
				Found = true, ByteLength = text.Length, ContentHash = "sha256:00", TransferId = "t1"
			}
		};
		var assets = new ScriptedHostAssets();
		assets.Transfers["t1"] = new HostAssetTransfer(true, "text/plain", text);
		var session = new PageSession();
		await using var handler = Handler(session, host, assets, null);
		await OpenAndRaiseAsync(handler);
		var origin = await session.NextOriginAsync();

		var read = await origin.ReadUploadAsync("u1");

		var arguments = (UiOriginReadUploadArguments)host.Calls
			.Single(call => call.Operation == HostOperations.Ui.OriginReadUpload)
			.Arguments!;
		Assert.Multiple(() =>
		{
			Assert.That(read.ToArray(), Is.EqualTo(text));
			Assert.That(arguments.SessionId, Is.EqualTo("s1"));
			Assert.That(arguments.UploadId, Is.EqualTo("u1"));
		});
	}

	[Test]
	public async Task An_upload_the_host_does_not_hold_for_this_session_is_not_found()
	{
		var host = new ScriptedHost { Answer = new UiOriginReadUploadResult { Found = false } };
		var session = new PageSession();
		await using var handler = Handler(session, host, new ScriptedHostAssets(), null);
		await OpenAndRaiseAsync(handler);
		var origin = await session.NextOriginAsync();

		Assert.ThrowsAsync<UiUploadNotFoundException>(() => origin.ReadUploadAsync("u1"));
	}

	[Test]
	public async Task A_macro_deck_that_predates_uploads_is_reported_as_not_supported()
	{
		var host = new ScriptedHost
		{
			Failure = HostInvocationException.CreateNonRetryable(ProtocolErrorCodes.CapabilityUnsupported, "no")
		};
		var session = new PageSession();
		await using var handler = Handler(session, host, new ScriptedHostAssets(), null);
		await OpenAndRaiseAsync(handler);
		var origin = await session.NextOriginAsync();

		Assert.ThrowsAsync<NotSupportedException>(() => origin.ReadUploadAsync("u1"));
	}

	private static UiCapabilityHandler Handler(
		PageSession session,
		ScriptedHost host,
		IPluginHostAssetReceiver? assets,
		IReadOnlyList<UiPageDeclaration>? pages)
	{
		IPluginIntegration integration = pages is null
			? new IntegrationWithoutPages(session)
			: new IntegrationWithPages(session, pages);

		return new UiCapabilityHandler([integration],
			host,
			Serilog.Core.Logger.None,
			new PluginConfigFlowSessions(TimeProvider.System),
			new ModalResultStore(),
			null,
			null,
			assets);
	}

	private static async Task OpenAndRaiseAsync(UiCapabilityHandler handler)
	{
		await handler.InvokeAsync(Invocation(CapabilityOperations.Ui.SessionOpen,
				new UiSessionOpenArguments
				{
					SessionId = "s1",
					SurfaceKind = UiSurfaceKinds.Page,
					SessionMode = UiSessionModes.Exclusive,
					UiModelVersion = 4
				}),
			CancellationToken.None);

		await handler.InvokeAsync(Invocation(CapabilityOperations.Ui.SessionEvent,
				new UiSessionEventArguments
				{
					SessionId = "s1", NodeId = "root", Name = "content-uploaded", ClientId = "admin"
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

	private sealed class ScriptedHostAssets : IPluginHostAssetReceiver
	{
		public Dictionary<string, HostAssetTransfer> Transfers { get; } = new(StringComparer.Ordinal);

		public Task<HostAssetTransfer> AwaitAsync(string transferId, CancellationToken cancellationToken)
		{
			return Task.FromResult(Transfers[transferId]);
		}

		public ProtocolEnvelope Handle(ProtocolEnvelope envelope)
		{
			throw new NotSupportedException();
		}

		public void Reset()
		{
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
	}

	private sealed class IntegrationWithoutPages(PageSession session) : IPluginIntegration, IUiProvider
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

	private sealed class IntegrationWithPages(PageSession session, IReadOnlyList<UiPageDeclaration> pages)
		: IPluginIntegration, IUiProvider
	{
		public IReadOnlyList<IActionDefinition> Actions => [];

		public IReadOnlyList<UiSurfaceDeclaration> Surfaces =>
			[new() { Kind = UiSurfaceKinds.Page, SessionMode = UiSessionModes.Exclusive }];

		public IReadOnlyList<UiPageDeclaration> Pages => pages;

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

	private sealed class PageSession : IUiSession
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
				Surface = new UiSurface { Kind = UiSurfaceKinds.Page, SessionMode = UiSessionModes.Exclusive },
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
