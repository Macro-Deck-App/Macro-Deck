using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Capabilities.ConfigEntries;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Testing.Internal;

namespace MacroDeck.Plugin.Testing;

/// <summary>The <c>config-entries</c> capability, driven the way Macro Deck drives it while it stores the
/// plugin's configuration entries.</summary>
public sealed class ConfigEntriesTestClient
{
	private readonly ICapabilityInvoker _invoker;

	internal ConfigEntriesTestClient(ICapabilityInvoker invoker)
	{
		_invoker = invoker;
	}

	/// <summary>What this provider declares about itself.</summary>
	public Task<CapabilityInvocationOutcome> DescribeAsync(CapabilityInvokeOptions? options = null)
	{
		return InvokeAsync(CapabilityOperations.ConfigEntries.Describe, null, options);
	}

	/// <summary>Asks the plugin to check and adjust an entry about to be stored.</summary>
	public Task<CapabilityInvocationOutcome> PrepareAsync(
		ConfigEntryPrepareArguments arguments,
		CapabilityInvokeOptions? options = null)
	{
		return InvokeAsync(CapabilityOperations.ConfigEntries.Prepare, arguments, options);
	}

	/// <summary>Asks for the status of a stored entry.</summary>
	public Task<CapabilityInvocationOutcome> GetStatusAsync(
		ConfigEntryArguments arguments,
		CapabilityInvokeOptions? options = null)
	{
		return InvokeAsync(CapabilityOperations.ConfigEntries.Status, arguments, options);
	}

	/// <summary>Asks whether a stored entry is usable.</summary>
	public Task<CapabilityInvocationOutcome> IsUsableAsync(
		ConfigEntryArguments arguments,
		CapabilityInvokeOptions? options = null)
	{
		return InvokeAsync(CapabilityOperations.ConfigEntries.Usable, arguments, options);
	}

	/// <summary>Asks the plugin to apply a stored change without being initialized again.</summary>
	public Task<CapabilityInvocationOutcome> ReloadAsync(CapabilityInvokeOptions? options = null)
	{
		return InvokeAsync(CapabilityOperations.ConfigEntries.Reload, null, options);
	}

	/// <summary>Hands the plugin every stored entry after a change.</summary>
	public Task<CapabilityInvocationOutcome> SynchronizeAsync(
		ConfigEntrySynchronizeArguments arguments,
		CapabilityInvokeOptions? options = null)
	{
		return InvokeAsync(CapabilityOperations.ConfigEntries.Synchronize, arguments, options);
	}

	/// <summary>Ends a preparation, stored or not.</summary>
	public Task<CapabilityInvocationOutcome> ReleaseAsync(
		ConfigEntryReleaseArguments arguments,
		CapabilityInvokeOptions? options = null)
	{
		return InvokeAsync(CapabilityOperations.ConfigEntries.Release, arguments, options);
	}

	private Task<CapabilityInvocationOutcome> InvokeAsync(
		string operation,
		object? arguments,
		CapabilityInvokeOptions? options)
	{
		return _invoker.InvokeAsync(CapabilityKinds.ConfigEntries,
			ProviderCapabilityId.LocalId,
			operation,
			arguments,
			options);
	}
}
