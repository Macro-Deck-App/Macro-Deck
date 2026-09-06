using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MacroDeckHost.Application.Connect;
using MacroDeckHost.Application.Logging;
using MacroDeckHost.Application.Paths;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Infrastructure.Connect;
using MacroDeckHost.Infrastructure.Persistence;
using MacroDeckHost.Tests.UnitTests.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Connect;

[NonParallelizable]
public class ConnectApiPolicyTests
{
	private const string AvatarUrl = "https://accounts.macro-deck.app/avatars/0123456789abcdef0123456789abcdef.png";

	private static readonly byte[] _avatarBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];

	private static readonly string[] _mutations = ["signin/start", "signin/cancel", "signout"];

	private IHost _host = null!;
	private HttpClient _client = null!;
	private string _dataDir = null!;
	private string? _previousDataDir;
	private string _clientToken = null!;
	private string _adminToken = null!;
	private FakeConnectSessionService _session = null!;

	[OneTimeSetUp]
	public async Task OneTimeSetUp()
	{
		_dataDir = Path.Combine(Path.GetTempPath(), "macro-deck-tests", Guid.NewGuid().ToString("N"));
		_previousDataDir = Environment.GetEnvironmentVariable("MACRODECK_DATA_DIR");
		Environment.SetEnvironmentVariable("MACRODECK_DATA_DIR", _dataDir);

		var paths = new MacroDeckPaths();
		DatabaseMigrationHelper.MigrateDatabase(paths);

		_session = new FakeConnectSessionService();

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

					// Without these, a sign-out would reach out to accounts.macro-deck.app from a unit test.
					services.RemoveAll<IConnectSessionService>();
					services.RemoveAll<IConnectSignInFlow>();
					services.RemoveAll<IConnectIdentityClient>();
					services.RemoveAll<IConnectAvatarCache>();
					services.AddSingleton<IConnectSessionService>(_session);
					services.AddSingleton<IConnectIdentityClient>(new FakeConnectIdentityClient(TimeProvider.System));
					services.AddSingleton<IConnectAvatarCache>(new ConnectAvatarCache(_session,
						paths,
						new StubAvatarHandler(),
						Log.Logger));
				});
			})
			.StartAsync();

		_client = _host.GetTestClient();

		var setup = await Send(HttpMethod.Post,
			"/api/auth/setup",
			new { username = "admin", password = "password123" },
			loopback: true);
		Assert.That(setup.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

		_clientToken = await LoginAndGetToken("client");
		_adminToken = await LoginAndGetToken("admin");
	}

	[OneTimeTearDown]
	public async Task OneTimeTearDown()
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
	public async Task Anonymous_requests_never_reach_any_connect_route()
	{
		var session = await Send(HttpMethod.Get, "/api/connect/session");
		var avatar = await Send(HttpMethod.Get, "/api/connect/avatar");
		var mutations = new List<HttpResponseMessage>();

		foreach (var route in _mutations)
		{
			mutations.Add(await Send(HttpMethod.Post, $"/api/connect/{route}", body: new { }));
		}

		Assert.Multiple(() =>
		{
			Assert.That(session.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
			Assert.That(avatar.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));

			foreach (var mutation in mutations)
			{
				Assert.That(mutation.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
			}
		});
	}

	[Test]
	public async Task Client_scope_reaches_no_part_of_the_account_surface()
	{
		// The web client is deliberately unaware that Macro Deck accounts exist (issue #673): it neither
		// signs in nor reads who is signed in.
		var session = await Send(HttpMethod.Get, "/api/connect/session", bearerToken: _clientToken);
		var avatar = await Send(HttpMethod.Get, "/api/connect/avatar", bearerToken: _clientToken);

		Assert.Multiple(() =>
		{
			Assert.That(session.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
			Assert.That(avatar.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
		});

		foreach (var route in _mutations)
		{
			var mutation = await Send(HttpMethod.Post, $"/api/connect/{route}", bearerToken: _clientToken);
			Assert.That(mutation.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden), route);
		}
	}

	[Test]
	public async Task An_admin_token_signs_in_from_off_the_host_machine()
	{
		// The reason issue #673 exists: the desktop UI opened over the public listener could not start a
		// sign-in at all, because the loopback redirect it needed only ever resolved to the host itself.
		// The device code grant has no redirect, so a remote admin connection is now enough.
		_session.Current = ConnectSessionSnapshot.SignedOut;

		var session = await Send(HttpMethod.Get, "/api/connect/session", bearerToken: _adminToken);
		var remote = new List<HttpResponseMessage>();
		var loopback = new List<HttpResponseMessage>();

		foreach (var route in _mutations)
		{
			remote.Add(await Send(HttpMethod.Post, $"/api/connect/{route}", new { }, bearerToken: _adminToken));
			loopback.Add(await Send(HttpMethod.Post, $"/api/connect/{route}", new { }, loopback: true));
		}

		Assert.Multiple(() =>
		{
			Assert.That(session.StatusCode, Is.EqualTo(HttpStatusCode.OK));

			foreach (var response in remote)
			{
				Assert.That((int)response.StatusCode,
					Is.InRange(200, 299),
					"an admin token must be enough now that no loopback redirect is involved");
			}

			foreach (var response in loopback)
			{
				Assert.That((int)response.StatusCode, Is.InRange(200, 299));
			}
		});
	}

	[Test]
	public async Task Starting_a_sign_in_remotely_returns_a_code_and_a_page_to_confirm_it_on()
	{
		_session.Current = ConnectSessionSnapshot.SignedOut;

		var json = await ReadJson(await Send(HttpMethod.Post,
			"/api/connect/signin/start",
			new { },
			bearerToken: _adminToken));

		Assert.Multiple(() =>
		{
			Assert.That(json.GetProperty("userCode").GetString(), Is.Not.Empty);
			Assert.That(json.GetProperty("verificationUriComplete").GetString(),
				Does.Contain(json.GetProperty("userCode").GetString()!),
				"the opened page has to carry the code, so nothing has to be typed");
			Assert.That(json.GetProperty("verificationUri").GetString(), Is.Not.Empty);
		});
	}

	[Test]
	public async Task A_signed_out_session_reports_nothing_but_the_account_management_url()
	{
		_session.Current = ConnectSessionSnapshot.SignedOut;

		var json = await ReadJson(await Send(HttpMethod.Get, "/api/connect/session", bearerToken: _adminToken));

		Assert.Multiple(() =>
		{
			Assert.That(json.GetProperty("status").GetString(), Is.EqualTo("signedOut"));
			Assert.That(json.GetProperty("account").ValueKind, Is.EqualTo(JsonValueKind.Null));
			Assert.That(json.GetProperty("accountManagementUrl").GetString(),
				Is.EqualTo("https://accounts.macro-deck.app/"));
		});
	}

	[Test]
	public async Task The_avatar_is_proxied_and_its_url_never_reaches_the_session_payload()
	{
		_session.Current = SignedIn(AvatarUrl);

		var avatar = await Send(HttpMethod.Get, "/api/connect/avatar", bearerToken: _adminToken);
		var bytes = await avatar.Content.ReadAsByteArrayAsync();
		var sessionBody = await (await Send(HttpMethod.Get, "/api/connect/session", bearerToken: _adminToken))
			.Content.ReadAsStringAsync();

		_session.Current = SignedIn(null);
		var missing = await Send(HttpMethod.Get, "/api/connect/avatar", bearerToken: _adminToken);
		var missingBody = await missing.Content.ReadAsByteArrayAsync();

		Assert.Multiple(() =>
		{
			Assert.That(avatar.StatusCode, Is.EqualTo(HttpStatusCode.OK));
			Assert.That(avatar.Content.Headers.ContentType?.MediaType, Is.EqualTo("image/png"));
			Assert.That(bytes, Is.EqualTo(_avatarBytes));
			Assert.That(sessionBody, Does.Not.Contain("accounts.macro-deck.app/avatars/"));
			Assert.That(sessionBody, Does.Contain("\"avatarAvailable\":true"));
			Assert.That(missing.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
			Assert.That(missingBody, Is.Empty);
		});
	}

	private static ConnectSessionSnapshot SignedIn(string? pictureUrl)
		=> new(ConnectAccountStatus.SignedIn,
			ConnectConnectivity.Ok,
			new ConnectAccount("sub-1", "Ada Lovelace", pictureUrl, null, []),
			null,
			null,
			null);

	private static StartupReadiness CompletedStartupReadiness()
	{
		var readiness = new StartupReadiness();
		readiness.MarkCachesReady();
		readiness.MarkVariablesReady();

		return readiness;
	}

	private async Task<string> LoginAndGetToken(string scope)
	{
		var response = await Send(HttpMethod.Post,
			"/api/auth/login",
			new { username = "admin", password = "password123", scope });
		var json = await ReadJson(response);

		return json.GetProperty("accessToken").GetString()!;
	}

	private async Task<HttpResponseMessage> Send(
		HttpMethod method,
		string path,
		object? body = null,
		string? bearerToken = null,
		bool loopback = false)
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

		return await _client.SendAsync(request);
	}

	private static async Task<JsonElement> ReadJson(HttpResponseMessage response)
		=> JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

	private sealed class StubAvatarHandler : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(
			HttpRequestMessage request,
			CancellationToken cancellationToken)
		{
			var response = request.RequestUri?.ToString() == AvatarUrl
				? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(_avatarBytes) }
				: new HttpResponseMessage(HttpStatusCode.NotFound);

			return Task.FromResult(response);
		}
	}
}
