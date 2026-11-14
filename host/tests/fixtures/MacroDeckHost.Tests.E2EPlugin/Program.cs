using MacroDeck.Plugin.Hosting;
using MacroDeckHost.Tests.E2EPlugin;

var plugin = MacroDeckPlugin.CreatePlugin(args)
	.RegisterIntegration<E2EFixtureIntegration>()
	.Build();

await plugin.RunAsync();
