using MacroDeck.Plugin.Hosting.Credentials;
using MacroDeck.Plugin.Hosting.Tests.UnitTests.Support;
using MacroDeck.Plugin.Hosting.Transport;
using MacroDeck.Plugin.Protocol.Auth;
using MacroDeck.Plugin.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace MacroDeck.Plugin.Hosting.Tests.UnitTests;

[TestFixture]
public class StoredCredentialHostTests
{
	private const string MovedHostUrl = "http://127.0.0.1:1";

	private static readonly string StoredSecret = new('s', PluginAuthDefaults.MinPluginSecretLength);

	private FakePluginHost _host = null!;
	private string _stateDirectory = string.Empty;
	private PluginManifestFixture _manifest = null!;
	private CollectingSink _logs = null!;

	[SetUp]
	public async Task SetUp()
	{
		_host = await FakePluginHost.StartAsync();
		_stateDirectory = Directory.CreateTempSubdirectory("macro-deck-plugin-stored-host-tests").FullName;
		_manifest = new PluginManifestFixture("""
											  {
											    "manifestVersion": 1,
											    "id": "com.example.test",
											    "name": "Test Plugin",
											    "version": "1.0.0"
											  }
											  """);
		_logs = new CollectingSink();
	}

	[TearDown]
	public async Task TearDown()
	{
		await _host.DisposeAsync();
		Directory.Delete(_stateDirectory, recursive: true);
		_manifest.Dispose();
	}

	[Test]
	public async Task A_credential_stored_while_the_host_listened_elsewhere_still_connects_to_the_configured_host()
	{
		await StoreCredentialIssuedByAsync(MovedHostUrl);

		await using var plugin = Builder().Build();
		await plugin.StartAsync();

		await _host.ConnectedAsync().WaitAsync(TimeSpan.FromSeconds(10));
		await _host.WelcomedAsync();

		Assert.Multiple(() =>
		{
			Assert.That(_host.Registrations, Is.Empty, "The stored credential was not used.");
			Assert.That(_logs.Warnings(),
				Has.Some.Contains(MovedHostUrl).And.Contains(_host.Url),
				"Connecting to another host than the one that issued the credential was not reported.");
		});
	}

	[Test]
	public async Task A_socket_that_cannot_be_opened_is_reported_as_a_warning_naming_the_host()
	{
		await StoreCredentialIssuedByAsync(_host.Url);
		_host.WebSocketUnavailable = true;

		await using var plugin = Builder().Build();
		await plugin.StartAsync();

		await Wait.UntilAsync(() => _logs.Warnings().Any(message => message.Contains("socket", StringComparison.Ordinal)),
			TimeSpan.FromSeconds(10));

		Assert.That(_logs.Warnings(),
			Has.Some.Contains("Could not open the session socket").And.Contains(_host.Url));
	}

	private PluginHostBuilder Builder()
	{
		var builder = _manifest.CreateBuilder();

		builder.Configuration["MacroDeck:Plugin:HostUrl"] = _host.Url;
		builder.Configuration["MacroDeck:Plugin:StateDirectory"] = _stateDirectory;
		builder.Configuration["MacroDeck:Plugin:PairingEnabled"] = "false";
		builder.Services.AddSingleton<ILogger>(new LoggerConfiguration().MinimumLevel.Debug().WriteTo.Sink(_logs)
			.CreateLogger());

		return builder;
	}

	private Task StoreCredentialIssuedByAsync(string hostUrl)
		=> new FilePluginCredentialStore(Options.Create(new PluginHostOptions { StateDirectory = _stateDirectory }),
			new PluginMetadata { Id = "com.example.test", Name = "Test Plugin", Version = "1.0.0" },
			Serilog.Core.Logger.None).SaveAsync(new PluginCredentials("com.example.test", hostUrl, StoredSecret));

	private sealed class CollectingSink : ILogEventSink
	{
		private readonly Lock _gate = new();
		private readonly List<LogEvent> _events = [];

		public void Emit(LogEvent logEvent)
		{
			lock (_gate)
			{
				_events.Add(logEvent);
			}
		}

		public IReadOnlyList<string> Warnings()
		{
			lock (_gate)
			{
				return [.. _events.Where(e => e.Level == LogEventLevel.Warning).Select(e => e.RenderMessage(System.Globalization.CultureInfo.InvariantCulture))];
			}
		}
	}
}
