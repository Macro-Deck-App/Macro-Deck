using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MacroDeckHost.Application.Paths;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Api;

[NonParallelizable]
public class BackupEndpointsTests
{
	private const string LoopbackHeader = "X-Test-Loopback";

	private IHost _host = null!;
	private string _publicToken = null!;
	private HttpClient _client = null!;
	private string _dataDir = null!;
	private string? _previousDataDir;

	[OneTimeSetUp]
	public async Task OneTimeSetUp()
	{
		_dataDir = Path.Combine(Path.GetTempPath(), "macro-deck-tests", Guid.NewGuid().ToString("N"));
		_previousDataDir = Environment.GetEnvironmentVariable("MACRODECK_DATA_DIR");
		Environment.SetEnvironmentVariable("MACRODECK_DATA_DIR", _dataDir);

		DatabaseMigrationHelper.MigrateDatabase(new MacroDeckPaths());

		_host = await new HostBuilder()
			.ConfigureWebHost(builder =>
			{
				builder.UseTestServer();
				builder.UseStartup<Startup>();
				builder.ConfigureTestServices(services =>
				{
					services.RemoveAll<IHostedService>();
					services.AddSingleton(Log.Logger);
					services.AddSingleton<IStartupFilter, LoopbackStartupFilter>();
					services.RemoveAll<StartupReadiness>();
					services.AddSingleton(CompletedStartupReadiness());
				});
			})
			.StartAsync();

		_client = _host.GetTestClient();

		var setup = await Send(HttpMethod.Post,
			"/api/auth/setup",
			new { username = "admin", password = "password123" });
		Assert.That(setup.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

		var loginResponse = await Send(HttpMethod.Post,
			"/api/auth/login",
			new { username = "admin", password = "password123", scope = "admin" });
		var loginBody = await loginResponse.Content.ReadAsStringAsync();
		Assert.That(loginResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK), loginBody);
		_publicToken = JsonDocument.Parse(loginBody).RootElement.GetProperty("accessToken").GetString()!;
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
	public async Task Creating_then_downloading_then_deleting_a_backup_round_trips()
	{
		var created = await ReadJson(await Send(HttpMethod.Post, "/api/backups", new { note = "round trip" }));
		Assert.That(created.GetProperty("success").GetBoolean(), Is.True, created.ToString());
		var backupId = created.GetProperty("backup").GetProperty("id").GetString();

		var listed = await ReadJson(await Send(HttpMethod.Get, "/api/backups"));
		var download = await Send(HttpMethod.Get, $"/api/backups/{backupId}/download");
		var archive = await download.Content.ReadAsByteArrayAsync();

		var deleted = await ReadJson(await Send(HttpMethod.Delete, $"/api/backups/{backupId}"));
		var afterDelete = await ReadJson(await Send(HttpMethod.Get, "/api/backups"));

		Assert.Multiple(() =>
		{
			Assert.That(Ids(listed), Does.Contain(backupId));
			Assert.That(download.StatusCode, Is.EqualTo(HttpStatusCode.OK));
			Assert.That(download.Content.Headers.ContentDisposition!.FileNameStar ??
				download.Content.Headers.ContentDisposition!.FileName,
				Does.Contain(".macroDeckBackup"));
			Assert.That(archive, Is.Not.Empty);
			Assert.That(deleted.GetProperty("success").GetBoolean(), Is.True);
			Assert.That(Ids(afterDelete), Does.Not.Contain(backupId));
		});
	}

	// A backup archive carries the data protection key ring, the auth signing key, the TLS private key and
	// every stored secret. engineering/api/authentication.md forbids handing that to the public listener.
	[Test]
	public async Task The_public_listener_cannot_reach_the_endpoints_that_expose_secrets()
	{
		var created = await ReadJson(await Send(HttpMethod.Post, "/api/backups", new { note = "public check" }));
		var backupId = created.GetProperty("backup").GetProperty("id").GetString();

		// Authenticated as a full admin, only reaching the host over the public listener - so a 404 here
		// proves the loopback boundary itself, not merely that the request was unauthenticated.
		var listed = await SendPublic(HttpMethod.Get, "/api/backups");
		var download = await SendPublic(HttpMethod.Get, $"/api/backups/{backupId}/download");
		var reveal = await SendPublic(HttpMethod.Post, "/api/backups/recovery-key/reveal", new { confirm = true });
		var regenerate = await SendPublic(HttpMethod.Post,
			"/api/backups/recovery-key/regenerate",
			new { confirm = true, acknowledgeExistingBackupsBecomeUnreadable = true });
		var prepare = await SendPublic(HttpMethod.Post,
			"/api/backups/restore/prepare",
			new { backupId, components = Array.Empty<string>() });

		Assert.Multiple(() =>
		{
			Assert.That(listed.StatusCode, Is.EqualTo(HttpStatusCode.OK), "listing stays available in the browser UI");
			Assert.That(download.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
			Assert.That(reveal.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
			Assert.That(regenerate.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
			Assert.That(prepare.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
		});
	}

	[Test]
	public async Task The_recovery_key_is_absent_from_every_response_that_is_not_a_deliberate_reveal()
	{
		await Send(HttpMethod.Post, "/api/backups", new { note = "key leak check" });

		var revealed = await ReadJson(await Send(HttpMethod.Post,
			"/api/backups/recovery-key/reveal",
			new { confirm = true }));
		var key = revealed.GetProperty("key").GetString();

		var list = await ReadRaw(HttpMethod.Get, "/api/backups");
		var settings = await ReadRaw(HttpMethod.Get, "/api/backups/settings");
		var status = await ReadRaw(HttpMethod.Get, "/api/backups/status");
		var state = await ReadRaw(HttpMethod.Get, "/api/backups/recovery-key");

		Assert.Multiple(() =>
		{
			Assert.That(key, Is.Not.Null.And.Not.Empty);
			Assert.That(list, Does.Not.Contain(key!));
			Assert.That(settings, Does.Not.Contain(key!));
			Assert.That(status, Does.Not.Contain(key!));
			Assert.That(state, Does.Not.Contain(key!), "the state endpoint reports availability, not the key");
		});
	}

	[Test]
	public async Task Revealing_the_recovery_key_requires_an_explicit_confirmation()
	{
		var withoutConfirmation = await Send(HttpMethod.Post,
			"/api/backups/recovery-key/reveal",
			new { confirm = false });
		var body = await withoutConfirmation.Content.ReadAsStringAsync();

		var withConfirmation = await ReadJson(await Send(HttpMethod.Post,
			"/api/backups/recovery-key/reveal",
			new { confirm = true }));

		Assert.Multiple(() =>
		{
			Assert.That(body, Does.Not.Contain("MDBK1"));
			Assert.That(withConfirmation.GetProperty("key").GetString(), Does.StartWith("MDBK1"));
		});
	}

	// Rotation is what a user reaches for after leaking an exported key, and it silently strands every
	// existing backup. One click is not enough to express that.
	[Test]
	public async Task Regenerating_the_recovery_key_needs_both_confirmations()
	{
		var onlyConfirmed = await ReadJson(await Send(HttpMethod.Post,
			"/api/backups/recovery-key/regenerate",
			new { confirm = true }));
		var onlyAcknowledged = await ReadJson(await Send(HttpMethod.Post,
			"/api/backups/recovery-key/regenerate",
			new { confirm = false, acknowledgeExistingBackupsBecomeUnreadable = true }));

		Assert.Multiple(() =>
		{
			Assert.That(onlyConfirmed.GetProperty("success").GetBoolean(), Is.False);
			Assert.That(onlyConfirmed.TryGetProperty("key", out var withoutAck) &&
				withoutAck.ValueKind
					is not JsonValueKind.Null,
				Is.False,
				"no key may be handed out on a refused rotation");
			Assert.That(onlyAcknowledged.GetProperty("success").GetBoolean(), Is.False);
		});
	}

	// Component groups travel by name. Sending the numeric enum value would tie every client to the
	// declaration order of BackupComponentGroup, and a client posting the name it was given back to the
	// host would fail model binding before any handler ran.
	[Test]
	public async Task Component_groups_cross_the_wire_by_name()
	{
		var created = await ReadJson(await Send(HttpMethod.Post, "/api/backups", new { note = "wire format" }));
		var backupId = created.GetProperty("backup").GetProperty("id").GetString();
		var components = created.GetProperty("backup").GetProperty("components");

		var echoed = components.EnumerateArray().Select(value => value.GetString()).ToList();
		var prepare = await ReadJson(await Send(HttpMethod.Post,
			"/api/backups/restore/prepare",
			new { backupId, components = echoed }));

		Assert.Multiple(() =>
		{
			Assert.That(components.EnumerateArray().Select(value => value.ValueKind),
				Is.All.EqualTo(JsonValueKind.String));
			Assert.That(echoed, Does.Contain("Profiles"));
			Assert.That(prepare.GetProperty("success").GetBoolean(),
				Is.True,
				"the host has to accept the very names it just handed out");
		});

		await Send(HttpMethod.Post,
			"/api/backups/restore/cancel",
			new { restoreId = prepare.GetProperty("restoreId").GetGuid() });
	}

	private static IEnumerable<string?> Ids(JsonElement listed)
		=> listed.GetProperty("backups").EnumerateArray().Select(b => b.GetProperty("id").GetString());

	private async Task<string> ReadRaw(HttpMethod method, string path)
		=> await (await Send(method, path)).Content.ReadAsStringAsync();

	private Task<HttpResponseMessage> Send(HttpMethod method, string path, object? body = null)
		=> Dispatch(method, path, body, loopback: true);

	private Task<HttpResponseMessage> SendPublic(HttpMethod method, string path, object? body = null)
		=> Dispatch(method, path, body, loopback: false);

	private async Task<HttpResponseMessage> Dispatch(HttpMethod method, string path, object? body, bool loopback)
	{
		using var request = new HttpRequestMessage(method, path);
		if (body is not null)
		{
			request.Content = JsonContent.Create(body);
		}

		if (loopback)
		{
			request.Headers.Add(LoopbackHeader, "1");
		}
		else if (_publicToken is not null)
		{
			request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _publicToken);
		}

		return await _client.SendAsync(request);
	}

	private static async Task<JsonElement> ReadJson(HttpResponseMessage response)
		=> JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

	private static StartupReadiness CompletedStartupReadiness()
	{
		var readiness = new StartupReadiness();
		readiness.MarkCachesReady();
		readiness.MarkVariablesReady();

		return readiness;
	}

	private sealed class LoopbackStartupFilter : IStartupFilter
	{
		public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
			=> app =>
			{
				app.Use(async (context, nextMiddleware) =>
				{
					if (context.Request.Headers.ContainsKey(LoopbackHeader))
					{
						context.Connection.LocalPort = TestListenerPorts.Loopback;
						context.Connection.RemoteIpAddress = IPAddress.Loopback;
					}
					else
					{
						context.Connection.LocalPort = HostEndpoints.PublicPort;
						context.Connection.RemoteIpAddress = IPAddress.Parse("192.0.2.10");
					}

					await nextMiddleware();
				});
				next(app);
			};
	}
}
