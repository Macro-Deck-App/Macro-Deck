using MacroDeck.Localization;

namespace MacroDeck.Plugin.Hosting.Capabilities.Localization;

/// <summary>
/// The catalog a plugin declared through <c>PluginHostBuilder.UseLocalization</c>, or none. Registered
/// unconditionally so the handler always has one to read; the capability itself is what stays undeclared
/// when there is nothing to serve, exactly as the icons kind does.
/// </summary>
internal sealed class PluginLocalizationSource(ILocalizationCatalog? catalog)
{
	/// <summary>Nothing to serve: no capability, nothing on the wire.</summary>
	public static readonly PluginLocalizationSource None = new(null);

	/// <summary>The plugin's catalog, or <c>null</c>.</summary>
	public ILocalizationCatalog? Catalog { get; } = catalog;
}
