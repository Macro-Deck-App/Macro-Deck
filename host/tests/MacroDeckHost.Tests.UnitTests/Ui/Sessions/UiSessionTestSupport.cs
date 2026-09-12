using System.Text;
using System.Text.Json;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Versioning;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Identity;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Model.Events;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Patches;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.Plugins;
using MacroDeckHost.Application.Plugins.Capabilities;
using MacroDeckHost.Application.Ui.Sessions;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.UiSessions;
using MacroDeck.Localization;

namespace MacroDeckHost.Tests.UnitTests.Ui.Sessions;

/// <summary>
/// Records what each client actually observed: the ordered sequence of messages addressed to it, either
/// directly or through a group it belonged to at the moment of the send, plus the exact bytes of any
/// opaque payload. Ordering and byte identity are both part of the session contract, so neither can be
/// reconstructed after the fact from a flat list of sends.
/// </summary>
internal sealed class RecordingUiSessionTransport : IUiTransport
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

	public IReadOnlyList<Recorded> InGroup(string group)
	{
		lock (_gate)
		{
			return [.. _all.Where(recorded => string.Equals(recorded.Group, group, StringComparison.Ordinal))];
		}
	}

	public Task Send<T>(T message, CancellationToken cancellationToken = default)
		where T : class
	{
		lock (_gate)
		{
			// A broadcast reaches every connection this transport has ever seen; that is the failure a
			// session test is looking for, so it is recorded rather than ignored.
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

/// <summary>A provider the test drives directly. Everything it produces still enters the host through
/// <see cref="IUiSessionSink" />, which is the same seam a plugin's callbacks enter through.</summary>
internal sealed class StubUiSessionProvider : IUiSessionProvider
{
	private readonly Lock _gate = new();

	public StubUiSessionProvider(string providerId) => ProviderId = providerId;

	public string ProviderId { get; }

	public IUiSessionSink Sink { get; set; } = null!;

	public UiSessionOpenOutcome OpenOutcome { get; set; } = UiSessionOpenOutcome.Accept(1);

	/// <summary>Set to make every provider call wait forever, modelling a provider that has stopped
	/// answering without disconnecting.</summary>
	public TaskCompletionSource? Gate { get; set; }

	/// <summary>Set to make every provider call fail the way an adapter reports it, so a test can drive
	/// the broker's classification of that failure rather than guess at it.</summary>
	public Func<Exception>? Fail { get; set; }

	/// <summary>Produces the bytes published in answer to a snapshot request. <c>null</c> means the
	/// provider records the request and never answers it.</summary>
	public Func<byte[]>? Snapshot { get; set; }

	public List<string> Calls { get; } = [];

	public List<UiSessionEventCommand> Events { get; } = [];

	public int OpenCalls => CountOf("open");

	public int CloseCalls => CountOf("close");

	public int SnapshotRequests => CountOf("snapshot");

	public async Task<UiSessionOpenOutcome> OpenAsync(UiSessionOpenCommand command,
		CancellationToken cancellationToken)
	{
		Record("open");
		await WaitOnGateAsync().ConfigureAwait(false);
		return OpenOutcome;
	}

	public async Task CloseAsync(string sessionId, string reason, CancellationToken cancellationToken)
	{
		Record("close");
		await WaitOnGateAsync().ConfigureAwait(false);
	}

	public async Task RequestSnapshotAsync(string sessionId, CancellationToken cancellationToken)
	{
		Record("snapshot");
		await WaitOnGateAsync().ConfigureAwait(false);

		if (Snapshot is { } factory)
		{
			Sink.PublishSnapshot(ProviderId, sessionId, new UiRawJson(factory()));
		}
	}

	public async Task DispatchEventAsync(string sessionId,
		UiSessionEventCommand command,
		CancellationToken cancellationToken)
	{
		Record("event");

		lock (_gate)
		{
			Events.Add(command);
		}

		if (Fail is { } fail)
		{
			throw fail();
		}

		await WaitOnGateAsync().ConfigureAwait(false);
	}

	private Task WaitOnGateAsync() => Gate is { } gate ? gate.Task : Task.CompletedTask;

	private void Record(string call)
	{
		lock (_gate)
		{
			Calls.Add(call);
		}
	}

	private int CountOf(string call)
	{
		lock (_gate)
		{
			return Calls.Count(recorded => string.Equals(recorded, call, StringComparison.Ordinal));
		}
	}
}

internal sealed class StubUiSessionProviderResolver : IUiSessionProviderResolver
{
	private readonly Dictionary<string, IUiSessionProvider> _providers = new(StringComparer.Ordinal);

	/// <summary>The real resolver, consulted for any id no stub claims, so an in-process integration is
	/// reached exactly the way the host reaches it.</summary>
	public IUiSessionProviderResolver? Fallback { get; set; }

	public void Add(IUiSessionProvider provider) => _providers[provider.ProviderId] = provider;

	public IUiSessionProvider? Resolve(string providerId)
		=> _providers.GetValueOrDefault(providerId) ?? Fallback?.Resolve(providerId);
}

/// <summary>An in-process integration that serves UI trees, driven by delegates the test supplies.</summary>
internal sealed class StubUiIntegration : IIntegration, IUiProvider
{
	public StubUiIntegration(string id) => Id = id;

	public string Id { get; }

	public LocalizedText Name => Id;

	public string Version => "1.0.0";

	public IReadOnlyList<IActionDefinition> Actions => [];

	public bool IsInitialized => true;

	public StubUiSession? Session { get; set; }

	public IReadOnlyList<UiSurfaceDeclaration> Surfaces { get; set; } =
	[
		new()
			{ Kind = UiSurfaceKinds.Config, SessionMode = UiSessionModes.Exclusive }
	];

	public Task InitializeAsync(IIntegrationContext context) => Task.CompletedTask;

	public Task ShutdownAsync() => Task.CompletedTask;

	public Task<IUiSession?> CreateSessionAsync(UiSessionRequest request, CancellationToken cancellationToken)
		=> Task.FromResult<IUiSession?>(Session);
}

internal sealed class StubUiSession : IUiSession
{
	private readonly Lock _gate = new();
	private readonly List<UiPatch> _pending = [];

	public required Func<UiTree> Tree { get; init; }

	public List<UiEvent> Dispatched { get; } = [];

	public int DisposeCalls { get; private set; }

	/// <summary>How many patches this session has actually handed out. The host must fan one patch out
	/// to every attached client without asking the provider to produce it again per client.</summary>
	public int PatchesDrained { get; private set; }

	public event EventHandler? Changed;

	public event EventHandler<UiSessionFaultedEventArgs>? Faulted;

	public UiTree BuildTree() => Tree();

	public IReadOnlyList<UiPatch> DrainPatches()
	{
		lock (_gate)
		{
			var drained = _pending.ToArray();
			_pending.Clear();
			PatchesDrained += drained.Length;
			return drained;
		}
	}

	public void Dispatch(UiEvent uiEvent)
	{
		lock (_gate)
		{
			Dispatched.Add(uiEvent);
		}
	}

	public void Emit(UiPatch patch)
	{
		lock (_gate)
		{
			_pending.Add(patch);
		}

		Changed?.Invoke(this, EventArgs.Empty);
	}

	public void Fault() => Faulted?.Invoke(this, new UiSessionFaultedEventArgs("gone"));

	public ValueTask DisposeAsync()
	{
		DisposeCalls++;
		return ValueTask.CompletedTask;
	}
}

internal sealed class StubIntegrationRegistry : IIntegrationRegistry
{
	private readonly List<IIntegration> _integrations = [];
	private readonly HashSet<string> _disabled = new(StringComparer.Ordinal);

	public event EventHandler<IntegrationAvailabilityChangedEventArgs>? AvailabilityChanged;

	public IReadOnlyList<IIntegration> Integrations => _integrations;

	public void Add(IIntegration integration) => _integrations.Add(integration);

	public IActionDefinition? FindAction(string integrationId, string actionId) => null;

	public IActionDefinition? FindAction(QualifiedId id) => null;

	public IReadOnlyList<ActionDescriptor> GetActions(bool enabledOnly = true) => [];

	public bool IsEnabled(string integrationId) => !_disabled.Contains(integrationId);

	public void SetEnabled(string integrationId, bool enabled)
	{
		if (enabled)
		{
			_disabled.Remove(integrationId);
		}
		else
		{
			_disabled.Add(integrationId);
		}

		AvailabilityChanged?.Invoke(this,
			new IntegrationAvailabilityChangedEventArgs { IntegrationId = integrationId, IsAvailable = enabled });
	}

	public IntegrationOrigin GetOrigin(string integrationId) => IntegrationOrigin.BuiltIn;

	public Task<IntegrationRegistrationResult> RegisterAsync(IIntegration integration,
		IntegrationOrigin origin = IntegrationOrigin.BuiltIn,
		IntegrationMetadata? metadata = null)
	{
		_integrations.Add(integration);
		return Task.FromResult(IntegrationRegistrationResult.Success);
	}

	public Task<bool> UnregisterAsync(string integrationId)
	{
		_integrations.RemoveAll(integration => string.Equals(integration.Id, integrationId, StringComparison.Ordinal));
		AvailabilityChanged?.Invoke(this,
			new IntegrationAvailabilityChangedEventArgs { IntegrationId = integrationId, IsAvailable = false });
		return Task.FromResult(true);
	}
}

/// <summary>Hand-written UI payloads. The relay never binds these to a model type, so the tests do not
/// either - the bytes are the contract.</summary>
internal static class UiPayloads
{
	public const string Surface =
		"{\"kind\":\"config\",\"sessionMode\":\"exclusive\",\"attributes\":{}}";

	public static byte[] Tree(int revision, int extraNodes = 0, string? padding = null)
	{
		var children = string.Join(',',
			Enumerable.Range(0, extraNodes).Select(index => Node($"n{index}", padding: null)));

		var root = "{\"id\":\"root\",\"type\":\"panel\",\"properties\":{" +
			(padding is null ? string.Empty : $"\"pad\":\"{padding}\"") +
			"},\"children\":[" +
			children +
			"]}";

		return Encoding.UTF8.GetBytes($"{{\"revision\":{revision},\"surface\":{Surface},\"root\":{root}}}");
	}

	public static byte[] TreeWithRoot(int revision, string rootJson)
		=> Encoding.UTF8.GetBytes($"{{\"revision\":{revision},\"surface\":{Surface},\"root\":{rootJson}}}");

	public static string Node(string id, string? padding = null, string? extraProperties = null)
	{
		var properties = new List<string>();
		if (padding is not null)
		{
			properties.Add($"\"pad\":\"{padding}\"");
		}

		if (extraProperties is not null)
		{
			properties.Add(extraProperties);
		}

		return $"{{\"id\":\"{id}\",\"type\":\"t\",\"properties\":{{{string.Join(',', properties)}}},\"children\":[]}}";
	}

	public static byte[] Patch(int fromRevision, int toRevision, string? operations = null)
		=> Encoding.UTF8.GetBytes("{\"fromRevision\":" +
			fromRevision +
			",\"toRevision\":" +
			toRevision +
			",\"operations\":[" +
			(operations ?? DefaultOperation) +
			"]}");

	public static byte[] PatchWithoutOperations(int fromRevision, int toRevision)
		=> Encoding.UTF8.GetBytes("{\"fromRevision\":" +
			fromRevision +
			",\"toRevision\":" +
			toRevision +
			",\"operations\":[]}");

	public const string DefaultOperation =
		"{\"op\":\"set-properties\",\"nodeId\":\"root\",\"properties\":{\"title\":\"x\"}}";

	public static string InsertOperation(string nodeJson, string nodeId)
		=> "{\"op\":\"insert-node\",\"nodeId\":\"" + nodeId + "\",\"parentId\":\"root\",\"node\":" + nodeJson + "}";

	public static string Resource(long byteLength)
		=> "\"source\":{\"resourceId\":\"r1\",\"byteLength\":" + byteLength + "}";

	/// <summary>A tree padded to an exact UTF-8 byte length using two-byte characters, so a test can put
	/// a payload on either side of a byte limit while keeping its character count far below it.</summary>
	public static byte[] TreeOfExactBytes(int revision, int totalBytes)
	{
		var baseline = Tree(revision, padding: string.Empty).Length;
		var delta = totalBytes - baseline;

		if (delta < 0)
		{
			throw new ArgumentOutOfRangeException(nameof(totalBytes), "The unpadded tree is already larger.");
		}

		var padding = new string('\u00e9', delta / 2) + new string('a', delta % 2);
		var payload = Tree(revision, padding: padding);

		if (payload.Length != totalBytes)
		{
			throw new InvalidOperationException($"Built {payload.Length} bytes, wanted {totalBytes}.");
		}

		return payload;
	}

	public static int CharCountOf(byte[] payload) => Encoding.UTF8.GetString(payload).Length;

	public static int RevisionOf(byte[] payload)
	{
		using var document = JsonDocument.Parse(payload);
		return document.RootElement.GetProperty("revision").GetInt32();
	}
}

/// <summary>Fails the moment anything reaches a plugin. An in-process session must produce no
/// <c>capability.invoke</c> traffic at all - not one that a plugin happens to ignore.</summary>
internal sealed class RecordingCapabilityInvoker : IPluginCapabilityInvoker
{
	public List<(string PluginId, string Kind, string Operation)> Invocations { get; } = [];

	public Task<JsonElement?> InvokeAsync(string pluginId,
		CapabilityInvokeRequest request,
		CancellationToken cancellationToken)
	{
		Invocations.Add((pluginId, request.Kind, request.Operation));
		return Task.FromResult<JsonElement?>(null);
	}

	public bool TryComplete(string pluginId, ProtocolEnvelope result) => false;

	public void AbortAll(string pluginId, ProtocolError reason)
	{
	}

	public bool IsLiveActionExecute(string pluginId, string correlationId) => false;
}

internal sealed class EmptyRemotePluginSnapshotStore : IRemotePluginSnapshotStore
{
	public RemotePluginCapabilitySnapshot GetSnapshot(string pluginId)
		=> RemotePluginCapabilitySnapshot.Empty(pluginId);

	public bool Has(string pluginId) => false;

	public Task SaveAsync(RemotePluginCapabilitySnapshot snapshot, CancellationToken cancellationToken = default)
		=> Task.CompletedTask;
}

/// <summary>
/// A real <see cref="PluginSessionRegistry" /> that can additionally replay a session-ended event on
/// demand. The registry itself cannot produce a teardown event for a session a reconnect has already
/// superseded - it removes the record first - so the ordering the broker has to survive is reachable
/// only by raising the event directly, which is the seam the broker actually subscribes to.
/// </summary>
internal sealed class ReplayablePluginSessionRegistry : IPluginSessionRegistry
{
	private readonly PluginSessionRegistry _inner;

	public ReplayablePluginSessionRegistry(TimeProvider timeProvider)
	{
		_inner = new PluginSessionRegistry(timeProvider, Serilog.Core.Logger.None);
		_inner.SessionEnded += (_, e) => SessionEnded?.Invoke(this, e);
	}

	public event EventHandler<PluginSessionEndedEventArgs>? SessionEnded;

	public void RaiseSessionEnded(string pluginId, string sessionId, PluginSessionEndReason reason)
		=> SessionEnded?.Invoke(this,
			new PluginSessionEndedEventArgs { PluginId = pluginId, SessionId = sessionId, Reason = reason });

	public Task Create(PluginSessionRecord record) => _inner.Create(record);

	public bool TryAttach(string sessionId, IPluginConnection connection, string? instanceId)
		=> _inner.TryAttach(sessionId, connection, instanceId);

	public void Detach(string sessionId, DateTimeOffset at) => _inner.Detach(sessionId, at);

	public void EndAfterGoodbye(string sessionId, IPluginConnection connection, DateTimeOffset at)
		=> _inner.EndAfterGoodbye(sessionId, connection, at);

	public bool TryResume(string pluginId, string? resumeSessionId, DateTimeOffset at, out PluginSessionRecord? record)
		=> _inner.TryResume(pluginId, resumeSessionId, at, out record);

	public void MakeNonResumable(string sessionId) => _inner.MakeNonResumable(sessionId);

	public void Touch(string sessionId, DateTimeOffset at) => _inner.Touch(sessionId, at);

	public Task<bool> SendToPlugin(string pluginId,
		ProtocolEnvelope envelope,
		CancellationToken cancellationToken = default)
		=> _inner.SendToPlugin(pluginId, envelope, cancellationToken);

	public bool IsCurrentConnection(string sessionId, IPluginConnection connection)
		=> _inner.IsCurrentConnection(sessionId, connection);

	public Task<bool> Terminate(string sessionId, int closeCode, string reason)
		=> _inner.Terminate(sessionId, closeCode, reason);

	public Task<bool> TerminateForPlugin(string pluginId, int closeCode, string reason)
		=> _inner.TerminateForPlugin(pluginId, closeCode, reason);

	public IReadOnlyList<PluginSessionSnapshot> Snapshot() => _inner.Snapshot();

	public void SetPaused(string sessionId, bool paused) => _inner.SetPaused(sessionId, paused);

	public PluginSessionCapabilities? GetCapabilities(string pluginId) => _inner.GetCapabilities(pluginId);

	public int? GetNegotiatedVersion(string pluginId) => _inner.GetNegotiatedVersion(pluginId);

	public IReadOnlyDictionary<string, CapabilityNegotiationResult>? UpdateDeclaredCapabilities(
		string pluginId,
		IReadOnlyList<DeclaredCapability> declaredCapabilities)
		=> _inner.UpdateDeclaredCapabilities(pluginId, declaredCapabilities);
}
