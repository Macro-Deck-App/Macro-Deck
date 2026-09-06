using MacroDeck.Plugin.Packaging.Artifacts;
using MacroDeck.Plugin.Packaging.Manifest;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeck.Plugin.Packaging;

/// <summary>Registers the manifest and artifact readers this package provides.</summary>
public static class ServiceCollectionExtensions
{
	/// <summary>Adds <see cref="IPluginManifestReader"/> and <see cref="IPluginArtifactReader"/> as
	/// singletons. Neither holds mutable state; both are pure readers over whatever path or stream they
	/// are given.</summary>
	public static IServiceCollection AddMacroDeckPluginPackaging(this IServiceCollection services)
	{
		services.AddSingleton<IPluginManifestReader, PluginManifestReader>();
		services.AddSingleton<IPluginArtifactReader, PluginArtifactReader>();
		return services;
	}
}
