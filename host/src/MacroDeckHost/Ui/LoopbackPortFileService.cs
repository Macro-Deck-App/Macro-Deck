using System.Globalization;
using MacroDeckHost.Application.Configuration;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Serilog;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Ui;

public class LoopbackPortFileService : IHostedService
{
	private readonly ILogger _logger = Log.ForContext<LoopbackPortFileService>();
	private readonly IServer _server;
	private readonly IHostApplicationLifetime _lifetime;
	private readonly IHostListenerState _listenerState;

	public LoopbackPortFileService(IServer server,
		IHostApplicationLifetime lifetime,
		IHostListenerState listenerState)
	{
		_server = server;
		_lifetime = lifetime;
		_listenerState = listenerState;
	}

	public Task StartAsync(CancellationToken cancellationToken)
	{
		_lifetime.ApplicationStarted.Register(WritePortFile);
		return Task.CompletedTask;
	}

	public Task StopAsync(CancellationToken cancellationToken)
	{
		try
		{
			File.Delete(HostEndpoints.LoopbackPortFilePath);
		}
		catch (IOException)
		{
		}

		return Task.CompletedTask;
	}

	private void WritePortFile()
	{
		var addresses = _server.Features.Get<IServerAddressesFeature>()?.Addresses ?? [];

		// The host filter already excludes the public listeners, which ListenAnyIP surfaces as [::];
		// excluding every public port as well keeps that true if a public listener is ever bound to a
		// loopback address, because publishing one here would make it the implicitly trusted listener.
		var loopbackPort = addresses
			.Select(address => new Uri(address))
			.Where(uri => uri.Host is "127.0.0.1" or "localhost")
			.Select(uri => uri.Port)
			.FirstOrDefault(port => !HostEndpoints.IsPublicPort(port));

		if (loopbackPort == 0)
		{
			_logger.Error("No loopback endpoint found, cannot publish port file. Addresses: {Addresses}", addresses);
			return;
		}

		// Publish it to the rest of the process too: the network settings refuse a public port that
		// would collide with this listener, and the port is only known once the server has bound it.
		// ResolvedLoopbackPort backs the auth trust boundary, so it is published here - before the
		// port file below, which is the only way anything discovers this listener in the first place.
		_listenerState.SetLoopbackPort(loopbackPort);
		ResolvedLoopbackPort.Set(loopbackPort);

		try
		{
			File.WriteAllText(HostEndpoints.LoopbackPortFilePath,
				loopbackPort.ToString(CultureInfo.InvariantCulture));
			_logger.Information("Listening for UI connections on 127.0.0.1:{Port} (published to {File})",
				loopbackPort,
				HostEndpoints.LoopbackPortFilePath);
		}
		catch (Exception e)
		{
			_logger.Error(e,
				"Failed to write port file {File}. The desktop UI cannot discover the host",
				HostEndpoints.LoopbackPortFilePath);
		}
	}
}
