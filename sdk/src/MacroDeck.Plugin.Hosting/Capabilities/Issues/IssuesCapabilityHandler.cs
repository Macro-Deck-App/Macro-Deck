using System.Text.Json;
using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Capabilities.Issues;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Plugin.Protocol.Versioning;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Issues;

namespace MacroDeck.Plugin.Hosting.Capabilities.Issues;

/// <summary>
/// Exposes every registered integration's <c>IIntegrationIssueProvider</c> as the <c>issues</c>
/// capability. Provider-shaped like <c>events</c>: the whole plugin declares one <c>provider</c> local
/// id, and <c>list</c>/<c>resolve</c> are always live round trips - <c>IIntegrationIssueProvider</c> has
/// no side-effect-free declared catalogue the way actions and variables do, so there is nothing for
/// <c>describe</c> to report beyond confirming the provider exists.
/// </summary>
internal sealed class IssuesCapabilityHandler(IEnumerable<IPluginIntegration> integrations) : ICapabilityHandler
{
	private static readonly CapabilityVersionRange _version = new() { Minimum = 1, Maximum = 1 };

	private readonly IReadOnlyList<IIntegrationIssueProvider> _providers =
		[.. integrations.OfType<IIntegrationIssueProvider>()];

	public string Kind => CapabilityKinds.Issues;

	public IReadOnlyList<DeclaredCapability> DeclareCapabilities()
		=> _providers.Count == 0
			? []
			:
			[
				new DeclaredCapability
				{
					Kind = CapabilityKinds.Issues, LocalId = ProviderCapabilityId.LocalId, VersionRange = _version
				}
			];

	public Task<CapabilityInvocationResult> InvokeAsync(
		CapabilityInvocation invocation,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(invocation);

		// describe ignores the local id entirely, the same way ActionsCapabilityHandler.Describe does -
		// see EventsCapabilityHandler's identical remark. Every other operation is addressed to the one
		// provider this kind ever declares, so it is the only place the gate applies.
		if (string.Equals(invocation.Operation, CapabilityOperations.Issues.Describe, StringComparison.Ordinal))
		{
			return Task.FromResult(CapabilityInvocationResult.Ok());
		}

		if (!string.Equals(invocation.LocalId, ProviderCapabilityId.LocalId, StringComparison.Ordinal))
		{
			return Task.FromResult(CapabilityInvocationResult.Failed(ProtocolErrorCodes.CapabilityUnavailable,
				$"No issues provider '{invocation.LocalId}' is registered in this plugin."));
		}

		return invocation.Operation switch
		{
			CapabilityOperations.Issues.List => ListAsync(cancellationToken),
			CapabilityOperations.Issues.Resolve => ResolveAsync(invocation, cancellationToken),
			_ => Task.FromResult(CapabilityInvocationResult.Failed(ProtocolErrorCodes.CapabilityUnsupported,
				$"The issues capability has no operation '{invocation.Operation}'."))
		};
	}

	private async Task<CapabilityInvocationResult> ListAsync(CancellationToken cancellationToken)
	{
		var all = new List<IntegrationIssueDescriptorDto>();

		foreach (var provider in _providers)
		{
			var issues = await provider.GetIssuesAsync(cancellationToken).ConfigureAwait(false);
			all.AddRange(issues.Select(IssueDescriptorMapper.ToDto));
		}

		return CapabilityInvocationResult.Ok(new IssueListResult { Issues = all });
	}

	private async Task<CapabilityInvocationResult> ResolveAsync(CapabilityInvocation invocation,
		CancellationToken cancellationToken)
	{
		var arguments = invocation.Arguments?.Deserialize<IssueResolveArguments>(PluginProtocolJson.Options);
		if (arguments is null)
		{
			return CapabilityInvocationResult.Failed(ProtocolErrorCodes.InvalidPayload,
				"The resolve operation requires arguments.");
		}

		foreach (var provider in _providers)
		{
			var issues = await provider.GetIssuesAsync(cancellationToken).ConfigureAwait(false);
			if (!issues.Any(issue => string.Equals(issue.Id, arguments.IssueId, StringComparison.Ordinal)))
			{
				continue;
			}

			var resolution = await provider.ResolveIssueAsync(arguments.IssueId, cancellationToken)
				.ConfigureAwait(false);
			return CapabilityInvocationResult.Ok(IssueDescriptorMapper.ToDto(resolution));
		}

		return CapabilityInvocationResult.Failed(ProtocolErrorCodes.CapabilityUnavailable,
			$"No issue '{arguments.IssueId}' is currently reported by this plugin.");
	}
}
