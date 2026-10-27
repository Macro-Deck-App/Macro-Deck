using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MacroDeckHost.Application.Backups;
using MacroDeckHost.Application.Configuration;
using MacroDeckHost.Application.Notifications;
using MacroDeckHost.Application.Paths;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Infrastructure.Backups;
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
	private RecordingProgressReporter _progress = null!;

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
					services.RemoveAll<IBackupProgressReporter>();
					services.AddSingleton<RecordingProgressReporter>();
					services.AddSingleton<IBackupProgressReporter>(provider
						=> provider.GetRequiredService<RecordingProgressReporter>());
				});
			})
			.StartAsync();

		_client = _host.GetTestClient();
		_progress = _host.Services.GetRequiredService<RecordingProgressReporter>();

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
			Directory.Delete(_dataDir, true);
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
			Assert.That(download.Content.Headers.ContentDisposition!.FileNameStar
				?? download.Content.Headers.ContentDisposition!.FileName,
				Does.Contain(".macroDeckBackup"));
			Assert.That(archive, Is.Not.Empty);
			Assert.That(deleted.GetProperty("success").GetBoolean(), Is.True);
			Assert.That(Ids(afterDelete), Does.Not.Contain(backupId));
		});
	}

	[Test]
	public async Task Creating_a_backup_reports_real_progress_that_only_moves_forward()
	{
		var created = await CreateBackup("progress");
		var backupId = created.GetProperty("backup").GetProperty("id").GetGuid();

		var reports = _progress.Reports.Where(status => status.BackupId == backupId).ToList();
		var percents = reports.Select(status => status.PercentComplete).OfType<int>().ToList();
		var saving = reports.Where(status => status.Stage == BackupOperationStage.Saving).ToList();
		var validating = reports.Where(status => status.Stage == BackupOperationStage.Validating).ToList();
		var completed = reports.Last();

		Assert.Multiple(() =>
		{
			Assert.That(percents, Is.Ordered, "progress never goes backwards");
			Assert.That(percents.Take(percents.Count - 1), Is.All.LessThan(100), "100 % only once the backup is done");
			Assert.That(completed.Stage, Is.EqualTo(BackupOperationStage.Completed));
			Assert.That(completed.PercentComplete, Is.EqualTo(100));
			Assert.That(saving.Max(status => status.BytesProcessed), Is.EqualTo(saving[0].TotalBytes));
			Assert.That(saving.Count, Is.GreaterThan(1), "the write pass reports while it runs");
			Assert.That(validating.Select(status => status.PercentComplete), Is.All.Not.Null);
			Assert.That(completed.TotalBytes, Is.EqualTo(saving[0].TotalBytes));
		});
	}

	[Test]
	public async Task Cancelling_a_backup_while_it_is_written_stores_nothing_and_frees_the_way()
	{
		var before = Ids(await ReadJson(await Send(HttpMethod.Get, "/api/backups"))).ToList();
		CancelOnFirst(status
			=> status.Kind == BackupOperationKind.Create && status.Stage == BackupOperationStage.Saving);

		var cancelled = await ReadJson(await Send(HttpMethod.Post, "/api/backups", new { note = "cancel me" }));
		_progress.OnReport = null;
		var after = Ids(await ReadJson(await Send(HttpMethod.Get, "/api/backups"))).ToList();
		var status = await ReadJson(await Send(HttpMethod.Get, "/api/backups/status"));
		var next = await ReadJson(await Send(HttpMethod.Post, "/api/backups", new { note = "after cancel" }));

		Assert.Multiple(() =>
		{
			Assert.That(cancelled.GetProperty("success").GetBoolean(), Is.False);
			Assert.That(cancelled.GetProperty("error").GetProperty("code").GetString(), Is.EqualTo("Cancelled"));
			Assert.That(after, Is.EquivalentTo(before), "a cancelled backup leaves nothing behind");
			Assert.That(status.GetProperty("stage").GetString(), Is.EqualTo("Failed"));
			Assert.That(status.GetProperty("error").GetString(), Is.EqualTo("Cancelled"));
			Assert.That(next.GetProperty("success").GetBoolean(), Is.True, "the next backup is not blocked");
		});
	}

	[Test]
	public async Task A_finished_backup_is_reported_in_the_notification_center()
	{
		var store = _host.Services.GetRequiredService<IUserNotificationStore>();
		Guid? operationId = null;
		string? entryId = null;

		void Watch()
		{
			if (operationId is { } id && entryId is null)
			{
				entryId = store.Snapshot().FirstOrDefault(entry => entry.CancelKey == $"backup:{id:D}")?.Id;
			}
		}

		store.Changed += Watch;
		_progress.OnReport = status =>
		{
			if (status.Kind == BackupOperationKind.Create && status.OperationId is { } id)
			{
				operationId ??= id;
			}
		};

		await CreateBackup("notified");
		_progress.OnReport = null;

		Assert.That(() => entryId, Is.Not.Null.After(3000, 50), "the running backup was listed");
		Assert.That(() => store.Snapshot()
				.Any(entry => entry.Id == entryId
					&& entry.Severity == UserNotificationSeverity.Success
					&& entry.Progress is null
					&& entry.CancelKey is null),
			Is.True.After(3000, 50),
			"the same entry ends as completed");
		store.Changed -= Watch;
	}

	[Test]
	public async Task A_cancelled_backup_leaves_no_entry_in_the_notification_center()
	{
		var store = _host.Services.GetRequiredService<IUserNotificationStore>();
		Guid? operationId = null;
		string? entryId = null;

		void Watch()
		{
			if (operationId is { } id && entryId is null)
			{
				entryId = store.Snapshot()
					.FirstOrDefault(entry => entry.CancelKey == $"backup:{id:D}" && entry.Progress is not null)
					?.Id;
			}
		}

		store.Changed += Watch;
		var cancellation = _host.Services.GetRequiredService<IBackupOperationCancellation>();
		_progress.OnReport = status =>
		{
			if (status.Kind != BackupOperationKind.Create || status.OperationId is not { } id)
			{
				return;
			}

			operationId = id;
			if (status.Stage == BackupOperationStage.Validating)
			{
				cancellation.Cancel(id);
			}
		};

		await ReadJson(await Send(HttpMethod.Post, "/api/backups", new { note = "cancel me" }));
		_progress.OnReport = null;

		Assert.That(() => entryId, Is.Not.Null.After(3000, 50), "a running backup is listed with its cancel key");
		Assert.That(() => store.Snapshot().Any(entry => entry.Id == entryId),
			Is.False.After(3000, 50),
			"the cancelled backup's entry is removed");
		store.Changed -= Watch;
	}

	[Test]
	public async Task Cancelling_an_unknown_operation_changes_nothing()
	{
		var response = await ReadJson(await Send(HttpMethod.Post, $"/api/backups/operations/{Guid.NewGuid()}/cancel"));

		Assert.That(response.GetProperty("success").GetBoolean(), Is.False);
	}

	[Test]
	public async Task Cancelling_a_restore_while_it_checks_the_backup_stages_nothing()
	{
		var backupId = await CreateBackupId("restore source");
		CancelOnFirst(status
			=> status.Kind == BackupOperationKind.Restore && status.Stage == BackupOperationStage.Validating);

		var prepare = await ReadJson(await Send(HttpMethod.Post,
			"/api/backups/restore/prepare",
			new { backupId, components = new[] { "Profiles" } }));
		_progress.OnReport = null;
		var listed = await ReadJson(await Send(HttpMethod.Get, "/api/backups"));

		Assert.Multiple(() =>
		{
			Assert.That(prepare.GetProperty("success").GetBoolean(), Is.False);
			Assert.That(prepare.GetProperty("error").GetProperty("code").GetString(), Is.EqualTo("Cancelled"));
			Assert.That(listed.TryGetProperty("pendingRestore", out var pending)
				&& pending.ValueKind != JsonValueKind.Null,
				Is.False);
		});
	}

	[Test]
	public async Task Cancelling_the_safety_backup_cancels_the_restore_even_when_it_may_go_on_without_one()
	{
		var backupId = await CreateBackupId("restore source");
		CancelOnFirst(status => status.Kind == BackupOperationKind.Create
			&& status.Trigger == BackupTrigger.BeforeRestore
			&& status.Stage == BackupOperationStage.Saving);

		var prepare = await ReadJson(await Send(HttpMethod.Post,
			"/api/backups/restore/prepare",
			new { backupId, components = new[] { "Profiles" }, proceedWithoutSafetyBackup = true }));
		_progress.OnReport = null;
		var listed = await ReadJson(await Send(HttpMethod.Get, "/api/backups"));

		Assert.Multiple(() =>
		{
			Assert.That(prepare.GetProperty("success").GetBoolean(), Is.False);
			Assert.That(prepare.GetProperty("error").GetProperty("code").GetString(), Is.EqualTo("Cancelled"));
			Assert.That(listed.TryGetProperty("pendingRestore", out var pending)
				&& pending.ValueKind != JsonValueKind.Null,
				Is.False);
		});
	}

	[Test]
	public async Task A_cancel_after_the_safety_backup_was_stored_leaves_that_backup_completed()
	{
		var backupId = await CreateBackupId("restore source");
		CancelRestoreWhenSafetyBackupCompletes();

		var prepare = await ReadJson(await Send(HttpMethod.Post,
			"/api/backups/restore/prepare",
			new { backupId, components = new[] { "Profiles" } }));
		_progress.OnReport = null;
		var safetyBackupReports = _progress.Reports
			.Where(status => status.Kind == BackupOperationKind.Create && status.Trigger == BackupTrigger.BeforeRestore)
			.ToList();

		Assert.Multiple(() =>
		{
			Assert.That(safetyBackupReports[^1].Stage, Is.EqualTo(BackupOperationStage.Completed));
			Assert.That(prepare.GetProperty("safetyBackupCreated").GetBoolean(), Is.True);
		});

		if (prepare.GetProperty("success").GetBoolean())
		{
			await Send(HttpMethod.Post,
				"/api/backups/restore/cancel",
				new { restoreId = prepare.GetProperty("restoreId").GetGuid() });
		}
	}

	[Test]
	public async Task A_restore_still_being_rolled_back_can_be_neither_cancelled_nor_replaced()
	{
		var backupId = await CreateBackupId("restore source");
		var paths = _host.Services.GetRequiredService<IMacroDeckPaths>();
		var restoreId = Guid.NewGuid();
		var staging = Path.Combine(paths.RestoreStagingDirectory, restoreId.ToString("N"));
		var original = Path.Combine(staging, "rollback", "data", "profiles", "kitchen.json");
		Directory.CreateDirectory(Path.GetDirectoryName(original)!);
		File.WriteAllText(original, "original");
		File.WriteAllText(Path.Combine(staging, "journal.json"), "{}");
		var marker = Path.Combine(paths.RestoreStagingDirectory, PendingRestoreDocument.FileName);
		File.WriteAllText(marker,
			JsonSerializer.Serialize(new PendingRestoreDocument
				{
					RestoreId = restoreId,
					BackupId = Guid.NewGuid(),
					Components = [BackupComponentGroup.Profiles],
					StagingDirectory = staging,
					ApplyDirectory = Path.Combine(staging, "apply")
				},
				PersistenceJsonOptions.Default));
		var markerBefore = File.ReadAllText(marker);

		try
		{
			var cancel = await ReadJson(await Send(HttpMethod.Post, "/api/backups/restore/cancel", new { restoreId }));
			var prepare = await ReadJson(await Send(HttpMethod.Post,
				"/api/backups/restore/prepare",
				new { backupId, components = new[] { "Profiles" } }));

			Assert.Multiple(() =>
			{
				Assert.That(cancel.GetProperty("success").GetBoolean(), Is.False);
				Assert.That(cancel.GetProperty("error").GetProperty("code").GetString(), Is.EqualTo("RestorePending"));
				Assert.That(prepare.GetProperty("success").GetBoolean(), Is.False);
				Assert.That(prepare.GetProperty("error").GetProperty("code").GetString(), Is.EqualTo("RestorePending"));
				Assert.That(File.ReadAllText(original), Is.EqualTo("original"));
				Assert.That(File.ReadAllText(marker), Is.EqualTo(markerBefore));
			});
		}
		finally
		{
			File.Delete(marker);
			Directory.Delete(staging, true);
		}
	}

	private void CancelRestoreWhenSafetyBackupCompletes()
	{
		var cancellation = _host.Services.GetRequiredService<IBackupOperationCancellation>();
		Guid? restoreOperation = null;
		_progress.OnReport = status =>
		{
			if (status.Kind == BackupOperationKind.Restore && status.OperationId is { } id)
			{
				restoreOperation = id;
			}

			if (status.Kind == BackupOperationKind.Create
				&& status.Trigger == BackupTrigger.BeforeRestore
				&& status.Stage == BackupOperationStage.Completed
				&& restoreOperation is { } restore)
			{
				cancellation.Cancel(restore);
			}
		};
	}

	private void CancelOnFirst(Func<BackupOperationStatus, bool> when)
	{
		var cancellation = _host.Services.GetRequiredService<IBackupOperationCancellation>();
		var done = 0;
		_progress.OnReport = status =>
		{
			if (status.OperationId is { } operationId && when(status) && Interlocked.Exchange(ref done, 1) == 0)
			{
				cancellation.Cancel(operationId);
			}
		};
	}

	private async Task<string?> CreateBackupId(string note)
	{
		var created = await CreateBackup(note);
		return created.GetProperty("backup").GetProperty("id").GetString();
	}

	private async Task<JsonElement> CreateBackup(string note)
	{
		var response = await Send(HttpMethod.Post, "/api/backups", new { note });
		var raw = await response.Content.ReadAsStringAsync();
		Assert.That(raw, Does.Contain("\"success\":true"), $"{(int)response.StatusCode} {raw}");
		return JsonDocument.Parse(raw).RootElement;
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
			Assert.That(onlyConfirmed.TryGetProperty("key", out var withoutAck)
				&& withoutAck.ValueKind
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
		var created = await CreateBackup("wire format");
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
	{
		return listed.GetProperty("backups").EnumerateArray().Select(b => b.GetProperty("id").GetString());
	}

	private async Task<string> ReadRaw(HttpMethod method, string path)
	{
		return await (await Send(method, path)).Content.ReadAsStringAsync();
	}

	private Task<HttpResponseMessage> Send(HttpMethod method, string path, object? body = null)
	{
		return Dispatch(method, path, body, true);
	}

	private Task<HttpResponseMessage> SendPublic(HttpMethod method, string path, object? body = null)
	{
		return Dispatch(method, path, body, false);
	}

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
	{
		return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
	}

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
		{
			return app =>
			{
				app.Use(async (context, nextMiddleware) =>
				{
					if (context.Request.Headers.ContainsKey(LoopbackHeader))
					{
						context.Connection.LocalPort = TestListenerPorts.Loopback;
						context.Request.Headers[LoopbackSecret.HeaderName] = TestListenerPorts.LoopbackSecret;
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

	private sealed class RecordingProgressReporter : IBackupProgressReporter
	{
		private readonly BackupProgressReporter _inner;
		private readonly ConcurrentQueue<BackupOperationStatus> _reports = new();

		public RecordingProgressReporter(IServiceScopeFactory scopeFactory)
		{
			_inner = new BackupProgressReporter(scopeFactory, TimeProvider.System);
		}

		public IReadOnlyList<BackupOperationStatus> Reports => [.. _reports];

		public BackupOperationStatus Current => _inner.Current;

		public Action<BackupOperationStatus>? OnReport { get; set; }

		public void Report(BackupOperationStatus status)
		{
			_reports.Enqueue(status);
			OnReport?.Invoke(status);
			_inner.Report(status);
		}
	}
}
