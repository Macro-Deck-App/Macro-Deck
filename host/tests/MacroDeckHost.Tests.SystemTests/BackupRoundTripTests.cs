using System.Text.Json.Nodes;
using MacroDeckHost.Tests.SystemTests.Support;

namespace MacroDeckHost.Tests.SystemTests;

[TestFixture]
internal sealed class BackupRoundTripTests : SystemTestFixture
{
	private const string VariableName = "round_trip_variable";
	private const string SecretValue = "round-trip-secret";

	[Test]
	public async Task A_backup_from_another_installation_restores_its_data_secrets_and_recovery_key()
	{
		var source = await StartHostAsync(root: Path.Combine(State.Path, "source"));
		var profileId = await CreateProfileAsync(source, "Round Trip");
		await CreateVariableAsync(source, "kept across installations");
		var secret = await PostJsonAsync(source, "/api/secrets", new { value = SecretValue, kind = "Password" });
		var secretId = (string)secret["id"]!;
		var sourceKey = await RecoveryKeyAsync(source);
		var backupId = await CreateBackupAsync(source);
		var archive = await DownloadAsync(source, backupId);

		var target = await StartHostAsync(root: Path.Combine(State.Path, "target"));
		var imported = await PostJsonAsync(target, "/api/backups/import-path", new { path = archive });
		Assert.That((bool?)imported["success"], Is.True, imported.ToJsonString());
		var importedId = (string)imported["backup"]!["id"]!;
		await RestoreAsync(target, await PrepareAsync(target, importedId, sourceKey));
		await UnlockIfLockedAsync(target, sourceKey);

		var revealed = await PostJsonAsync(target, $"/api/secrets/{secretId}/reveal", new { });
		var profiles = await target.GetJsonAsync("/api/profiles");
		await Assert.MultipleAsync(async () =>
		{
			Assert.That(profiles["profiles"]!.AsArray().Any(profile => (string?)profile!["id"] == profileId), Is.True);
			Assert.That((string?)(await target.VariableAsync(VariableName))?["value"],
				Is.EqualTo("kept across installations"));
			Assert.That((string?)revealed["value"], Is.EqualTo(SecretValue), revealed.ToJsonString());
			Assert.That(await RecoveryKeyAsync(target), Is.EqualTo(sourceKey), "the recovery key came along");
		});
	}

	[Test]
	public async Task A_second_prepare_replaces_the_first_so_only_the_second_can_be_committed()
	{
		var host = await StartHostAsync();
		var keyId = await CreateProfileAsync(host, "Kept");
		var firstProfileId = await CreateProfileAsync(host, "Only In The First Backup");
		var firstBackupId = await CreateBackupAsync(host);
		await DeleteAsync(host, $"/api/profiles/{firstProfileId}");
		var secondProfileId = await CreateProfileAsync(host, "Only In The Second Backup");
		var secondBackupId = await CreateBackupAsync(host);
		await DeleteAsync(host, $"/api/profiles/{secondProfileId}");

		var first = await PrepareAsync(host, firstBackupId, null);
		var second = await PrepareAsync(host, secondBackupId, null);
		var commitFirst = await PostJsonAsync(host, "/api/backups/restore/commit", new { restoreId = first });
		await RestoreAsync(host, second);

		var profileIds = (await host.GetJsonAsync("/api/profiles"))["profiles"]!.AsArray()
			.Select(profile => (string?)profile!["id"])
			.ToList();
		Assert.Multiple(() =>
		{
			Assert.That((bool?)commitFirst["success"], Is.False, commitFirst.ToJsonString());
			Assert.That(profileIds, Does.Contain(secondProfileId), "the second backup was applied");
			Assert.That(profileIds, Does.Not.Contain(firstProfileId), "the first backup was not");
			Assert.That(profileIds, Does.Contain(keyId));
		});
	}

	private static async Task<string> PrepareAsync(HostInstance host, string backupId, string? recoveryKey)
	{
		var catalog = await host.GetJsonAsync("/api/backups");
		var components = catalog["backups"]!.AsArray()
			.Single(backup => (string?)backup!["id"] == backupId)!["components"]!.AsArray()
			.Select(component => (string?)component)
			.ToList();
		var prepared = await PostJsonAsync(host,
			"/api/backups/restore/prepare",
			new { backupId, components, recoveryKey });
		Assert.That((bool?)prepared["success"], Is.True, prepared.ToJsonString());
		return (string)prepared["restoreId"]!;
	}

	private static async Task RestoreAsync(HostInstance host, string restoreId)
	{
		var startsBefore = host.Starts;
		var committed = await PostJsonAsync(host, "/api/backups/restore/commit", new { restoreId });
		Assert.That((bool?)committed["success"], Is.True, committed.ToJsonString());
		Assert.That((bool?)committed["restartRequested"], Is.True, committed.ToJsonString());
		await host.WaitForRestartAsync(startsBefore);
	}

	private static async Task UnlockIfLockedAsync(HostInstance host, string recoveryKey)
	{
		var status = await host.GetJsonAsync("/api/key-ring/status");
		if ((bool?)status["locked"] != true)
		{
			return;
		}

		var unlocked = await PostJsonAsync(host, "/api/key-ring/unlock", new { recoveryKey });
		Assert.That((bool?)unlocked["success"], Is.True, unlocked.ToJsonString());
		if ((bool?)unlocked["restartRequested"] == true)
		{
			await host.RestartAsync();
		}
	}

	private static async Task<string> CreateProfileAsync(HostInstance host, string name)
	{
		var created = await PostJsonAsync(host, "/api/profiles", new { name, defaultColumns = 3, defaultRows = 2 });
		return (string)created["profile"]!["id"]!;
	}

	private static async Task CreateVariableAsync(HostInstance host, string value)
	{
		var created = await PostJsonAsync(host,
			"/api/variables",
			new { name = VariableName, type = "text", scope = "global", initialValue = value });
		Assert.That(created["variable"], Is.Not.Null, created.ToJsonString());
	}

	private static async Task<string> RecoveryKeyAsync(HostInstance host)
	{
		var revealed = await PostJsonAsync(host, "/api/backups/recovery-key/reveal", new { confirm = true });
		if ((string?)revealed["key"] is { Length: > 0 } key)
		{
			return key;
		}

		var created = await PostJsonAsync(host, "/api/backups/recovery-key", new { });
		return (string?)created["key"] ?? throw new InvalidOperationException(created.ToJsonString());
	}

	private static async Task<string> CreateBackupAsync(HostInstance host)
	{
		var created = await PostJsonAsync(host, "/api/backups", new { note = "system test" });
		Assert.That((bool?)created["success"], Is.True, created.ToJsonString());
		await Poll.UntilAsync(async () => (string?)(await host.GetJsonAsync("/api/backups/status"))["stage"]
				is "Idle" or "Completed",
			TimeSpan.FromSeconds(60),
			"the backup finished");
		return (string)created["backup"]!["id"]!;
	}

	private async Task<string> DownloadAsync(HostInstance host, string backupId)
	{
		Directory.CreateDirectory(ArtifactsDirectory);
		var path = Path.Combine(ArtifactsDirectory, $"{backupId}.macroDeckBackup");
		await using var stream = await host.Api.GetStreamAsync($"/api/backups/{backupId}/download");
		await using var file = File.Create(path);
		await stream.CopyToAsync(file);
		return path;
	}

	private static async Task DeleteAsync(HostInstance host, string path)
	{
		using var response = await host.Api.DeleteAsync(path);
		response.EnsureSuccessStatusCode();
	}

	private static async Task<JsonNode> PostJsonAsync(HostInstance host, string path, object body)
	{
		using var response = await host.PostAsync(path, body);
		var text = await response.Content.ReadAsStringAsync();
		Assert.That(response.IsSuccessStatusCode, Is.True, $"{path}: {(int)response.StatusCode} {text}");
		return JsonNode.Parse(text)!;
	}
}
