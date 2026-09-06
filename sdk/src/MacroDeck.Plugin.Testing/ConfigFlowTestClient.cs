using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Capabilities.ConfigFlow;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Testing.Internal;

namespace MacroDeck.Plugin.Testing;

/// <summary>
/// The <c>config-flow</c> capability. Result shapes live in
/// <c>MacroDeck.Plugin.Protocol.Capabilities.ConfigFlow</c>; read a successful outcome's data back with
/// <see cref="CapabilityInvocationOutcome.DataAs{T}" /> - <see cref="ConfigFlowDescribePayload" /> for
/// <see cref="DescribeAsync" />, <see cref="ConfigFlowResultDto" /> for the other three.
/// </summary>
public sealed class ConfigFlowTestClient
{
	private readonly ICapabilityInvoker _invoker;

	internal ConfigFlowTestClient(ICapabilityInvoker invoker) => _invoker = invoker;

	/// <summary>The config flow's shape, before any step has run.</summary>
	public Task<CapabilityInvocationOutcome> DescribeAsync(CapabilityInvokeOptions? options = null)
		=> _invoker.InvokeAsync(CapabilityKinds.ConfigFlow,
			ProviderCapabilityId.LocalId,
			CapabilityOperations.ConfigFlow.Describe,
			null,
			options);

	/// <summary>Starts a new flow instance.</summary>
	public Task<CapabilityInvocationOutcome> StartAsync(FlowStartArguments arguments,
		CapabilityInvokeOptions? options = null)
		=> _invoker.InvokeAsync(CapabilityKinds.ConfigFlow,
			ProviderCapabilityId.LocalId,
			CapabilityOperations.ConfigFlow.FlowStart,
			arguments,
			options);

	/// <summary>Submits one step's input.</summary>
	public Task<CapabilityInvocationOutcome> SubmitAsync(FlowSubmitArguments arguments,
		CapabilityInvokeOptions? options = null)
		=> _invoker.InvokeAsync(CapabilityKinds.ConfigFlow,
			ProviderCapabilityId.LocalId,
			CapabilityOperations.ConfigFlow.FlowSubmit,
			arguments,
			options);

	/// <summary>Abandons a flow instance the user did not complete.</summary>
	public Task<CapabilityInvocationOutcome> AbandonAsync(FlowAbandonArguments arguments,
		CapabilityInvokeOptions? options = null)
		=> _invoker.InvokeAsync(CapabilityKinds.ConfigFlow,
			ProviderCapabilityId.LocalId,
			CapabilityOperations.ConfigFlow.FlowAbandon,
			arguments,
			options);
}
