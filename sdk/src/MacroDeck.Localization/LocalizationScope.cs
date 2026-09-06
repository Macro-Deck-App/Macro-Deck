using MacroDeck.Localization.Compiler;

namespace MacroDeck.Localization;

/// <summary>
/// The namespace a localization key belongs to. Macro Deck's own catalog is <see cref="MacroDeck" />;
/// every plugin owns <c>plugin:&lt;plugin-id&gt;</c> and nothing else, which is what stops one plugin's
/// resources from overwriting Macro Deck's or another plugin's.
/// </summary>
public static class LocalizationScope
{
	/// <summary>The scope of the reusable catalog Macro Deck itself ships.</summary>
	public const string MacroDeck = LocalizationScopeNames.MacroDeck;

	/// <summary>The prefix every plugin-owned scope starts with.</summary>
	public const string PluginPrefix = LocalizationScopeNames.PluginPrefix;

	/// <summary>Builds the scope owned by <paramref name="pluginId" />.</summary>
	/// <param name="pluginId">The plugin's manifest id.</param>
	/// <returns>The plugin's scope, for example <c>plugin:com.example.spotify</c>.</returns>
	/// <exception cref="ArgumentException"><paramref name="pluginId" /> is null or blank.</exception>
	public static string ForPlugin(string pluginId)
	{
		if (string.IsNullOrWhiteSpace(pluginId))
		{
			throw new ArgumentException("A plugin scope needs a plugin id.", nameof(pluginId));
		}

		return PluginPrefix + pluginId;
	}

	/// <summary>Whether <paramref name="scope" /> is owned by a plugin rather than by Macro Deck.</summary>
	public static bool IsPlugin(string? scope)
		=> scope is not null &&
			scope.StartsWith(PluginPrefix, StringComparison.Ordinal) &&
			scope.Length > PluginPrefix.Length;

	/// <summary>The plugin id inside a plugin scope, or <c>null</c> when the scope is not a plugin's.</summary>
	public static string? PluginIdOf(string? scope)
		=> IsPlugin(scope) ? scope![PluginPrefix.Length..] : null;

	/// <summary>
	/// Whether <paramref name="scope" /> names one of Macro Deck's own application catalogs, such as
	/// <c>macrodeck.app</c>. These ship with the app rather than in the SDK package, so their keys are
	/// not the frozen, additive-only contract <see cref="MacroDeck" />'s are.
	/// </summary>
	public static bool IsApplication(string? scope) => LocalizationScopeNames.IsMacroDeckOwned(scope);

	/// <summary>Whether <paramref name="scope" /> is a scope this framework recognises at all.</summary>
	public static bool IsValid(string? scope)
		=> string.Equals(scope, MacroDeck, StringComparison.Ordinal) ||
			IsPlugin(scope) ||
			IsApplication(scope);
}
