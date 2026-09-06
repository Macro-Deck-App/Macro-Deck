using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Capabilities.Issues;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Testing.Internal;

namespace MacroDeck.Plugin.Testing;

/// <summary>The <c>issues</c> capability.</summary>
public sealed class IssuesTestClient
{
	private readonly ICapabilityInvoker _invoker;

	internal IssuesTestClient(ICapabilityInvoker invoker) => _invoker = invoker;

	/// <summary>What this provider declares about itself.</summary>
	public Task<CapabilityInvocationOutcome> DescribeAsync(CapabilityInvokeOptions? options = null)
		=> _invoker.InvokeAsync(CapabilityKinds.Issues,
			ProviderCapabilityId.LocalId,
			CapabilityOperations.Issues.Describe,
			null,
			options);

	/// <summary>The integration issues currently open.</summary>
	public Task<CapabilityInvocationOutcome> GetIssuesAsync(CapabilityInvokeOptions? options = null)
		=> _invoker.InvokeAsync(CapabilityKinds.Issues,
			ProviderCapabilityId.LocalId,
			CapabilityOperations.Issues.List,
			null,
			options);

	/// <summary>Asks the plugin to resolve one issue.</summary>
	public Task<CapabilityInvocationOutcome> ResolveAsync(IssueResolveArguments arguments,
		CapabilityInvokeOptions? options = null)
		=> _invoker.InvokeAsync(CapabilityKinds.Issues,
			ProviderCapabilityId.LocalId,
			CapabilityOperations.Issues.Resolve,
			arguments,
			options);
}
