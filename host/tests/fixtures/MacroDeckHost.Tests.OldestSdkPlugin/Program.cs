using MacroDeck.Plugin.Hosting;
using MacroDeckHost.Tests.OldestSdkPlugin;

var plugin = MacroDeckPlugin.CreatePlugin(args)
	.RegisterIntegration<OldestSdkIntegration>()
	.Build();

await plugin.RunAsync();
