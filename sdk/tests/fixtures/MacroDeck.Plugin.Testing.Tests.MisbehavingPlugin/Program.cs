using MacroDeck.Plugin.Hosting;
using MacroDeck.Plugin.Serilog;
using MacroDeck.Plugin.Testing.Tests.MisbehavingPlugin;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

var behaviors = (Environment.GetEnvironmentVariable("MACRODECK_MISBEHAVE") ?? string.Empty)
	.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
	.ToHashSet(StringComparer.OrdinalIgnoreCase);

// Checked before anything else builds: a launcher's own launched-process test needs to see a process
// that dies immediately, with no plugin machinery (and no chance of a partially-started listener)
// in the way.
if (behaviors.Contains(MisbehaviorFlags.ExitImmediately))
{
	await Console.Error.WriteLineAsync(
		"misbehaving-plugin: exiting immediately (MACRODECK_MISBEHAVE=exit-immediately).");
	return 3;
}

// Overwrites whatever ASPNETCORE_URLS the launcher assigned before MacroDeckPlugin.CreatePlugin reads
// it - the documented foot-gun: a supervisor's health probe, aimed at the port it assigned, never finds
// a plugin that did this.
if (behaviors.Contains(MisbehaviorFlags.SelfSetUrls))
{
	Environment.SetEnvironmentVariable("ASPNETCORE_URLS", "http://127.0.0.1:0");
}

// Identity, name and version come from manifest.json beside the executable, not from code.
var plugin = MacroDeckPlugin.CreatePlugin(args)
	.UseMacroDeckLogging()
	.RegisterIntegration(provider
		=> new MisbehavingIntegration(behaviors, provider.GetRequiredService<ILogger>()))
	.Build();

await plugin.RunAsync();
return 0;
