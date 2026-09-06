using System.Text.Json;
using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Capabilities.VirtualProfiles;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Plugin.Protocol.Versioning;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Profiles;

namespace MacroDeck.Plugin.Hosting.Capabilities.VirtualProfiles;

/// <summary>
/// Exposes every registered integration's <c>IProfileProvider</c> as the <c>virtual-profiles</c>
/// capability. Provider-shaped like <c>issues</c>: one <c>provider</c> local id, and
/// <c>profiles</c>/<c>widget-interaction</c> are always live round trips - a virtual profile's
/// contents come straight from the provider's own descriptors, so there is nothing for
/// <c>describe</c> to cache beyond what <c>profiles</c> already returns.
/// </summary>
internal sealed class VirtualProfilesCapabilityHandler(
	IEnumerable<IPluginIntegration> integrations,
	PluginMetadata metadata) : ICapabilityHandler
{
	private static readonly CapabilityVersionRange _version = new() { Minimum = 1, Maximum = 1 };

	private readonly IReadOnlyList<IProfileProvider> _providers = [.. integrations.OfType<IProfileProvider>()];

	public string Kind => CapabilityKinds.VirtualProfiles;

	public IReadOnlyList<DeclaredCapability> DeclareCapabilities()
		=> _providers.Count == 0
			? []
			:
			[
				new DeclaredCapability
				{
					Kind = CapabilityKinds.VirtualProfiles, LocalId = ProviderCapabilityId.LocalId,
					VersionRange = _version
				}
			];

	public Task<CapabilityInvocationResult> InvokeAsync(
		CapabilityInvocation invocation,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(invocation);

		// describe ignores the local id entirely - see EventsCapabilityHandler's identical remark.
		if (string.Equals(invocation.Operation,
			CapabilityOperations.VirtualProfiles.Describe,
			StringComparison.Ordinal))
		{
			return Task.FromResult(Describe());
		}

		if (!string.Equals(invocation.LocalId, ProviderCapabilityId.LocalId, StringComparison.Ordinal))
		{
			return Task.FromResult(CapabilityInvocationResult.Failed(ProtocolErrorCodes.CapabilityUnavailable,
				$"No virtual profiles provider '{invocation.LocalId}' is registered in this plugin."));
		}

		return invocation.Operation switch
		{
			CapabilityOperations.VirtualProfiles.Profiles => Task.FromResult(Profiles()),
			CapabilityOperations.VirtualProfiles.WidgetInteraction => WidgetInteractionAsync(invocation,
				cancellationToken),
			_ => Task.FromResult(CapabilityInvocationResult.Failed(ProtocolErrorCodes.CapabilityUnsupported,
				$"The virtual-profiles capability has no operation '{invocation.Operation}'."))
		};
	}

	private CapabilityInvocationResult Describe()
		=> CapabilityInvocationResult.Ok(new VirtualProfilesDescribePayload
		{
			ProviderName = _providers.Select(p => p.ProviderName).FirstOrDefault(name => !string.IsNullOrEmpty(name)) ??
				metadata.Name,
			Profiles = BuildProfiles()
		});

	/// <summary>The <c>profiles</c> operation - see <c>MusicPlayerInstancesResult</c>'s identical remarks.</summary>
	private CapabilityInvocationResult Profiles() => CapabilityInvocationResult.Ok(new VirtualProfilesResult
	{
		Profiles = BuildProfiles()
	});

	/// <summary>Never re-sorted - see <c>MusicPlayerCapabilityHandler.BuildInstances</c>'s identical remarks.</summary>
	private IReadOnlyList<VirtualProfileDescriptorDto> BuildProfiles()
		=> [.. _providers.SelectMany(provider => provider.GetProfiles()).Select(VirtualProfileDescriptorMapper.ToDto)];

	private async Task<CapabilityInvocationResult> WidgetInteractionAsync(
		CapabilityInvocation invocation,
		CancellationToken cancellationToken)
	{
		var arguments = invocation.Arguments?.Deserialize<WidgetInteractionArguments>(PluginProtocolJson.Options);
		if (arguments is null)
		{
			return CapabilityInvocationResult.Failed(ProtocolErrorCodes.InvalidPayload,
				"The widget-interaction operation requires arguments.");
		}

		var provider = _providers.FirstOrDefault(p => Owns(p, arguments.FolderId, arguments.WidgetId));
		if (provider is null)
		{
			return CapabilityInvocationResult.Failed(ProtocolErrorCodes.CapabilityUnavailable,
				$"No virtual widget '{arguments.WidgetId}' in folder '{arguments.FolderId}' is currently available.");
		}

		// ProfileId is forwarded exactly as received rather than re-derived: the host's own in-process
		// ProfileRegistry always sends string.Empty for it (see ProfileRegistry.RouteAsync), and this
		// remote path must answer identically rather than trying to "fix" or backfill it.
		await provider.HandleWidgetInteractionAsync(arguments.ProfileId,
				arguments.FolderId,
				arguments.WidgetId,
				new WidgetInteraction(arguments.TriggerType))
			.WaitAsync(cancellationToken)
			.ConfigureAwait(false);

		return CapabilityInvocationResult.Ok();
	}

	private static bool Owns(IProfileProvider provider, string folderId, string widgetId)
		=> provider.GetProfiles()
			.SelectMany(profile => profile.Folders)
			.Where(folder => string.Equals(folder.Id, folderId, StringComparison.Ordinal))
			.SelectMany(folder => folder.Widgets)
			.Any(widget => string.Equals(widget.Id, widgetId, StringComparison.Ordinal));
}
