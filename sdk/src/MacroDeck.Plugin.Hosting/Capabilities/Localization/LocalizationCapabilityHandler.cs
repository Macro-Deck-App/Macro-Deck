using System.Text.Json;
using MacroDeck.Localization;
using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Capabilities.Localization;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Plugin.Protocol.Versioning;

namespace MacroDeck.Plugin.Hosting.Capabilities.Localization;

/// <summary>
/// Serves the plugin's own localization catalog: which cultures it ships, and one culture's strings on
/// request.
///
/// <para>
/// Two operations rather than one payload pushed at declaration time, because a catalog is bounded per
/// culture but unbounded across them - the host asks only for the cultures its fallback chain actually
/// needs, and asks again when the language changes.
/// </para>
/// </summary>
internal sealed class LocalizationCapabilityHandler(PluginLocalizationSource source) : ICapabilityHandler
{
	private static readonly CapabilityVersionRange _version = new() { Minimum = 1, Maximum = 1 };

	public string Kind => CapabilityKinds.Localization;

	public IReadOnlyList<DeclaredCapability> DeclareCapabilities()
		=> source.Catalog is null
			? []
			:
			[
				new DeclaredCapability
				{
					Kind = CapabilityKinds.Localization,
					LocalId = ProviderCapabilityId.LocalId,
					VersionRange = _version,
				},
			];

	public Task<CapabilityInvocationResult> InvokeAsync(CapabilityInvocation invocation,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(invocation);

		if (source.Catalog is not { } catalog)
		{
			return Task.FromResult(CapabilityInvocationResult.Failed(ProtocolErrorCodes.CapabilityUnavailable,
				"This plugin ships no localization catalog."));
		}

		return Task.FromResult(invocation.Operation switch
		{
			CapabilityOperations.Localization.Describe => Describe(catalog),
			CapabilityOperations.Localization.Catalog => Catalog(catalog, invocation),
			_ => CapabilityInvocationResult.Failed(ProtocolErrorCodes.CapabilityUnsupported,
				$"The localization capability has no operation '{invocation.Operation}'."),
		});
	}

	private static CapabilityInvocationResult Describe(ILocalizationCatalog catalog)
	{
		if (catalog.Cultures.Count > ProtocolLimits.MaxLocalizationCultures)
		{
			return CapabilityInvocationResult.Failed(ProtocolErrorCodes.CapabilityUnavailable,
				$"This plugin declares {catalog.Cultures.Count} cultures; at most " +
				$"{ProtocolLimits.MaxLocalizationCultures} are allowed.");
		}

		return CapabilityInvocationResult.Ok(new LocalizationDescribeResult
		{
			Scope = catalog.Scope, DefaultCulture = catalog.DefaultCulture, Cultures = catalog.Cultures,
		});
	}

	private static CapabilityInvocationResult Catalog(ILocalizationCatalog catalog,
		CapabilityInvocation invocation)
	{
		var arguments = invocation.Arguments?.Deserialize<LocalizationCatalogArguments>(PluginProtocolJson.Options);

		if (arguments is null || string.IsNullOrWhiteSpace(arguments.Culture))
		{
			return CapabilityInvocationResult.Failed(ProtocolErrorCodes.InvalidPayload,
				"A localization catalog request needs a culture.");
		}

		var entries = new Dictionary<string, string>(StringComparer.Ordinal);

		foreach (var key in catalog.KeysOf(arguments.Culture))
		{
			if (!catalog.TryGetTemplate(arguments.Culture, key, out var template))
			{
				continue;
			}

			// Refused rather than truncated: a half-catalog would resolve some keys and silently render
			// the missing-resource placeholder for the rest, which reads as a translation gap rather than
			// as the size problem it is.
			if (key.Length > ProtocolLimits.MaxLocalizationKeyLength ||
				template.Length > ProtocolLimits.MaxLocalizationValueLength ||
				entries.Count >= ProtocolLimits.MaxLocalizationEntries)
			{
				return CapabilityInvocationResult.Failed(ProtocolErrorCodes.CapabilityUnavailable,
					$"This plugin's '{arguments.Culture}' catalog exceeds the protocol's localization limits.");
			}

			entries.Add(key, template);
		}

		return CapabilityInvocationResult.Ok(new LocalizationCatalogResult
		{
			Culture = arguments.Culture, Entries = entries,
		});
	}
}
