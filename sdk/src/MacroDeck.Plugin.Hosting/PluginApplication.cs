using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Serilog;
using Serilog.Core;

namespace MacroDeck.Plugin.Hosting;

/// <summary>
/// A built plugin: start it, stop it, or just run it.
///
/// <para>
/// Deliberately not behind an interface. <see cref="WebApplication" /> has none either, an author
/// tests their integration rather than the host that runs it, and publishing an interface now would
/// fix a shape that still has capability kinds to grow into.
/// </para>
/// </summary>
public sealed class PluginApplication : IAsyncDisposable, IDisposable
{
	private readonly WebApplication _application;
	private bool _running;

	internal PluginApplication(WebApplication application, PluginMetadata metadata)
	{
		_application = application;
		Metadata = metadata;
		Logger = application.Services.GetRequiredService<ILogger>()
			.ForContext(Constants.SourceContextPropertyName, metadata.Id);
	}

	/// <summary>The plugin's services.</summary>
	public IServiceProvider Services => _application.Services;

	public IConfiguration Configuration => _application.Configuration;

	/// <summary>What the plugin says about itself, as validated at build.</summary>
	public PluginMetadata Metadata { get; }

	/// <summary>A Serilog logger whose source context is the plugin's id.</summary>
	public ILogger Logger { get; }

	/// <summary>The wrapped application. The escape hatch for anything the SDK does not surface.</summary>
	public WebApplication WebApplication => _application;

	/// <summary>
	/// Starts the plugin and returns once its hosted services have started. Connecting to the host
	/// continues in the background, so a host that is not up yet delays nothing.
	/// </summary>
	public async Task StartAsync(CancellationToken cancellationToken = default)
	{
		await _application.StartAsync(cancellationToken);
		_running = true;
	}

	/// <summary>Stops the plugin: integrations shut down, the session says goodbye and closes.</summary>
	public async Task StopAsync(CancellationToken cancellationToken = default)
	{
		if (!_running)
		{
			return;
		}

		await _application.StopAsync(cancellationToken);
		_running = false;
	}

	/// <summary>Starts the plugin and runs until the process is asked to stop.</summary>
	public async Task RunAsync(CancellationToken cancellationToken = default)
	{
		_running = true;
		await _application.RunAsync();
		_running = false;

		cancellationToken.ThrowIfCancellationRequested();
	}

	public async ValueTask DisposeAsync()
	{
		await StopAsync();
		await _application.DisposeAsync();
	}

	/// <summary>
	/// Synchronous disposal, for a <c>using</c> in a context that has no <c>await</c>. Prefer
	/// <see cref="DisposeAsync" />: shutting a plugin down is asynchronous work, and this blocks on it.
	/// </summary>
	public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();
}
