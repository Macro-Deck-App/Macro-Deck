using System.Text.Json;
using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Capabilities.ConfigFlow;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Plugin.Protocol.Versioning;
using MacroDeck.Sdk;
using MacroDeck.Sdk.ConfigFlow;

namespace MacroDeck.Plugin.Hosting.Capabilities.ConfigFlow;

/// <summary>
/// Exposes one registered integration's <c>IConfigFlowProvider</c> as the <c>config-flow</c>
/// capability. Provider-shaped like <c>events</c>: one <c>provider</c> local id for the whole plugin -
/// a config flow is a single setup wizard for the plugin, not a per-instance thing, so unlike
/// <c>weather</c>/<c>music-player</c> there is no aggregation across several <see cref="IConfigFlowProvider" />
/// integrations; the first one found is the one this handler serves.
///
/// <para>
/// <see cref="IConfigFlow" /> is stateful and instance-per-session (see its own doc comment), but there
/// is no shared instance across a socket. This handler bridges that through the shared
/// <see cref="PluginConfigFlowSessions" /> <c>sessionId -&gt; IConfigFlow</c> map: <c>flow.start</c>
/// creates a fresh instance via <see cref="IConfigFlowProvider.CreateConfigFlow" /> and stores it under
/// the session id the host minted for it; every later <c>flow.submit</c> for that session id is routed
/// to the same instance, so state a flow keeps in its own fields (values gathered in earlier steps)
/// survives across calls exactly as it would in-process. <c>flow.abandon</c> removes and disposes the
/// instance eagerly; an idle session nobody abandoned is swept by <see cref="IdleTimeout" /> instead,
/// checked lazily on every invocation rather than a background timer, so it stays testable with a fake
/// <see cref="TimeProvider" /> - see <see cref="SweepExpiredSessions" />. The same map is shared with
/// <see cref="MacroDeck.Plugin.Hosting.Capabilities.Ui.UiCapabilityHandler" />, which looks a flow up by
/// session id to route an <c>integration-config</c> UI session to it.
/// </para>
/// </summary>
internal sealed class ConfigFlowCapabilityHandler : ICapabilityHandler
{
	/// <summary>How long a session may sit untouched before it is swept - see
	/// <see cref="PluginConfigFlowSessions.IdleTimeout" />.</summary>
	internal static readonly TimeSpan IdleTimeout = PluginConfigFlowSessions.IdleTimeout;

	private static readonly CapabilityVersionRange _version = new() { Minimum = 1, Maximum = 1 };

	private readonly IConfigFlowProvider? _provider;
	private readonly PluginConfigFlowSessions _sessions;

	public ConfigFlowCapabilityHandler(IEnumerable<IPluginIntegration> integrations, PluginConfigFlowSessions sessions)
	{
		_provider = integrations.OfType<IConfigFlowProvider>().FirstOrDefault();
		_sessions = sessions;
	}

	public string Kind => CapabilityKinds.ConfigFlow;

	public IReadOnlyList<DeclaredCapability> DeclareCapabilities()
		=> _provider is null
			? []
			:
			[
				new DeclaredCapability
				{
					Kind = CapabilityKinds.ConfigFlow, LocalId = ProviderCapabilityId.LocalId, VersionRange = _version
				}
			];

	public Task<CapabilityInvocationResult> InvokeAsync(
		CapabilityInvocation invocation,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(invocation);

		SweepExpiredSessions();

		// describe ignores the local id entirely - see EventsCapabilityHandler's identical remark.
		if (string.Equals(invocation.Operation, CapabilityOperations.ConfigFlow.Describe, StringComparison.Ordinal))
		{
			return Task.FromResult(Describe());
		}

		if (_provider is null ||
			!string.Equals(invocation.LocalId, ProviderCapabilityId.LocalId, StringComparison.Ordinal))
		{
			return Task.FromResult(CapabilityInvocationResult.Failed(ProtocolErrorCodes.CapabilityUnavailable,
				$"No config flow provider '{invocation.LocalId}' is registered in this plugin."));
		}

		return invocation.Operation switch
		{
			CapabilityOperations.ConfigFlow.FlowStart => FlowStartAsync(invocation, cancellationToken),
			CapabilityOperations.ConfigFlow.FlowSubmit => FlowSubmitAsync(invocation, cancellationToken),
			CapabilityOperations.ConfigFlow.FlowAbandon => FlowAbandon(invocation),
			_ => Task.FromResult(CapabilityInvocationResult.Failed(ProtocolErrorCodes.CapabilityUnsupported,
				$"The config-flow capability has no operation '{invocation.Operation}'."))
		};
	}

	private CapabilityInvocationResult Describe()
		=> CapabilityInvocationResult.Ok(new ConfigFlowDescribePayload
		{
			AllowsMultipleConfigurations = _provider?.AllowsMultipleConfigurations ?? true,
			ServesConfigUiTree = _provider is IUiConfigFlowProvider { ServesConfigUiTree: true }
		});

	private async Task<CapabilityInvocationResult> FlowStartAsync(CapabilityInvocation invocation,
		CancellationToken cancellationToken)
	{
		var arguments = invocation.Arguments?.Deserialize<FlowStartArguments>(PluginProtocolJson.Options);
		if (arguments is null)
		{
			return CapabilityInvocationResult.Failed(ProtocolErrorCodes.InvalidPayload,
				"The flow.start operation requires arguments.");
		}

		var flow = _provider!.CreateConfigFlow();

		// Defensive: flow.start is only ever sent once per session id by RemoteConfigFlow, but a retry
		// (or a misbehaving plugin host) must not leak the instance this replaces.
		if (_sessions.Set(arguments.SessionId, flow) is { } previous)
		{
			await DisposeFlowAsync(previous).ConfigureAwait(false);
		}

		var context = new RemoteConfigFlowContext(arguments.OAuth, arguments.EntryTitle);
		var result = await flow.StartAsync(context, cancellationToken).ConfigureAwait(false);

		return CapabilityInvocationResult.Ok(ConfigFlowDescriptorMapper.ToDto(result));
	}

	private async Task<CapabilityInvocationResult> FlowSubmitAsync(CapabilityInvocation invocation,
		CancellationToken cancellationToken)
	{
		var arguments = invocation.Arguments?.Deserialize<FlowSubmitArguments>(PluginProtocolJson.Options);
		if (arguments is null)
		{
			return CapabilityInvocationResult.Failed(ProtocolErrorCodes.InvalidPayload,
				"The flow.submit operation requires arguments.");
		}

		if (!_sessions.TryGetFlow(arguments.SessionId, out var flow))
		{
			return CapabilityInvocationResult.Failed(ProtocolErrorCodes.CapabilityUnavailable,
				"This config flow session has expired or was never started. Please restart setup.");
		}

		_sessions.Touch(arguments.SessionId);

		var context = new RemoteConfigFlowContext(arguments.OAuth, arguments.EntryTitle);
		var input = arguments.Input.ToDictionary(pair => pair.Key,
			pair => ToClrValue(pair.Value),
			StringComparer.Ordinal);

		var result = await flow.SubmitAsync(arguments.StepId, input, context, cancellationToken)
			.ConfigureAwait(false);

		if (result.Kind == ConfigFlowResultKind.Complete && _sessions.TryRemove(arguments.SessionId, out var completed))
		{
			await DisposeFlowAsync(completed).ConfigureAwait(false);
		}

		return CapabilityInvocationResult.Ok(ConfigFlowDescriptorMapper.ToDto(result));
	}

	private Task<CapabilityInvocationResult> FlowAbandon(CapabilityInvocation invocation)
	{
		var arguments = invocation.Arguments?.Deserialize<FlowAbandonArguments>(PluginProtocolJson.Options);
		if (arguments is null)
		{
			return Task.FromResult(CapabilityInvocationResult.Failed(ProtocolErrorCodes.InvalidPayload,
				"The flow.abandon operation requires arguments."));
		}

		return AbandonCoreAsync(arguments.SessionId);
	}

	private async Task<CapabilityInvocationResult> AbandonCoreAsync(string sessionId)
	{
		if (_sessions.TryRemove(sessionId, out var flow))
		{
			await DisposeFlowAsync(flow).ConfigureAwait(false);
		}

		// Unknown or already-gone session ids are not an error: the host may abandon a session that
		// this handler already swept for being idle, and that race must not surface as a failure.
		return CapabilityInvocationResult.Ok();
	}

	/// <summary>Removes and disposes every session idle past <see cref="IdleTimeout" />. Checked at the
	/// top of every invocation rather than on a background timer, so a test can drive it deterministically
	/// with a fake clock instead of a real delay.</summary>
	private void SweepExpiredSessions()
	{
		foreach (var flow in _sessions.SweepExpired())
		{
			_ = DisposeFlowAsync(flow);
		}
	}

	private static async Task DisposeFlowAsync(IConfigFlow flow)
	{
		switch (flow)
		{
			case IAsyncDisposable asyncDisposable:
				await asyncDisposable.DisposeAsync().ConfigureAwait(false);
				break;
			case IDisposable disposable:
				disposable.Dispose();
				break;
		}
	}

	private static object? ToClrValue(JsonElement element)
		=> element.ValueKind switch
		{
			JsonValueKind.String => element.GetString(),
			JsonValueKind.Number => element.TryGetInt64(out var integer) ? integer : element.GetDouble(),
			JsonValueKind.True => true,
			JsonValueKind.False => false,
			JsonValueKind.Null or JsonValueKind.Undefined => null,
			_ => element.Clone()
		};

	private sealed class RemoteConfigFlowContext : IConfigFlowEntryContext, IOAuthSession
	{
		public RemoteConfigFlowContext(ConfigFlowOAuthContextDto oauth, string? entryTitle)
		{
			RedirectUri = oauth.RedirectUri;
			State = oauth.State;
			AuthorizationCode = oauth.AuthorizationCode;
			EntryTitle = entryTitle;
		}

		public IOAuthSession OAuth => this;

		public string RedirectUri { get; }

		public string State { get; }

		public string? AuthorizationCode { get; }

		public string? EntryTitle { get; }
	}
}
