using MacroDeck.Localization;
using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Capabilities.Localization;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Plugin.Testing;

namespace MacroDeck.Plugin.Cli.Rendering;

internal static class PreviewPluginCatalog
{
	public const string UnavailableCode = "preview-localization-unavailable";

	public static async Task<IReadOnlyDictionary<string, string>> LoadAsync(
		CliConsole console,
		PluginSessionView session,
		string locale)
	{
		if (!session.Accepted.Any(result => result.Accepted && result.Kind == CapabilityKinds.Localization))
		{
			return new Dictionary<string, string>();
		}

		try
		{
			return await FetchAsync(console, session, locale).ConfigureAwait(false);
		}
		catch (Exception exception) when (exception is PluginTestTimeoutException or PluginProcessExitedException)
		{
			return Unavailable(console, exception.Message);
		}
	}

	private static async Task<IReadOnlyDictionary<string, string>> FetchAsync(
		CliConsole console,
		PluginSessionView session,
		string locale)
	{
		var describeOutcome = await session.InvokeAsync(CapabilityKinds.Localization,
			ProviderCapabilityId.LocalId,
			CapabilityOperations.Localization.Describe).ConfigureAwait(false);

		if (describeOutcome.DataAs<LocalizationDescribeResult>() is not { } describe || !describeOutcome.Succeeded)
		{
			return Unavailable(console, describeOutcome.Error?.Message ?? "the plugin did not describe its catalog");
		}

		if (!describe.Scope.StartsWith("plugin:", StringComparison.Ordinal))
		{
			return Unavailable(console, $"the plugin claimed the scope '{describe.Scope}'");
		}

		if (describe.Cultures.Count == 0 || describe.Cultures.Count > ProtocolLimits.MaxLocalizationCultures)
		{
			return Unavailable(console, $"it declares {describe.Cultures.Count} cultures");
		}

		var declared = new HashSet<string>(describe.Cultures, StringComparer.OrdinalIgnoreCase);
		var chain = LocalizationCultureChain.For(locale, describe.DefaultCulture).Where(declared.Contains).ToList();
		var resolved = new Dictionary<string, string>(StringComparer.Ordinal);

		foreach (var culture in chain)
		{
			var outcome = await session.InvokeAsync(CapabilityKinds.Localization,
				ProviderCapabilityId.LocalId,
				CapabilityOperations.Localization.Catalog,
				new LocalizationCatalogArguments { Culture = culture }).ConfigureAwait(false);

			if (outcome.DataAs<LocalizationCatalogResult>() is not { } catalog || !outcome.Succeeded)
			{
				return Unavailable(console, outcome.Error?.Message ?? $"the plugin did not serve the culture '{culture}'");
			}

			if (catalog.Entries.Count > ProtocolLimits.MaxLocalizationEntries)
			{
				return Unavailable(console, $"the culture '{culture}' has {catalog.Entries.Count} entries");
			}

			foreach (var (key, template) in catalog.Entries)
			{
				resolved.TryAdd($"{describe.Scope}:{key}", template);
			}
		}

		return resolved;
	}

	private static Dictionary<string, string> Unavailable(CliConsole console, string reason)
	{
		console.WriteWarning(UnavailableCode,
			$"The plugin's own text could not be loaded, so its strings are drawn as placeholders: {reason}.");
		return new Dictionary<string, string>();
	}
}
