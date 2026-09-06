using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Capabilities.Variables;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Testing.Internal;

namespace MacroDeck.Plugin.Testing;

/// <summary>
/// The <c>variables</c> capability, both halves. Every eager variable has its own local id - the same
/// one <see cref="PluginSessionView.Declared" /> lists it under - and <see cref="GetAsync" /> and
/// <see cref="SetAsync" /> address one by it, catalog resource ids included. Deserialize a successful
/// outcome's data with <see cref="CapabilityInvocationOutcome.DataAs{T}" />:
/// <see cref="VariableCatalogPayload" /> for <see cref="DescribeAsync" />,
/// <see cref="VariableReadingDto" /> for <see cref="GetAsync" />, <see cref="VariableSetResult" /> for
/// <see cref="SetAsync" />, <see cref="VariableCatalogPageResult" /> for <see cref="DiscoverAsync" />,
/// <see cref="VariableResolveResult" /> for <see cref="ResolveAsync" /> and
/// <see cref="VariableSubscribeResult" /> for <see cref="SubscribeAsync" />.
/// </summary>
public sealed class VariablesTestClient
{
	// Not a protocol constant - variables has no ProviderCapabilityId to address a catalog-level
	// operation with, since every declared local id belongs to a specific eager variable. Any string
	// works here because VariablesCapabilityHandler ignores CapabilityInvocation.LocalId for describe,
	// discover, resolve and subscribe; this value is this package's own convention, not something a wire
	// trace should be expected to explain.
	private const string CatalogLocalId = "describe";

	private readonly ICapabilityInvoker _invoker;

	internal VariablesTestClient(ICapabilityInvoker invoker) => _invoker = invoker;

	/// <summary>The full variable catalogue, both halves - see <see cref="VariableCatalogPayload" />.</summary>
	public Task<CapabilityInvocationOutcome> DescribeAsync(CapabilityInvokeOptions? options = null)
		=> _invoker.InvokeAsync(CapabilityKinds.Variables,
			CatalogLocalId,
			CapabilityOperations.Variables.Describe,
			null,
			options);

	/// <summary>Reads the current reading of the variable addressed by <paramref name="localId" /> - an
	/// eager variable's declared local id, or a catalog resource id.</summary>
	public Task<CapabilityInvocationOutcome> GetAsync(string localId, CapabilityInvokeOptions? options = null)
		=> _invoker.InvokeAsync(CapabilityKinds.Variables, localId, CapabilityOperations.Variables.Get, null, options);

	/// <summary>Writes <paramref name="value" /> to the variable addressed by <paramref name="localId" />.
	/// Succeeds as an invocation whether or not the write was applied - the outcome's
	/// <see cref="VariableSetResult.Status" /> is what says which.</summary>
	public Task<CapabilityInvocationOutcome> SetAsync(
		string localId,
		VariableValueDto value,
		CapabilityInvokeOptions? options = null)
		=> _invoker.InvokeAsync(CapabilityKinds.Variables,
			localId,
			CapabilityOperations.Variables.Set,
			new VariableSetArguments { Value = value },
			options);

	/// <summary>Browses one page of the provider's on-demand catalog - see
	/// <see cref="VariableCatalogPageResult" />.</summary>
	public Task<CapabilityInvocationOutcome> DiscoverAsync(
		string? parentId = null,
		string? search = null,
		string? continuationToken = null,
		int pageSize = 100,
		CapabilityInvokeOptions? options = null)
		=> _invoker.InvokeAsync(CapabilityKinds.Variables,
			CatalogLocalId,
			CapabilityOperations.Variables.Discover,
			new VariableDiscoverArguments
			{
				ParentId = parentId, Search = search, ContinuationToken = continuationToken, PageSize = pageSize
			},
			options);

	/// <summary>Resolves a single catalog resource id - see <see cref="VariableResolveResult" />.</summary>
	public Task<CapabilityInvocationOutcome> ResolveAsync(string id, CapabilityInvokeOptions? options = null)
		=> _invoker.InvokeAsync(CapabilityKinds.Variables,
			CatalogLocalId,
			CapabilityOperations.Variables.Resolve,
			new VariableResolveArguments { Id = id },
			options);

	/// <summary>Replaces the catalog's working set - see <see cref="VariableSubscribeResult" />.
	/// <paramref name="ids" /> is the complete set to watch, not an increment.</summary>
	public Task<CapabilityInvocationOutcome> SubscribeAsync(
		IReadOnlyList<string> ids,
		CapabilityInvokeOptions? options = null)
		=> _invoker.InvokeAsync(CapabilityKinds.Variables,
			CatalogLocalId,
			CapabilityOperations.Variables.Subscribe,
			new VariableSubscribeArguments { Ids = ids },
			options);
}
