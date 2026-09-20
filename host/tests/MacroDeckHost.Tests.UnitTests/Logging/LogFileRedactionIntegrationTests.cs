using MacroDeckHost.Application.Logging;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Extensions;
using MacroDeckHost.Infrastructure.Logging;
using MacroDeckHost.Logging;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Logging;

public class LogFileRedactionIntegrationTests
{
	private const string Jwt = "eyJhbGciOiJIUzI1NiJ9.eyJzY29wZSI6ImFkbWluIn0.dBjftJeZ4CVP-mB92K27uhbUJU1p";

	private TestPaths _paths = null!;
	private Serilog.ILogger _previousLogger = null!;

	[SetUp]
	public void SetUp()
	{
		_paths = new TestPaths();
		_previousLogger = Log.Logger;
	}

	[TearDown]
	public void TearDown()
	{
		Log.Logger = _previousLogger;
		_paths.Cleanup();
	}

	[Test]
	public async Task RequestLog_Does_Not_Persist_The_Access_Token_Or_The_OAuth_Code()
	{
		// The framework's request-pipeline categories are noise floors now, so the line carrying the
		// query string only exists once the user asks for Debug - which is exactly when it must still
		// be redacted.
		var host = await new HostBuilder()
			.ConfigureSerilog(_paths, new LogLevelState(LogEntryLevel.Debug))
			.ConfigureWebHost(web => web
				.UseTestServer()
				.Configure(app => app.Run(context => context.Response.WriteAsync("ok"))))
			.StartAsync();

		using (host)
		{
			using var client = host.GetTestClient();
			await client.GetAsync($"/hubs/ui?id=Vf3d&access_token={Jwt}");
			await client.GetAsync("/api/integrations/oauth/callback?code=AQD5x9k-secret&state=b7f1");
			await host.StopAsync();
		}

		await Log.CloseAndFlushAsync();

		var contents = string.Concat(Directory.EnumerateFiles(_paths.LogsDirectory, "host-*.log")
			.Select(File.ReadAllText));

		var parsed = string.Join('\n',
			contents.Split('\n').Select(ParsedMessage).Where(message => message is not null));

		Assert.Multiple(() =>
		{
			Assert.That(contents, Is.Not.Empty, "the request log should have reached the file sink");
			Assert.That(contents, Does.Not.Contain(Jwt));
			Assert.That(contents, Does.Not.Contain("AQD5x9k-secret"));
			Assert.That(contents, Does.Contain("access_token=***"));
			Assert.That(contents, Does.Contain("code=***"));
			Assert.That(contents, Does.Contain("/hubs/ui"));
			Assert.That(contents, Does.Contain("state=b7f1"));

			Assert.That(parsed, Is.Not.Empty, "the request log should have parsed back out of the file");
			Assert.That(parsed, Does.Not.Contain(Jwt));
			Assert.That(parsed, Does.Not.Contain("AQD5x9k-secret"));
			Assert.That(parsed, Does.Contain("access_token=***"));
			Assert.That(parsed, Does.Contain("code=***"));
		});
	}

	[Test]
	public async Task Log_File_Does_Not_Persist_The_User_Name_From_A_Path()
	{
		var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
		if (string.IsNullOrWhiteSpace(home))
		{
			Assert.Ignore("The operating system reports no home directory.");
		}

		var host = await new HostBuilder()
			.ConfigureSerilog(_paths, new LogLevelState(LogEntryLevel.Information))
			.StartAsync();

		using (host)
		{
			Log.Error(new InvalidOperationException(@"C:\Users\other-user\Documents\profile.json is missing"),
				"Opening database at {Path}",
				Path.Combine(home, "MacroDeck", "database.db"));
			await host.StopAsync();
		}

		await Log.CloseAndFlushAsync();

		var contents = string.Concat(Directory.EnumerateFiles(_paths.LogsDirectory, "host-*.log")
			.Select(File.ReadAllText));

		Assert.Multiple(() =>
		{
			Assert.That(contents, Is.Not.Empty, "the entry should have reached the file sink");
			Assert.That(contents, Does.Not.Contain(home));
			Assert.That(contents, Does.Not.Contain("other-user"));
			Assert.That(contents, Does.Contain("MacroDeck"));
			Assert.That(contents, Does.Contain(@"C:\Users\<user>\Documents\profile.json"));
		});
	}

	[Test]
	public async Task Http_Logs_Only_Reach_The_File_When_Debug_Is_Enabled()
	{
		var state = new LogLevelState(LogEntryLevel.Information);
		using (var host = await Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder()
			.ConfigureSerilog(_paths, state)
			.ConfigureWebHost(web => web
				.UseTestServer()
				.Configure(app =>
				{
					app.UseRequestOutcomeLogging();
					app.Run(context => context.Response.WriteAsync("ok"));
				}))
			.StartAsync())
		{
			Assert.That(host.Services.GetServices<ILoggerProvider>(), Is.Empty,
				"Default providers, including Windows EventLog, must be removed.");
			using var client = host.GetTestClient();
			var outgoing = host.Services.GetRequiredService<ILoggerFactory>()
				.CreateLogger("System.Net.Http.HttpClient.HealthProbe.LogicalHandler");
			var logInformation = LoggerMessage.Define<string>(LogLevel.Information, new EventId(1), "{Message}");
			var logWarning = LoggerMessage.Define<string>(LogLevel.Warning, new EventId(2), "{Message}");
			await client.GetAsync("/_macrodeck/health?phase=quiet");
			logInformation(outgoing, "outgoing-quiet", null);
			logWarning(outgoing, "outgoing-warning", null);

			state.Minimum = LogEntryLevel.Debug;
			await client.GetAsync("/_macrodeck/health?phase=debug");
			logInformation(outgoing, "outgoing-debug", null);

			state.Minimum = LogEntryLevel.Information;
			await client.GetAsync("/_macrodeck/health?phase=quiet-again");
			logInformation(outgoing, "outgoing-quiet-again", null);
			await host.StopAsync();
		}

		await Log.CloseAndFlushAsync();
		var contents = string.Concat(Directory.EnumerateFiles(_paths.LogsDirectory, "host-*.log")
			.Select(File.ReadAllText));
		Assert.Multiple(() =>
		{
			Assert.That(contents, Does.Not.Contain("phase=quiet"));
			Assert.That(contents, Does.Not.Contain("outgoing-quiet"));
			Assert.That(contents, Does.Contain("phase=debug"));
			Assert.That(contents, Does.Contain("outgoing-debug"));
			Assert.That(contents, Does.Contain("outgoing-warning"));
			Assert.That(contents.Split('\n').Count(line => line.Contains("HTTP ", StringComparison.Ordinal)),
				Is.EqualTo(1), "Only the Debug request should produce an outcome entry.");
		});
	}

	private static string? ParsedMessage(string line)
		=> HostLogParser.TryParseHeader(line, out var header) ? header.Message : null;
}
