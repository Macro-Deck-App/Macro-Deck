using MacroDeck.Plugin.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Serilog;
using Serilog.Events;

namespace MacroDeck.Plugin.Serilog;

/// <summary>
/// Wires <c>MacroDeck.Plugin.Serilog</c> into a plugin: after this call, a plugin author's own logging -
/// an injected <c>ILogger&lt;T&gt;</c> or <c>MacroDeck.Sdk.Logging.IntegrationLog</c> - reaches the
/// host's log viewer over <c>log.publish</c>, with no other code change required.
/// </summary>
public static class MacroDeckLoggingBuilderExtensions
{
	private static readonly string[] RequestNoiseCategories =
	[
		"Microsoft.AspNetCore.Hosting.Diagnostics",
		"Microsoft.AspNetCore.Routing.EndpointMiddleware",
		"Microsoft.AspNetCore.Http.Result",
		"Microsoft.AspNetCore.Mvc",
		"Microsoft.AspNetCore.Cors.Infrastructure.CorsService",
		"Microsoft.AspNetCore.StaticFiles",
		"System.Net.Http.HttpClient"
	];

	/// <summary>
	/// Installs Serilog as the plugin's logging pipeline and attaches the Macro Deck sink to it.
	///
	/// <para>
	/// This has to be a builder extension rather than a bare <c>WriteTo.MacroDeck(...)</c> sink
	/// extension: a sink extension is evaluated while the <see cref="LoggerConfiguration" /> itself is
	/// being built, before any service provider exists, and the sink needs DI-resolved services (the
	/// connection, protocol options) plus a hosted-service lifetime for its batching shipper. Only
	/// Serilog's own <c>UseSerilog</c> - which installs <c>SerilogLoggerFactory</c> and replaces
	/// <see cref="Log.Logger" /> - can supply both, and replacing <see cref="Log.Logger" /> is also what
	/// makes "use normal Serilog APIs" actually true: it is what routes both an injected
	/// <c>ILogger&lt;T&gt;</c> and the static <c>MacroDeck.Sdk.Logging.IntegrationLog</c> (itself a
	/// <c>Log.ForContext</c> call) into the sink this method attaches.
	/// </para>
	/// </summary>
	/// <param name="configure">
	/// Runs before the Macro Deck sink is attached, so ordinary Serilog configuration -
	/// <c>MinimumLevel.Debug()</c>, <c>Enrich.With(...)</c>, <c>WriteTo.File(...)</c> - keeps working.
	/// The request pipeline categories (<c>Microsoft.AspNetCore.Hosting.Diagnostics</c> and its siblings) and
	/// <c>System.Net.Http.HttpClient</c> start at <see cref="LogEventLevel.Warning" />, so a health poll or
	/// any other successful request logs no line. A global minimum does not lift them; a
	/// <c>MinimumLevel.Override</c> for the category here does. A console sink added here is the one
	/// thing that does not keep working: plugin logging already reaches the console through the logging
	/// providers the web builder registered, so a <c>WriteTo.Console(...)</c> added here prints every line
	/// twice.
	/// </param>
	public static PluginHostBuilder UseMacroDeckLogging(
		this PluginHostBuilder builder,
		Action<LoggerConfiguration>? configure = null)
	{
		ArgumentNullException.ThrowIfNull(builder);

		// A marker service, checked before anything else is registered, so a second call is a no-op
		// instead of a second sink, a second shipper and UseSerilog replacing Log.Logger twice.
		if (builder.Services.Any(descriptor => descriptor.ServiceType == typeof(MacroDeckLoggingMarker)))
		{
			return builder;
		}

		builder.Services.AddSingleton<MacroDeckLoggingMarker>();

		builder.Services.AddOptions<MacroDeckLoggingOptions>()
			.Bind(builder.Configuration.GetSection(MacroDeckLoggingOptions.SectionName))
			.ValidateDataAnnotations()
			.ValidateOnStart();

		builder.Services.TryAddSingleton<IMacroDeckLogTransport, PluginConnectionLogTransport>();
		builder.Services.AddSingleton<MacroDeckLogSink>();
		builder.Services.AddHostedService<MacroDeckLogShipper>();

		builder.WebApplicationBuilder.Host.UseSerilog((_, services, cfg) =>
			{
				foreach (var category in RequestNoiseCategories)
				{
					cfg.MinimumLevel.Override(category, LogEventLevel.Warning);
				}

				configure?.Invoke(cfg);

				var minimumLevel = services.GetRequiredService<IOptions<MacroDeckLoggingOptions>>().Value.MinimumLevel;

				cfg.WriteTo.Sink(services.GetRequiredService<MacroDeckLogSink>(), minimumLevel);
			},
			// writeToProviders forwards Serilog events *to* the ILoggerProvider instances a plugin built from
			// WebApplication.CreateBuilder already has registered - the console one above all. UseSerilog
			// replaces the logger factory, so without this the console provider is bypassed and a plugin's
			// logging has no console destination at all: nothing appears in an IDE's run window, and
			// 'macrodeck-plugin run' has nothing to forward (#755). Only the host sink is attached here, so
			// there is no console sink for it to duplicate.
			writeToProviders: true);

		return builder;
	}
}

/// <summary>Presence alone marks that <see cref="MacroDeckLoggingBuilderExtensions.UseMacroDeckLogging" />
/// already ran on this builder.</summary>
internal sealed class MacroDeckLoggingMarker;
