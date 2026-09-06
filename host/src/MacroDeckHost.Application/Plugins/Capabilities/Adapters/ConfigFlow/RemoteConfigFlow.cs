using System.Text.Json;
using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Capabilities.ConfigFlow;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeckHost.Application.Plugins.Capabilities.Mapping;
using MacroDeck.Sdk.ConfigFlow;

namespace MacroDeckHost.Application.Plugins.Capabilities.Adapters.ConfigFlow;

internal sealed class RemoteConfigFlow(string pluginId, IPluginCapabilityInvoker invoker)
	: IConfigFlow, IAsyncDisposable
{
	private readonly string _sessionId = Guid.NewGuid().ToString("N");
	private bool _started;
	private bool _completed;

	/// <summary>The session id this instance minted for <c>flow.start</c>/<c>flow.submit</c>. Exposed so
	/// a UI session opened for this flow can carry the very id the plugin already knows this flow by,
	/// rather than minting a second one.</summary>
	internal string SessionId => _sessionId;

	public async Task<ConfigFlowResult> StartAsync(IConfigFlowContext context, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(context);

		_started = true;

		var data = await invoker.InvokeAsync(pluginId,
				new CapabilityInvokeRequest
				{
					Kind = CapabilityKinds.ConfigFlow,
					LocalId = ProviderCapabilityId.LocalId,
					Operation = CapabilityOperations.ConfigFlow.FlowStart,
					Arguments = new FlowStartArguments
					{
						SessionId = _sessionId,
						OAuth = ToDto(context.OAuth),
						EntryTitle = (context as IConfigFlowEntryContext)?.EntryTitle
					}
				},
				cancellationToken)
			.ConfigureAwait(false);

		return MapResult(data);
	}

	public async Task<ConfigFlowResult> SubmitAsync(
		string stepId,
		IReadOnlyDictionary<string, object?> input,
		IConfigFlowContext context,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(context);

		var data = await invoker.InvokeAsync(pluginId,
				new CapabilityInvokeRequest
				{
					Kind = CapabilityKinds.ConfigFlow,
					LocalId = ProviderCapabilityId.LocalId,
					Operation = CapabilityOperations.ConfigFlow.FlowSubmit,
					Arguments = new FlowSubmitArguments
					{
						SessionId = _sessionId, StepId = stepId, Input = ToWireInput(input),
						OAuth = ToDto(context.OAuth),
						EntryTitle = (context as IConfigFlowEntryContext)?.EntryTitle
					}
				},
				cancellationToken)
			.ConfigureAwait(false);

		var result = MapResult(data);
		if (result.Kind == ConfigFlowResultKind.Complete)
		{
			_completed = true;
		}

		return result;
	}

	public async ValueTask DisposeAsync()
	{
		if (!_started || _completed)
		{
			return;
		}

		try
		{
			await invoker.InvokeAsync(pluginId,
					new CapabilityInvokeRequest
					{
						Kind = CapabilityKinds.ConfigFlow,
						LocalId = ProviderCapabilityId.LocalId,
						Operation = CapabilityOperations.ConfigFlow.FlowAbandon,
						Arguments = new FlowAbandonArguments { SessionId = _sessionId }
					},
					CancellationToken.None)
				.ConfigureAwait(false);
		}
		catch (RemoteCapabilityException)
		{
		}
	}

	private static ConfigFlowOAuthContextDto ToDto(IOAuthSession session)
		=> new()
		{
			RedirectUri = session.RedirectUri, State = session.State, AuthorizationCode = session.AuthorizationCode
		};

	private static Dictionary<string, JsonElement> ToWireInput(IReadOnlyDictionary<string, object?> input)
	{
		var wire = new Dictionary<string, JsonElement>(input.Count, StringComparer.Ordinal);

		foreach (var (name, value) in input)
		{
			wire[name] = value is JsonElement element
				? element
				: JsonSerializer.SerializeToElement(value, PluginProtocolJson.Options);
		}

		return wire;
	}

	private static ConfigFlowResult MapResult(JsonElement? data)
	{
		var dto = data?.Deserialize<ConfigFlowResultDto>(PluginProtocolJson.Options);
		if (dto is null)
		{
			throw RemoteCapabilityException.CreateNonRetryable(ProtocolErrorCodes.InvalidPayload,
				"The plugin returned no config flow result.");
		}

		return ConfigFlowResultMapper.ToDomain(dto);
	}
}
