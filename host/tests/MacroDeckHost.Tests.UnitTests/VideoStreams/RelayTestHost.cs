using System.Collections.Concurrent;
using System.Net;
using MacroDeckHost.Application.Logging;
using MacroDeckHost.Application.Paths;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.VideoStreams;
using MacroDeckHost.Extensions;
using MacroDeckHost.Infrastructure.Persistence;
using MacroDeckHost.Tests.UnitTests.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.VideoStreams;

internal sealed record UpstreamRequest(string Method, string RawTarget, string? Range, int Port);

internal sealed class ScriptedUpstream : IAsyncDisposable
{
	private readonly WebApplication _app;

	private ScriptedUpstream(WebApplication app, int port)
	{
		_app = app;
		Port = port;
	}

	public int Port { get; }

	public string Origin => $"http://127.0.0.1:{Port}";

	public ConcurrentQueue<UpstreamRequest> Requests { get; } = new();

	public Func<HttpContext, Task> Handler { get; set; } = NotFound;

	public static async Task<ScriptedUpstream> StartAsync()
	{
		var builder = WebApplication.CreateSlimBuilder();
		builder.Logging.ClearProviders();
		builder.WebHost.UseUrls("http://127.0.0.1:0");
		var app = builder.Build();
		ScriptedUpstream? upstream = null;
		app.Run(context =>
		{
			upstream!.Requests.Enqueue(new UpstreamRequest(context.Request.Method,
				context.Features.GetRequiredFeature<IHttpRequestFeature>().RawTarget,
				context.Request.Headers.Range.ToString() is { Length: > 0 } range ? range : null,
				upstream.Port));
			return upstream.Handler(context);
		});
		await app.StartAsync();
		var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
		upstream = new ScriptedUpstream(app, new Uri(address).Port);
		return upstream;
	}

	public void Reset(Func<HttpContext, Task> handler)
	{
		Requests.Clear();
		Handler = handler;
	}

	public async ValueTask DisposeAsync()
	{
		await _app.StopAsync();
		await _app.DisposeAsync();
	}

	private static Task NotFound(HttpContext context)
	{
		context.Response.StatusCode = StatusCodes.Status404NotFound;
		return Task.CompletedTask;
	}
}

internal sealed class RelayTestHost : IAsyncDisposable
{
	private readonly string _dataDir;
	private readonly string? _previousDataDir;
	private IHost? _host;

	private RelayTestHost(string dataDir, string? previousDataDir)
	{
		_dataDir = dataDir;
		_previousDataDir = previousDataDir;
	}

	public HttpClient Client { get; private set; } = null!;

	public IVideoStreamRelay Relay => _host!.Services.GetRequiredService<IVideoStreamRelay>();

	public TestServer? Server { get; private set; }

	public IPEndPoint? Listener { get; private set; }

	public string LogsDirectory { get; private set; } = string.Empty;

	public static async Task<RelayTestHost> StartAsync(
		VideoStreamRelayOptions? options = null,
		bool kestrel = false,
		bool realLogging = false)
	{
		var dataDir = Path.Combine(Path.GetTempPath(), "macro-deck-tests", Guid.NewGuid().ToString("N"));
		var host = new RelayTestHost(dataDir, Environment.GetEnvironmentVariable("MACRODECK_DATA_DIR"));
		Environment.SetEnvironmentVariable("MACRODECK_DATA_DIR", dataDir);
		var paths = new MacroDeckPaths();
		DatabaseMigrationHelper.MigrateDatabase(paths);
		host.LogsDirectory = paths.LogsDirectory;

		var hostBuilder = new HostBuilder();
		if (realLogging)
		{
			hostBuilder.ConfigureSerilog(paths, new LogLevelState(LogEntryLevel.Debug));
		}

		host._host = await hostBuilder
			.ConfigureWebHost(builder =>
			{
				if (kestrel)
				{
					builder.UseKestrel();
					builder.UseUrls("http://127.0.0.1:0");
				}
				else
				{
					builder.UseTestServer();
				}

				builder.UseStartup<Startup>();
				builder.ConfigureTestServices(services =>
				{
					services.RemoveAll<IHostedService>();
					if (!realLogging)
					{
						services.AddSingleton(Log.Logger);
					}

					services.AddSingleton<IStartupFilter, FakeConnectionStartupFilter>();
					services.RemoveAll<StartupReadiness>();
					var readiness = new StartupReadiness();
					readiness.MarkCachesReady();
					readiness.MarkVariablesReady();
					services.AddSingleton(readiness);
					if (options is not null)
					{
						services.RemoveAll<VideoStreamRelayOptions>();
						services.AddSingleton(options);
					}
				});
			})
			.StartAsync();

		if (kestrel)
		{
			var address = host._host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!
				.Addresses.First();
			host.Listener = new IPEndPoint(IPAddress.Loopback, new Uri(address).Port);
			host.Client = new HttpClient(new SocketsHttpHandler { UseProxy = false })
			{
				BaseAddress = new Uri(address),
				Timeout = TimeSpan.FromSeconds(30)
			};
		}
		else
		{
			host.Server = host._host.GetTestServer();
			host.Client = host._host.GetTestClient();
		}

		return host;
	}

	public string Arm(string sessionId, string upstreamUrl, string transport)
		=> Relay.Arm(sessionId, new Uri(upstreamUrl), transport);

	public Task<HttpResponseMessage> GetAsync(
		string relayPath,
		HttpMethod? method = null,
		Action<HttpRequestMessage>? configure = null,
		CancellationToken cancellationToken = default)
	{
		var request = new HttpRequestMessage(method ?? HttpMethod.Get, relayPath);
		configure?.Invoke(request);
		return Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
	}

	// TestServer, unlike Kestrel, hands the pipeline whatever Path it is given, so this delivers the
	// forms Kestrel leaves in a parsed path, which HttpClient would otherwise normalise away.
	public async Task<HttpContext> SendRawPathAsync(string rawPath, string? rawQuery = null)
	{
		return await Server!.SendAsync(context =>
		{
			context.Request.Method = HttpMethods.Get;
			context.Request.Path = new PathString(rawPath);
			context.Request.QueryString = new QueryString(rawQuery);
		});
	}

	public static string TokenOf(string relayPath)
		=> relayPath[VideoStreamRelay.PathPrefix.Length..].Split('/', '?')[0];

	public async ValueTask DisposeAsync()
	{
		Client.Dispose();
		if (_host is not null)
		{
			try
			{
				await _host.StopAsync();
			}
			finally
			{
				_host.Dispose();
			}
		}

		Environment.SetEnvironmentVariable("MACRODECK_DATA_DIR", _previousDataDir);
		if (Directory.Exists(_dataDir))
		{
			try
			{
				Directory.Delete(_dataDir, recursive: true);
			}
			catch (IOException)
			{
			}
		}
	}

	public async Task StopAsync() => await _host!.StopAsync();
}
