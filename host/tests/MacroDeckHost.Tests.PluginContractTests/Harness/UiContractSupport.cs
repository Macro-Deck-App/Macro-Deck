using System.Text;
using System.Text.Json;
using MacroDeck.Plugin.Hosting.Capabilities;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Capabilities.Ui;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Plugin.Protocol.Versioning;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.UiSessions;
using MacroDeckHost.Ui;
using MacroDeck.Localization;

namespace MacroDeckHost.Tests.PluginContractTests.Harness;

/// <summary>
/// What each client observed, in order, with the exact bytes of any opaque payload. A copy of the
/// recorder the host unit tests use, for the same reason the callback fakes are copied: a sibling test
/// assembly is not a library.
/// </summary>
internal sealed class RecordingUiTransport : IUiTransport
{
	private readonly Lock _gate = new();
	private readonly List<Recorded> _all = [];
	private readonly Dictionary<string, HashSet<string>> _groups = new(StringComparer.Ordinal);

	public IReadOnlyList<Recorded> All
	{
		get
		{
			lock (_gate)
			{
				return [.. _all];
			}
		}
	}

	public IReadOnlyList<Recorded> For(string connectionId)
	{
		lock (_gate)
		{
			return [.. _all.Where(recorded => recorded.Recipients.Contains(connectionId, StringComparer.Ordinal))];
		}
	}

	public IReadOnlyList<T> MessagesFor<T>(string connectionId)
		where T : class
		=> [.. For(connectionId).Select(recorded => recorded.Message).OfType<T>()];

	public Task Send<T>(T message, CancellationToken cancellationToken = default)
		where T : class
	{
		lock (_gate)
		{
			_all.Add(new Recorded(message, null, [.. _groups.Values.SelectMany(members => members).Distinct()]));
		}

		return Task.CompletedTask;
	}

	public Task SendToGroup<T>(string group, T message, CancellationToken cancellationToken = default)
		where T : class
	{
		lock (_gate)
		{
			var members = _groups.TryGetValue(group, out var set) ? set.ToArray() : [];
			_all.Add(new Recorded(message, group, members));
		}

		return Task.CompletedTask;
	}

	public Task SendToConnection<T>(string connectionId, T message, CancellationToken cancellationToken = default)
		where T : class
	{
		lock (_gate)
		{
			_all.Add(new Recorded(message, null, [connectionId]));
		}

		return Task.CompletedTask;
	}

	public Task AddToGroup(string connectionId, string group, CancellationToken cancellationToken = default)
	{
		lock (_gate)
		{
			if (!_groups.TryGetValue(group, out var members))
			{
				members = new HashSet<string>(StringComparer.Ordinal);
				_groups[group] = members;
			}

			members.Add(connectionId);
		}

		return Task.CompletedTask;
	}

	public Task RemoveFromGroup(string connectionId, string group, CancellationToken cancellationToken = default)
	{
		lock (_gate)
		{
			if (_groups.TryGetValue(group, out var members))
			{
				members.Remove(connectionId);
			}
		}

		return Task.CompletedTask;
	}

	internal sealed record Recorded(object Message, string? Group, IReadOnlyList<string> Recipients)
	{
		public byte[]? Payload => Message switch
		{
			UiSessionTreeUpdatedEvent tree => tree.Tree.Utf8.ToArray(),
			UiSessionPatchedEvent patch => patch.Patch.Utf8.ToArray(),
			_ => null
		};
	}
}

/// <summary>The plugin side of the <c>ui</c> capability, standing in for what a real provider plugin
/// does: answer the five session operations, and push trees, patches and faults back over
/// <c>host.invoke</c>.</summary>
internal sealed class TestUiCapabilityHandler : ICapabilityHandler
{
	private static readonly CapabilityVersionRange _version = new() { Minimum = 1, Maximum = 1 };

	public string Kind => CapabilityKinds.Ui;

	public List<UiSessionOpenArguments> Opened { get; } = [];

	public List<UiSessionEventArguments> Events { get; } = [];

	public List<string> Closed { get; } = [];

	public List<string> SnapshotRequests { get; } = [];

	public List<UiModalResultArguments> ModalResults { get; } = [];

	/// <summary>Answers a snapshot request, typically by pushing raw bytes back through the link.</summary>
	public Func<string, Task>? OnSnapshotRequested { get; set; }

	public string SurfaceKind { get; set; } = "config";

	public string SessionMode { get; set; } = "exclusive";

	public IReadOnlyList<DeclaredCapability> DeclareCapabilities()
		=>
		[
			new()
			{
				Kind = CapabilityKinds.Ui, LocalId = ProviderCapabilityId.LocalId, VersionRange = _version
			}
		];

	public async Task<CapabilityInvocationResult> InvokeAsync(CapabilityInvocation invocation,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(invocation);

		switch (invocation.Operation)
		{
			case CapabilityOperations.Ui.Describe:
				return CapabilityInvocationResult.Ok(new UiDescribePayload
				{
					Surfaces = [new UiSurfaceDescriptorDto { Kind = SurfaceKind, SessionMode = SessionMode }],
					UiModelVersion = 1
				});

			case CapabilityOperations.Ui.SessionOpen:
			{
				var arguments = Read<UiSessionOpenArguments>(invocation);
				if (arguments is not null)
				{
					Opened.Add(arguments);
				}

				return CapabilityInvocationResult.Ok(new UiSessionOpenResult
				{
					Accepted = true, NegotiatedUiModelVersion = 1
				});
			}

			case CapabilityOperations.Ui.SessionClose:
			{
				var arguments = Read<UiSessionCloseArguments>(invocation);
				if (arguments is not null)
				{
					Closed.Add(arguments.SessionId);
				}

				return CapabilityInvocationResult.Ok();
			}

			case CapabilityOperations.Ui.SessionSnapshot:
			{
				var arguments = Read<UiSessionSnapshotArguments>(invocation);
				if (arguments is not null)
				{
					SnapshotRequests.Add(arguments.SessionId);

					if (OnSnapshotRequested is { } push)
					{
						await push(arguments.SessionId).ConfigureAwait(false);
					}
				}

				return CapabilityInvocationResult.Ok();
			}

			case CapabilityOperations.Ui.SessionEvent:
			{
				var arguments = Read<UiSessionEventArguments>(invocation);
				if (arguments is not null)
				{
					Events.Add(arguments);
				}

				return CapabilityInvocationResult.Ok();
			}

			case CapabilityOperations.Ui.ModalResult:
			{
				var arguments = Read<UiModalResultArguments>(invocation);
				if (arguments is not null)
				{
					ModalResults.Add(arguments);
				}

				return CapabilityInvocationResult.Ok();
			}

			default:
				return CapabilityInvocationResult.Failed(ProtocolErrorCodes.CapabilityUnsupported,
					$"The ui capability has no operation '{invocation.Operation}'.");
		}
	}

	private static T? Read<T>(CapabilityInvocation invocation)
		=> invocation.Arguments is { } arguments
			? arguments.Deserialize<T>(PluginProtocolJson.Options)
			: default;
}

/// <summary>Builds the bytes a plugin puts on the socket, with the payload spliced in verbatim.</summary>
internal static class UiWire
{
	public static byte[] HostInvoke(string operation, string sessionId, string member, string payloadJson)
		=> Encoding.UTF8.GetBytes("{\"type\":\"host.invoke\",\"id\":\"" +
			Guid.CreateVersion7().ToString("N") +
			"\",\"payload\":{\"api\":\"" +
			HostApis.Ui +
			"\",\"operation\":\"" +
			operation +
			"\",\"arguments\":{\"sessionId\":\"" +
			sessionId +
			"\",\"" +
			member +
			"\":" +
			payloadJson +
			"}}}");

	public static byte[] Snapshot(string sessionId, string treeJson)
		=> HostInvoke(HostOperations.Ui.Snapshot, sessionId, "tree", treeJson);

	public static byte[] Patch(string sessionId, string patchJson)
		=> HostInvoke(HostOperations.Ui.Patch, sessionId, "patch", patchJson);
}

/// <summary>An in-process integration that serves UI trees, so the same session script can be run
/// against a provider that has no plugin socket at all.</summary>
internal sealed class InProcessUiTestIntegration : MacroDeck.Sdk.IIntegration, MacroDeck.Sdk.Ui.IUiProvider
{
	private readonly string _surfaceKind;

	public InProcessUiTestIntegration(string id, InProcessUiTestSession session, string surfaceKind = "config")
	{
		Id = id;
		Session = session;
		_surfaceKind = surfaceKind;
	}

	public string Id { get; }

	public LocalizedText Name => Id;

	public string Version => "1.0.0";

	public IReadOnlyList<MacroDeck.Sdk.Actions.IActionDefinition> Actions => [];

	public bool IsInitialized => true;

	public InProcessUiTestSession Session { get; }

	public IReadOnlyList<MacroDeck.Sdk.Ui.UiSurfaceDeclaration> Surfaces =>
	[
		new()
			{ Kind = _surfaceKind, SessionMode = "exclusive" }
	];

	public Task InitializeAsync(MacroDeck.Sdk.IIntegrationContext context) => Task.CompletedTask;

	public Task ShutdownAsync() => Task.CompletedTask;

	public Task<MacroDeck.Sdk.Ui.IUiSession?> CreateSessionAsync(MacroDeck.Sdk.Ui.UiSessionRequest request,
		CancellationToken cancellationToken)
		=> Task.FromResult<MacroDeck.Sdk.Ui.IUiSession?>(Session);
}

internal sealed class InProcessUiTestSession : MacroDeck.Sdk.Ui.IUiSession
{
	private readonly Lock _gate = new();
	private readonly List<MacroDeck.Ui.Model.Patches.UiPatch> _pending = [];

	public required MacroDeck.Ui.Model.Nodes.UiTree Tree { get; init; }

	public List<MacroDeck.Ui.Model.Events.UiEvent> Dispatched { get; } = [];

	public event EventHandler? Changed;

	public event EventHandler<MacroDeck.Sdk.Ui.UiSessionFaultedEventArgs>? Faulted;

	public MacroDeck.Ui.Model.Nodes.UiTree BuildTree() => Tree;

	public IReadOnlyList<MacroDeck.Ui.Model.Patches.UiPatch> DrainPatches()
	{
		lock (_gate)
		{
			var drained = _pending.ToArray();
			_pending.Clear();
			return drained;
		}
	}

	public void Dispatch(MacroDeck.Ui.Model.Events.UiEvent uiEvent)
	{
		lock (_gate)
		{
			Dispatched.Add(uiEvent);
		}
	}

	public void Emit(MacroDeck.Ui.Model.Patches.UiPatch patch)
	{
		lock (_gate)
		{
			_pending.Add(patch);
		}

		Changed?.Invoke(this, EventArgs.Empty);
	}

	public void Fault() => Faulted?.Invoke(this, new MacroDeck.Sdk.Ui.UiSessionFaultedEventArgs("gone"));

	public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

internal sealed class StubIntegrationRegistry : MacroDeckHost.Application.Integrations.IIntegrationRegistry
{
	private readonly List<MacroDeck.Sdk.IIntegration> _integrations = [];

	public event EventHandler<MacroDeckHost.Application.Integrations.IntegrationAvailabilityChangedEventArgs>?
		AvailabilityChanged
		{
			add { }
			remove { }
		}

	public IReadOnlyList<MacroDeck.Sdk.IIntegration> Integrations => _integrations;

	public void Add(MacroDeck.Sdk.IIntegration integration) => _integrations.Add(integration);

	public MacroDeck.Sdk.Actions.IActionDefinition? FindAction(string integrationId, string actionId) => null;

	public MacroDeck.Sdk.Actions.IActionDefinition? FindAction(MacroDeck.Sdk.Identity.QualifiedId id) => null;

	public IReadOnlyList<MacroDeckHost.Application.Integrations.ActionDescriptor> GetActions(bool enabledOnly = true)
		=> [];

	public bool IsEnabled(string integrationId) => true;

	public void SetEnabled(string integrationId, bool enabled)
	{
	}

	public MacroDeckHost.Application.Integrations.IntegrationOrigin GetOrigin(string integrationId)
		=> Application.Integrations.IntegrationOrigin.BuiltIn;

	public Task<MacroDeckHost.Application.Integrations.IntegrationRegistrationResult> RegisterAsync(
		MacroDeck.Sdk.IIntegration integration,
		MacroDeckHost.Application.Integrations.IntegrationOrigin origin =
			Application.Integrations.IntegrationOrigin.BuiltIn,
		MacroDeckHost.Application.Integrations.IntegrationMetadata? metadata = null)
	{
		_integrations.Add(integration);
		return Task.FromResult(Application.Integrations.IntegrationRegistrationResult.Success);
	}

	public Task<bool> UnregisterAsync(string integrationId) => Task.FromResult(true);
}

/// <summary>
/// Serializes a push message exactly as the running UI WebSocket does.
/// </summary>
internal static class UiWebSocketEgress
{
	public static byte[] Frame<T>(T message)
		where T : class
	{
		return JsonSerializer.SerializeToUtf8Bytes(new UiWebSocketEnvelope(UiWebSocketProtocol.Version,
				"message",
				typeof(T).Name,
				null,
				null,
				message,
				null),
			UiWebSocketProtocol.Json);
	}

	public static void AssertCarriesVerbatim<T>(T message, byte[] expectedPayload, string because)
		where T : class
	{
		var framed = Frame(message);

		Assert.That(IndexOf(framed, expectedPayload),
			Is.GreaterThanOrEqualTo(0),
			$"{because} The hub's own JSON protocol re-encoded the payload on its way to the client.");
	}

	private static int IndexOf(byte[] haystack, byte[] needle)
	{
		for (var start = 0; start + needle.Length <= haystack.Length; start++)
		{
			var matched = true;
			for (var offset = 0; offset < needle.Length; offset++)
			{
				if (haystack[start + offset] != needle[offset])
				{
					matched = false;
					break;
				}
			}

			if (matched)
			{
				return start;
			}
		}

		return -1;
	}
}
