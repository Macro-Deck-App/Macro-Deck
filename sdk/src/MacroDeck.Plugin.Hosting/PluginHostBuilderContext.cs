using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace MacroDeck.Plugin.Hosting;

/// <summary>
/// What a <c>ConfigureServices</c> or <c>Configure</c> callback gets alongside the thing it is
/// configuring: the configuration, the environment, and what the plugin has declared about itself.
/// </summary>
public sealed class PluginHostBuilderContext
{
	internal PluginHostBuilderContext(
		IConfiguration configuration,
		IHostEnvironment hostEnvironment,
		PluginMetadata metadata)
	{
		Configuration = configuration;
		HostEnvironment = hostEnvironment;
		Metadata = metadata;
	}

	public IConfiguration Configuration { get; }

	public IHostEnvironment HostEnvironment { get; }

	/// <summary>The plugin's metadata, as declared by the time <c>Build()</c> ran.</summary>
	public PluginMetadata Metadata { get; }
}
