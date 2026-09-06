using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeck.Plugin.Hosting;

/// <summary>
/// The ASP.NET Core <c>Startup</c> shape, as an interface. Register it with
/// <see cref="PluginHostBuilder.UseStartup{TStartup}" />.
///
/// <para>
/// An interface rather than the classic convention-based discovery: a misspelled <c>ConfigureServices</c>
/// on a conventional <c>Startup</c> class is silently ignored until something is missing at run time,
/// and reflection over conventions is hostile to trimming. The implementation is constructed with
/// <c>ActivatorUtilities</c>, so it can take <c>IConfiguration</c>, <c>IHostEnvironment</c> and
/// <see cref="PluginMetadata" /> as constructor parameters.
/// </para>
/// </summary>
public interface IPluginStartup
{
	/// <summary>Registers the plugin's own services. Runs before the middleware pipeline is built.</summary>
	void ConfigureServices(IServiceCollection services);

	/// <summary>
	/// Builds the middleware pipeline. The SDK's own reserved-path middleware already ran, so nothing
	/// here can serve a <c>/_macrodeck</c> request.
	/// </summary>
	void Configure(IApplicationBuilder app);
}
