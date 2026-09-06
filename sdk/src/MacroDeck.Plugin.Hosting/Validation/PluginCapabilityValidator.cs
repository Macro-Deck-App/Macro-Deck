using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Sdk.Identity;

namespace MacroDeck.Plugin.Hosting.Validation;

/// <summary>
/// Checks a declared capability catalogue before it is ever sent.
///
/// <para>
/// The host validates the same catalogue when it arrives, and rejects a plugin whose ids are illegal.
/// Checking here first turns that into a build-time error with a full list, in the process that can
/// actually fix it, rather than a rejection an author has to read out of a host log.
/// </para>
/// </summary>
internal static class PluginCapabilityValidator
{
	public static IReadOnlyList<CapabilityIdConflict> Validate(
		string pluginId,
		IReadOnlyList<DeclaredCapability> capabilities)
	{
		var conflicts = new List<CapabilityIdConflict>();

		if (capabilities.Count > ProtocolLimits.MaxDeclaredCapabilities)
		{
			conflicts.Add(new CapabilityIdConflict(pluginId,
				"Capability",
				string.Empty,
				$"Declares {capabilities.Count} capabilities; the protocol allows at most " +
				$"{ProtocolLimits.MaxDeclaredCapabilities}."));
		}

		foreach (var kind in capabilities.Select(capability => capability.Kind).Distinct(StringComparer.Ordinal))
		{
			if (!CapabilityKinds.IsKnown(kind))
			{
				conflicts.Add(new CapabilityIdConflict(pluginId,
					"Capability",
					kind,
					$"'{kind}' is not a capability kind this protocol version knows. " +
					$"Known kinds: {string.Join(", ", CapabilityKinds.All)}."));
				continue;
			}

			// Per kind, because the wire identity of a capability is (kind, localId): the same local id
			// under two different kinds is two different capabilities and not a collision.
			DeclaredIdValidator.Validate(conflicts,
				pluginId,
				kind,
				capabilities
					.Where(capability => string.Equals(capability.Kind, kind, StringComparison.Ordinal))
					.Select(capability => capability.LocalId));
		}

		return conflicts;
	}
}
