using MacroDeck.Plugin.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeck.Plugin.Testing;

/// <summary>A plugin built with <see cref="MacroDeckTestHost.HostAsync" />: running in this process, on a real loopback port.</summary>
public sealed class InProcessPlugin : PluginUnderTest
{
	private readonly TempStateDirectory? _ownedStateDirectory;
	private readonly PluginTestManifest? _ownedManifest;

	internal InProcessPlugin(PluginApplication application,
		TempStateDirectory? ownedStateDirectory,
		PluginTestManifest? ownedManifest = null)
	{
		Application = application;
		_ownedStateDirectory = ownedStateDirectory;
		_ownedManifest = ownedManifest;

		BaseAddress = new Uri(application.Services.GetRequiredService<IServer>()
			.Features.Get<IServerAddressesFeature>()!
			.Addresses.First());
	}

	/// <summary>The running application. The escape hatch for anything this type does not surface.</summary>
	public PluginApplication Application { get; }

	/// <inheritdoc />
	public override Uri BaseAddress { get; }

	/// <inheritdoc />
	public override async ValueTask DisposeAsync()
	{
		try
		{
			// Bounded independently of whatever ASP.NET Core's own HostOptions.ShutdownTimeout is doing
			// internally, mirroring MacroDeckTestHost.DisposeAsync's own connection-loop wait: best effort,
			// not a source of truth about the plugin's own graceful-shutdown behaviour (that is what
			// GracefulShutdownReport and A16 are for), only a guarantee that tearing down one subject in a
			// suite that creates several of these in a row cannot itself wedge the run.
			await Application.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(false);
		}
		catch (TimeoutException)
		{
		}

		_ownedStateDirectory?.Dispose();
		_ownedManifest?.Dispose();
		DisposeHttpClient();
	}
}
