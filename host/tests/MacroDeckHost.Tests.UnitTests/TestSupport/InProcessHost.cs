using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MacroDeckHost.Application.Logging;
using MacroDeckHost.Application.Paths;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Infrastructure.Persistence;
using MacroDeckHost.Tests.UnitTests.Auth;
using MacroDeckHost.Ui;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.TestSupport;

// The data directory is read from a process-wide environment variable, so a fixture using this host must be
// NonParallelizable.
internal sealed class InProcessHost : IAsyncDisposable
{
	public const string Username = "admin";
	public const string Password = "password123";

	private readonly string? _previousDataDir;

	private InProcessHost(IHost host, string dataDirectory, string? previousDataDir)
	{
		Host = host;
		DataDirectory = dataDirectory;
		_previousDataDir = previousDataDir;
		Client = host.GetTestClient();
	}

	public IHost Host { get; }

	public HttpClient Client { get; }

	public string DataDirectory { get; }

	public IServiceProvider Services => Host.Services;

	public static async Task<InProcessHost> StartAsync(
		Action<IServiceCollection>? configureServices = null,
		IReadOnlyList<Type>? hostedServices = null,
		string? dataDirectory = null)
	{
		var directory = dataDirectory
			?? Path.Combine(Path.GetTempPath(), "macro-deck-tests", Guid.NewGuid().ToString("N"));
		var previous = Environment.GetEnvironmentVariable("MACRODECK_DATA_DIR");
		Environment.SetEnvironmentVariable("MACRODECK_DATA_DIR", directory);
		DatabaseMigrationHelper.MigrateDatabase(new MacroDeckPaths());

		var host = await new HostBuilder()
			.ConfigureWebHost(builder =>
			{
				builder.UseTestServer();
				builder.UseStartup<Startup>();
				builder.ConfigureTestServices(services =>
				{
					services.RemoveAll<IHostedService>();
					foreach (var type in hostedServices ?? [])
					{
						services.AddSingleton(typeof(IHostedService),
							provider => provider.GetService(type) ?? ActivatorUtilities.CreateInstance(provider, type));
					}

					services.AddSingleton(Log.Logger);
					services.AddSingleton<ILogLevelState>(new LogLevelState(LogEntryLevel.Information));
					services.AddSingleton<IStartupFilter, FakeConnectionStartupFilter>();
					services.RemoveAll<StartupReadiness>();
					var readiness = new StartupReadiness();
					readiness.MarkCachesReady();
					readiness.MarkVariablesReady();
					services.AddSingleton(readiness);
					configureServices?.Invoke(services);
				});
			})
			.StartAsync();

		return new InProcessHost(host, directory, previous);
	}

	public async Task SetUpAccountAsync()
	{
		var setup = await SendAsync(HttpMethod.Post,
			"/api/auth/setup",
			new { username = Username, password = Password },
			loopback: true);
		Assert.That(setup.StatusCode, Is.EqualTo(HttpStatusCode.NoContent), "account setup");
	}

	public async Task<JsonElement> LoginAsync(string scope, string? deviceName = null)
	{
		object body = deviceName is null
			? new { username = Username, password = Password, scope }
			: new
			{
				username = Username,
				password = Password,
				scope,
				device = new
				{
					clientType = "web-client",
					proposedName = deviceName,
					platform = "Windows",
					browser = "Chrome",
					formFactor = "desktop",
					appVersion = "test"
				}
			};
		var response = await SendAsync(HttpMethod.Post, "/api/auth/login", body);
		Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), $"login with scope {scope}");
		return await ReadJsonAsync(response);
	}

	public async Task<string> LoginTokenAsync(string scope, string? deviceName = null)
	{
		return (await LoginAsync(scope, deviceName)).GetProperty("accessToken").GetString()!;
	}

	public async Task<HttpResponseMessage> SendAsync(
		HttpMethod method,
		string path,
		object? body = null,
		string? bearerToken = null,
		bool loopback = false,
		string? cookie = null)
	{
		using var request = new HttpRequestMessage(method, path);
		if (body is not null)
		{
			request.Content = JsonContent.Create(body);
		}

		if (bearerToken is not null)
		{
			request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
		}

		if (loopback)
		{
			request.Headers.Add(FakeConnectionStartupFilter.ShapeHeader, nameof(FakeConnectionShape.Loopback));
		}

		if (cookie is not null)
		{
			request.Headers.Add("Cookie", cookie);
		}

		return await Client.SendAsync(request);
	}

	public async Task<UiWsTestClient> ConnectUiAsync(string? bearerToken, bool hello = true, bool read = true)
	{
		var shape = bearerToken is null ? FakeConnectionShape.Loopback : FakeConnectionShape.PublicLan;
		using var ticketRequest = new HttpRequestMessage(HttpMethod.Post, "/api/ui-websocket/tickets")
		{
			Content = JsonContent.Create(new { })
		};
		if (bearerToken is not null)
		{
			ticketRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
		}

		ticketRequest.Headers.Add(FakeConnectionStartupFilter.ShapeHeader, shape.ToString());
		ticketRequest.Headers.Add("X-MacroDeck-Ui-Protocol", "1");
		var ticketResponse = await Client.SendAsync(ticketRequest);
		Assert.That(ticketResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK), "UI WebSocket ticket");
		var ticket = (await ReadJsonAsync(ticketResponse)).GetProperty("value").GetString()!;

		var webSocketClient = Host.GetTestServer().CreateWebSocketClient();
		webSocketClient.SubProtocols.Add(UiWebSocketProtocol.SubProtocol);
		webSocketClient.ConfigureRequest = request =>
			request.Headers[FakeConnectionStartupFilter.ShapeHeader] = shape.ToString();
		var socket = await webSocketClient.ConnectAsync(
			new Uri($"ws://localhost{UiWebSocketProtocol.Path}?ticket={Uri.EscapeDataString(ticket)}"),
			CancellationToken.None);
		var client = new UiWsTestClient(socket, read);
		if (hello)
		{
			await client.HelloAsync();
		}

		return client;
	}

	public static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
	{
		return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
	}

	public async ValueTask DisposeAsync()
	{
		Client.Dispose();
		await Host.StopAsync();
		Host.Dispose();
		Environment.SetEnvironmentVariable("MACRODECK_DATA_DIR", _previousDataDir);
		try
		{
			if (Directory.Exists(DataDirectory))
			{
				Directory.Delete(DataDirectory, true);
			}
		}
		catch (IOException)
		{
		}
		catch (UnauthorizedAccessException)
		{
		}
	}
}
