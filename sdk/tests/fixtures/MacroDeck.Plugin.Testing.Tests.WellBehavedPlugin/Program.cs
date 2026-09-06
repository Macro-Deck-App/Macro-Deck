using MacroDeck.Plugin.Hosting;
using MacroDeck.Plugin.Serilog;
using MacroDeck.Plugin.Testing.Tests.WellBehavedPlugin;

// Identity, description and icon are not set here: they come from manifest.json at the content root.
var plugin = MacroDeckPlugin.CreatePlugin(args)
	.UseMacroDeckLogging()
	.RegisterIntegration<WellBehavedIntegration>()
	// Strings is generated from Localization/*.resx; its scope comes from this plugin's manifest id.
	.UseLocalization(Strings.LocalizationCatalog)
	.Build();

await plugin.RunAsync();
