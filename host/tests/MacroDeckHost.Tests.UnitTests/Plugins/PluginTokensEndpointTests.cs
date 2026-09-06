using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MacroDeck.Plugin.Protocol.Auth;
using MacroDeckHost.Application.Logging;
using MacroDeckHost.Application.Paths;
using MacroDeckHost.Application.Persistence.Repositories;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Plugins;
using MacroDeckHost.Infrastructure.Persistence;
using MacroDeckHost.Tests.UnitTests.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Plugins;

/// <summary>Issue #753: removing a developer credential for good. Each test gets its own host and data
/// directory so a listing assertion can say "exactly these tokens" instead of "at least these".</summary>
[NonParallelizable]
public class PluginTokensEndpointTests
{
	private IHost _host = null!;
	private HttpClient _client = null!;
	private string _dataDir = null!;
	private string? _previousDataDir;
	private string _adminToken = null!;
	private RecordingUiTransport _uiTransport = null!;

	[SetUp]
	public async Task SetUp()
	{
		_dataDir = Path.Combine(Path.GetTempPath(), "macro-deck-tests", Guid.NewGuid().ToString("N"));
		_previousDataDir = Environment.GetEnvironmentVariable("MACRODECK_DATA_DIR");
		Environment.SetEnvironmentVariable("MACRODECK_DATA_DIR", _dataDir);

		var paths = new MacroDeckPaths();
		DatabaseMigrationHelper.MigrateDatabase(paths);

		_uiTransport = new RecordingUiTransport();
		_host = await new HostBuilder()
			.ConfigureWebHost(builder =>
			{
				builder.UseTestServer();
				builder.UseStartup<Startup>();
				builder.ConfigureTestServices(services =>
				{
					services.RemoveAll<IHostedService>();
					services.AddSingleton(Log.Logger);
					services.AddSingleton<ILogLevelState>(new LogLevelState(LogEntryLevel.Information));
					services.AddSingleton<IStartupFilter, FakeConnectionStartupFilter>();
					services.RemoveAll<StartupReadiness>();
					services.AddSingleton(CompletedStartupReadiness());
					services.RemoveAll<IUiTransport>();
					services.AddSingleton<IUiTransport>(_uiTransport);
				});
			})
			.StartAsync();

		_client = _host.GetTestClient();

		var setup = await Send(HttpMethod.Post,
			"/api/auth/setup",
			shape: FakeConnectionShape.Loopback,
			body: new { username = "admin", password = "password123" });
		Assert.That(setup.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

		var login = await Send(HttpMethod.Post,
			"/api/auth/login",
			shape: FakeConnectionShape.PublicLan,
			body: new { username = "admin", password = "password123", scope = "admin" });
		Assert.That(login.StatusCode, Is.EqualTo(HttpStatusCode.OK));
		_adminToken = (await ReadJson(login)).GetProperty("accessToken").GetString()!;

		var enableDeveloperMode = await Send(HttpMethod.Put,
			"/api/settings/developer",
			shape: FakeConnectionShape.PublicLan,
			bearerToken: _adminToken,
			body: new { enabled = true });
		Assert.That(enableDeveloperMode.StatusCode, Is.EqualTo(HttpStatusCode.OK));
	}

	[TearDown]
	public async Task TearDown()
	{
		_client.Dispose();
		await _host.StopAsync();
		_host.Dispose();
		Environment.SetEnvironmentVariable("MACRODECK_DATA_DIR", _previousDataDir);
		if (Directory.Exists(_dataDir))
		{
			Directory.Delete(_dataDir, recursive: true);
		}
	}

	[Test]
	public async Task Removing_a_revoked_token_takes_its_registrations_with_it_and_leaves_another_tokens_alone()
	{
		var doomed = await CreateTokenAsync("Doomed");
		var kept = await CreateTokenAsync("Kept");
		var (removedPluginId, removedSecret) = await EnrollAsync(doomed.Plaintext);
		await EnrollAsync(doomed.Plaintext);
		var (keptPluginId, _) = await EnrollAsync(kept.Plaintext);

		await RevokeAsync(doomed.Id);
		var removal = await DeleteTokenAsync(doomed.Id);

		Assert.That(removal.StatusCode, Is.EqualTo(HttpStatusCode.OK));
		Assert.That((await ReadJson(removal)).GetProperty("success").GetBoolean(), Is.True);

		var tokens = (await ListTokensAsync()).EnumerateArray().ToList();
		var registrations = await ReadRegistrationRowsAsync();
		var orphanedSession = await CreateSessionAsync(removedPluginId, removedSecret);

		Assert.Multiple(() =>
		{
			Assert.That(tokens.Select(token => token.GetProperty("id").GetGuid()), Is.EqualTo(new[] { kept.Id }));
			Assert.That(RegistrationIdsOf(tokens.Single()), Is.EqualTo(new[] { keptPluginId }));
			Assert.That(registrations.Select(registration => registration.PluginId),
				Is.EqualTo(new[] { keptPluginId }));
			Assert.That(orphanedSession.StatusCode,
				Is.EqualTo(HttpStatusCode.Unauthorized),
				"the removed registration must be gone, not merely detached from its token");
		});
	}

	[Test]
	public async Task Removing_a_token_spares_an_interactively_paired_plugin()
	{
		var doomed = await CreateTokenAsync("Doomed");
		var (enrolledPluginId, _) = await EnrollAsync(doomed.Plaintext);
		var (pairedPluginId, pairedSecret) = await PairPluginAsync();

		await RevokeAsync(doomed.Id);
		var removal = await DeleteTokenAsync(doomed.Id);
		Assert.That((await ReadJson(removal)).GetProperty("success").GetBoolean(), Is.True);

		var registrations = await ReadRegistrationRowsAsync();
		var pairedRegistrations = await ListPairedRegistrationsAsync();
		var pairedSession = await CreateSessionAsync(pairedPluginId, pairedSecret);

		Assert.Multiple(() =>
		{
			Assert.That(registrations.Select(registration => registration.PluginId),
				Is.EqualTo(new[] { pairedPluginId }),
				"a paired registration carries no token id, and must not be swept up with the rows that " +
				$"belong to the removed token (gone: {enrolledPluginId})");
			Assert.That(registrations.Select(registration => registration.AccessTokenId),
				Is.EqualTo(new Guid?[] { null }),
				"the surviving row is the ambiguous kind - no token id, yet not a managed plugin");
			Assert.That(pairedRegistrations, Is.EqualTo(new[] { pairedPluginId }));
			Assert.That(pairedSession.StatusCode,
				Is.EqualTo(HttpStatusCode.Created),
				"the paired plugin's own credential must still open a session");
		});
	}

	[Test]
	public async Task A_live_token_is_refused_and_keeps_working()
	{
		var token = await CreateTokenAsync("Live");
		var (pluginId, secret) = await EnrollAsync(token.Plaintext);

		var removal = await DeleteTokenAsync(token.Id);
		var body = await ReadJson(removal);

		var tokens = (await ListTokensAsync()).EnumerateArray().ToList();
		var session = await CreateSessionAsync(pluginId, secret);

		Assert.Multiple(() =>
		{
			Assert.That(removal.StatusCode, Is.EqualTo(HttpStatusCode.OK));
			Assert.That(body.GetProperty("success").GetBoolean(), Is.False);
			Assert.That(body.GetProperty("error").GetProperty("code").GetString(), Is.EqualTo("not_revoked"));
			Assert.That(MessageOf(body.GetProperty("error")), Is.Not.Empty);

			Assert.That(tokens.Select(entry => entry.GetProperty("id").GetGuid()), Is.EqualTo(new[] { token.Id }));
			Assert.That(RegistrationIdsOf(tokens.Single()), Is.EqualTo(new[] { pluginId }));
			Assert.That(session.StatusCode,
				Is.EqualTo(HttpStatusCode.Created),
				"refusing the removal must not have revoked the token as a side effect");
		});
	}

	[Test]
	public async Task An_unknown_token_is_reported_as_not_found()
	{
		var removal = await DeleteTokenAsync(Guid.NewGuid());
		var body = await ReadJson(removal);

		Assert.Multiple(() =>
		{
			Assert.That(removal.StatusCode, Is.EqualTo(HttpStatusCode.OK));
			Assert.That(body.GetProperty("success").GetBoolean(), Is.False);
			Assert.That(body.GetProperty("error").GetProperty("code").GetString(), Is.EqualTo("not_found"));
		});
	}

	[Test]
	public async Task A_removal_tells_the_ui_the_token_list_changed()
	{
		var token = await CreateTokenAsync("Doomed");
		await RevokeAsync(token.Id);
		_uiTransport.GroupMessages.Clear();

		var removal = await DeleteTokenAsync(token.Id);
		Assert.That((await ReadJson(removal)).GetProperty("success").GetBoolean(), Is.True);

		Assert.That(_uiTransport.GroupMessages.Select(sent => sent.Message),
			Has.Some.InstanceOf<PluginTokensChangedEvent>());
	}

	// A TransportError message is a LocalizedText: a bare string when literal, a {"$localized":{…}}
	// reference otherwise.
	private static string MessageOf(JsonElement error)
	{
		var message = error.GetProperty("message");

		return message.ValueKind == JsonValueKind.String
			? message.GetString()!
			: message.GetProperty("$localized").GetProperty("key").GetString()!;
	}

	private static List<string> RegistrationIdsOf(JsonElement token)
		=> token.GetProperty("registrations").EnumerateArray()
			.Select(registration => registration.GetProperty("pluginId").GetString()!)
			.ToList();

	private async Task<IReadOnlyList<Domain.Entities.PluginRegistrationEntity>> ReadRegistrationRowsAsync()
	{
		using var scope = _host.Services.CreateScope();

		return await scope.ServiceProvider.GetRequiredService<IPluginRegistrationRepository>().GetAll();
	}

	private async Task<JsonElement> ListTokensAsync()
	{
		var response = await Send(HttpMethod.Get,
			"/api/plugin-tokens",
			shape: FakeConnectionShape.PublicLan,
			bearerToken: _adminToken);
		Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

		return (await ReadJson(response)).GetProperty("tokens");
	}

	private async Task<(Guid Id, string Plaintext)> CreateTokenAsync(string name)
	{
		var response = await Send(HttpMethod.Post,
			"/api/plugin-tokens",
			shape: FakeConnectionShape.PublicLan,
			bearerToken: _adminToken,
			body: new { name, expiresInDays = (int?)null });
		Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
		var json = await ReadJson(response);

		return (json.GetProperty("token").GetProperty("id").GetGuid(), json.GetProperty("plaintext").GetString()!);
	}

	private async Task RevokeAsync(Guid id)
	{
		var response = await Send(HttpMethod.Post,
			$"/api/plugin-tokens/{id}/revoke",
			shape: FakeConnectionShape.PublicLan,
			bearerToken: _adminToken);
		Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
		Assert.That((await ReadJson(response)).GetProperty("success").GetBoolean(), Is.True);
	}

	private Task<HttpResponseMessage> DeleteTokenAsync(Guid id)
		=> Send(HttpMethod.Delete,
			$"/api/plugin-tokens/{id}",
			shape: FakeConnectionShape.PublicLan,
			bearerToken: _adminToken);

	private async Task<(string PluginId, string Secret)> EnrollAsync(string enrollmentToken)
	{
		var pluginId = $"com.example.t{Guid.NewGuid():N}";
		var response = await Send(HttpMethod.Post,
			"/api/plugins/registration",
			shape: FakeConnectionShape.Loopback,
			body: new { pluginId, displayName = "Example Plugin" },
			extraHeaders: new Dictionary<string, string>
				{ [PluginAuthDefaults.EnrollmentTokenHeaderName] = enrollmentToken });
		Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));

		return (pluginId, (await ReadJson(response)).GetProperty("pluginSecret").GetString()!);
	}

	private async Task<(string PluginId, string Secret)> PairPluginAsync()
	{
		var pluginId = $"com.example.p{Guid.NewGuid():N}";
		var verifier = $"verifier-{Guid.NewGuid():N}-{Guid.NewGuid():N}";
		var digest = SHA256.HashData(Encoding.UTF8.GetBytes(verifier));
		var challenge = Convert.ToBase64String(digest).TrimEnd('=').Replace('+', '-').Replace('/', '_');

		var created = await Send(HttpMethod.Post,
			"/api/plugins/pairing",
			shape: FakeConnectionShape.Loopback,
			body: new
			{
				pluginId, displayName = "Paired Plugin", codeChallenge = challenge, codeChallengeMethod = "S256"
			});
		Assert.That(created.StatusCode, Is.EqualTo(HttpStatusCode.Created));
		var requestId = (await ReadJson(created)).GetProperty("requestId").GetString()!;

		var approve = await Send(HttpMethod.Post,
			$"/api/plugin-pairing/requests/{requestId}/approve",
			shape: FakeConnectionShape.Loopback,
			body: new { replaceExistingRegistration = false });
		Assert.That(approve.StatusCode, Is.EqualTo(HttpStatusCode.OK));

		var redemption = await Send(HttpMethod.Post,
			$"/api/plugins/pairing/{requestId}/redemption",
			shape: FakeConnectionShape.Loopback,
			body: new { codeVerifier = verifier });
		Assert.That(redemption.StatusCode, Is.EqualTo(HttpStatusCode.Created));

		return (pluginId, (await ReadJson(redemption)).GetProperty("pluginSecret").GetString()!);
	}

	private async Task<IReadOnlyList<string>> ListPairedRegistrationsAsync()
	{
		var response = await Send(HttpMethod.Get, "/api/plugin-pairing/registrations", FakeConnectionShape.Loopback);
		Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

		return (await ReadJson(response)).GetProperty("registrations").EnumerateArray()
			.Select(registration => registration.GetProperty("pluginId").GetString()!)
			.ToList();
	}

	private Task<HttpResponseMessage> CreateSessionAsync(string pluginId, string secret)
		=> Send(HttpMethod.Post,
			"/api/plugins/sessions",
			shape: FakeConnectionShape.Loopback,
			body: new
			{
				requestedVersion = new { minimum = 1, maximum = 1 }, capabilities = Array.Empty<object>()
			},
			extraHeaders: new Dictionary<string, string>
			{
				[PluginAuthDefaults.PluginIdHeaderName] = pluginId, [PluginAuthDefaults.PluginSecretHeaderName] = secret
			});

	private static StartupReadiness CompletedStartupReadiness()
	{
		var readiness = new StartupReadiness();
		readiness.MarkCachesReady();
		readiness.MarkVariablesReady();
		return readiness;
	}

	private async Task<HttpResponseMessage> Send(
		HttpMethod method,
		string path,
		FakeConnectionShape shape,
		string? bearerToken = null,
		object? body = null,
		IDictionary<string, string>? extraHeaders = null)
	{
		using var request = new HttpRequestMessage(method, path);
		if (body is not null)
		{
			request.Content = JsonContent.Create(body);
		}

		request.Headers.Add(FakeConnectionStartupFilter.ShapeHeader, shape.ToString());

		if (bearerToken is not null)
		{
			request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
		}

		if (extraHeaders is not null)
		{
			foreach (var (key, value) in extraHeaders)
			{
				request.Headers.TryAddWithoutValidation(key, value);
			}
		}

		return await _client.SendAsync(request);
	}

	private static async Task<JsonElement> ReadJson(HttpResponseMessage response)
		=> JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
}
