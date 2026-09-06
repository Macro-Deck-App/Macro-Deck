using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MacroDeckHost.Application.Paths;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Infrastructure.Persistence;
using MacroDeckHost.Tests.UnitTests.Migration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Api;

[NonParallelizable]
public class PortabilityEndpointsTests
{
	private const string LoopbackHeader = "X-Test-Loopback";

	private IHost _host = null!;
	private HttpClient _client = null!;
	private string _dataDir = null!;
	private string? _previousDataDir;

	[OneTimeSetUp]
	public async Task OneTimeSetUp()
	{
		_dataDir = Path.Combine(Path.GetTempPath(), "macro-deck-tests", Guid.NewGuid().ToString("N"));
		_previousDataDir = Environment.GetEnvironmentVariable("MACRODECK_DATA_DIR");
		Environment.SetEnvironmentVariable("MACRODECK_DATA_DIR", _dataDir);

		var paths = new MacroDeckPaths();
		DatabaseMigrationHelper.MigrateDatabase(paths);

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

		var setup = await SendJson(HttpMethod.Post,
			"/api/auth/setup",
			new { username = "admin", password = "password123" });
		Assert.That(setup.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
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
	public async Task Profile_export_then_import_creates_a_new_profile()
	{
		var profileId = await CreateProfile("Round Trip");

		var export = await SendJson(HttpMethod.Post,
			$"/api/profiles/{profileId}/export",
			new { includeSecrets = false });
		Assert.That(export.StatusCode, Is.EqualTo(HttpStatusCode.OK));
		Assert.That(export.Content.Headers.ContentDisposition!.FileNameStar ??
			export.Content.Headers.ContentDisposition!.FileName,
			Does.Contain(".macroDeckProfile"));
		var archive = await export.Content.ReadAsByteArrayAsync();

		var import = await ImportProfile(archive, password: null);
		var body = await ReadJson(import);

		Assert.Multiple(() =>
		{
			Assert.That(import.StatusCode, Is.EqualTo(HttpStatusCode.OK));
			Assert.That(body.GetProperty("success").GetBoolean(), Is.True);
			Assert.That(body.GetProperty("profile").GetProperty("id").GetString(), Is.Not.EqualTo(profileId));
			Assert.That(body.GetProperty("profile").GetProperty("name").GetString(), Is.EqualTo("Round Trip"));
		});
	}

	[Test]
	public async Task Profile_export_including_secrets_requires_a_password()
	{
		var profileId = await CreateProfile("Needs Password");

		var export = await SendJson(HttpMethod.Post,
			$"/api/profiles/{profileId}/export",
			new { includeSecrets = true });

		Assert.That(export.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
	}

	[Test]
	public async Task Encrypted_profile_import_enforces_the_password()
	{
		var profileId = await CreateProfile("Encrypted");
		var export = await SendJson(HttpMethod.Post,
			$"/api/profiles/{profileId}/export",
			new { includeSecrets = true, password = "s3cret-export" });
		var archive = await export.Content.ReadAsByteArrayAsync();

		var withoutPassword = await ReadJson(await ImportProfile(archive, password: null));
		var wrongPassword = await ReadJson(await ImportProfile(archive, password: "nope"));
		var correctPassword = await ReadJson(await ImportProfile(archive, password: "s3cret-export"));

		Assert.Multiple(() =>
		{
			Assert.That(export.StatusCode, Is.EqualTo(HttpStatusCode.OK));
			Assert.That(withoutPassword.GetProperty("success").GetBoolean(), Is.False);
			Assert.That(withoutPassword.GetProperty("error").GetProperty("code").GetString(),
				Is.EqualTo("PasswordRequired"));
			Assert.That(wrongPassword.GetProperty("error").GetProperty("code").GetString(),
				Is.EqualTo("InvalidPassword"));
			Assert.That(correctPassword.GetProperty("success").GetBoolean(), Is.True);
		});
	}

	[Test]
	public async Task Encrypted_profile_export_rejects_a_weak_password()
	{
		var profileId = await CreateProfile("Weak");

		var export = await SendJson(HttpMethod.Post,
			$"/api/profiles/{profileId}/export",
			new { includeSecrets = true, password = "short" });
		var body = await ReadJson(export);

		Assert.Multiple(() =>
		{
			Assert.That(export.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
			Assert.That(body.GetProperty("code").GetString(), Is.EqualTo("WeakPassword"));
		});
	}

	[Test]
	public async Task Widget_export_downloads_a_macroDeckWidget_file()
	{
		var profileId = await CreateProfile("Widget Export");
		var folderId = await FirstFolderId(profileId);
		var widgetId = await CreateWidget(folderId);

		var export = await SendJson(HttpMethod.Post,
			"/api/widgets/export",
			new { folderId, widgetIds = new[] { widgetId }, includeSecrets = false });

		Assert.Multiple(() =>
		{
			Assert.That(export.StatusCode, Is.EqualTo(HttpStatusCode.OK));
			Assert.That(export.Content.Headers.ContentDisposition!.FileNameStar ??
				export.Content.Headers.ContentDisposition!.FileName,
				Does.Contain(".macroDeckWidget"));
		});
	}

	[Test]
	public async Task Widget_export_then_import_carries_its_widget_scoped_variable()
	{
		var profileId = await CreateProfile("Widget Variable Export");
		var folderId = await FirstFolderId(profileId);
		var widgetId = await CreateWidget(folderId);

		var createVariable = await SendJson(HttpMethod.Post,
			"/api/variables",
			new { name = "mine", scope = "widget", scopeRefId = widgetId, type = "text", initialValue = "hello" });
		Assert.That(createVariable.StatusCode, Is.EqualTo(HttpStatusCode.OK), "create widget variable");

		var export = await SendJson(HttpMethod.Post,
			"/api/widgets/export",
			new { folderId, widgetIds = new[] { widgetId }, includeSecrets = false });
		Assert.That(export.StatusCode, Is.EqualTo(HttpStatusCode.OK));
		var path = WriteArchive(await export.Content.ReadAsByteArrayAsync(), "widgets.macroDeckWidget");

		var import = await ReadJson(await SendJson(HttpMethod.Post,
			"/api/widgets/import-path",
			new { folderId, anchorX = 1, anchorY = 1, path }));
		Assert.That(import.GetProperty("success").GetBoolean(), Is.True);
		var newWidgetId = import.GetProperty("widgets")[0].GetProperty("id").GetString();

		using var variablesRequest = new HttpRequestMessage(HttpMethod.Get, "/api/variables");
		variablesRequest.Headers.Add(LoopbackHeader, "1");
		var variables = await ReadJson(await _client.SendAsync(variablesRequest));
		var imported = variables.GetProperty("variables")
			.EnumerateArray()
			.Single(v => v.GetProperty("scopeRefId").GetString() == newWidgetId);

		Assert.Multiple(() =>
		{
			Assert.That(imported.GetProperty("name").GetString(), Is.EqualTo("mine"));
			Assert.That(imported.GetProperty("value").GetString(), Is.EqualTo("hello"));
			Assert.That(imported.GetProperty("classification").GetString(), Is.EqualTo("user"));
		});
	}

	[Test]
	public async Task Folder_export_then_import_creates_a_folder_under_the_target_parent()
	{
		var profileId = await CreateProfile("Folder Export");
		var rootId = await FirstFolderId(profileId);
		var childId = await CreateFolder(profileId, "Lights", rootId);
		await CreateWidget(childId);

		var export = await SendJson(HttpMethod.Post,
			$"/api/folders/{childId}/export",
			new { includeSubfolders = true, includeSecrets = false });
		Assert.That(export.StatusCode, Is.EqualTo(HttpStatusCode.OK));
		Assert.That(export.Content.Headers.ContentDisposition!.FileNameStar ??
			export.Content.Headers.ContentDisposition!.FileName,
			Does.Contain(".macroDeckFolder"));
		var archive = await export.Content.ReadAsByteArrayAsync();

		var import = await ImportFolder(archive, profileId, rootId, password: null);
		var body = await ReadJson(import);

		Assert.Multiple(() =>
		{
			Assert.That(import.StatusCode, Is.EqualTo(HttpStatusCode.OK));
			Assert.That(body.GetProperty("success").GetBoolean(), Is.True);
			Assert.That(body.GetProperty("folder").GetProperty("id").GetString(), Is.Not.EqualTo(childId));
			Assert.That(body.GetProperty("folder").GetProperty("name").GetString(), Is.EqualTo("Lights"));
			Assert.That(body.GetProperty("folder").GetProperty("parentId").GetString(), Is.EqualTo(rootId));
			Assert.That(body.GetProperty("folderCount").GetInt32(), Is.EqualTo(1));
		});
	}

	[Test]
	public async Task Folder_import_without_a_parent_lands_at_the_profile_root()
	{
		var profileId = await CreateProfile("Folder Root Import");
		var rootId = await FirstFolderId(profileId);

		var export = await SendJson(HttpMethod.Post,
			$"/api/folders/{rootId}/export",
			new { includeSubfolders = false, includeSecrets = false });
		var archive = await export.Content.ReadAsByteArrayAsync();

		var body = await ReadJson(await ImportFolder(archive, profileId, parentFolderId: null, password: null));

		Assert.Multiple(() =>
		{
			Assert.That(body.GetProperty("success").GetBoolean(), Is.True);
			Assert.That(body.GetProperty("folder").GetProperty("parentId").GetString(), Is.Null.Or.Empty);
		});
	}

	[Test]
	public async Task Encrypted_folder_import_enforces_the_password()
	{
		var profileId = await CreateProfile("Encrypted Folder");
		var rootId = await FirstFolderId(profileId);

		var export = await SendJson(HttpMethod.Post,
			$"/api/folders/{rootId}/export",
			new { includeSubfolders = false, includeSecrets = true, password = "s3cret-export" });
		var archive = await export.Content.ReadAsByteArrayAsync();

		var withoutPassword = await ReadJson(await ImportFolder(archive, profileId, null, password: null));
		var wrongPassword = await ReadJson(await ImportFolder(archive, profileId, null, password: "nope"));
		var correctPassword = await ReadJson(await ImportFolder(archive, profileId, null, "s3cret-export"));

		Assert.Multiple(() =>
		{
			Assert.That(export.StatusCode, Is.EqualTo(HttpStatusCode.OK));
			Assert.That(withoutPassword.GetProperty("error").GetProperty("code").GetString(),
				Is.EqualTo("PasswordRequired"));
			Assert.That(wrongPassword.GetProperty("error").GetProperty("code").GetString(),
				Is.EqualTo("InvalidPassword"));
			Assert.That(correctPassword.GetProperty("success").GetBoolean(), Is.True);
		});
	}

	[Test]
	public async Task Folder_import_rejects_a_widget_archive()
	{
		var profileId = await CreateProfile("Wrong Kind");
		var folderId = await FirstFolderId(profileId);
		var widgetId = await CreateWidget(folderId);
		var export = await SendJson(HttpMethod.Post,
			"/api/widgets/export",
			new { folderId, widgetIds = new[] { widgetId }, includeSecrets = false });
		var archive = await export.Content.ReadAsByteArrayAsync();

		var body = await ReadJson(await ImportFolder(archive, profileId, folderId, password: null));

		Assert.Multiple(() =>
		{
			Assert.That(body.GetProperty("success").GetBoolean(), Is.False);
			Assert.That(body.GetProperty("error").GetProperty("code").GetString(), Is.EqualTo("InvalidArchive"));
		});
	}

	[Test]
	public async Task Folder_export_of_an_unknown_folder_is_not_found()
	{
		var export = await SendJson(HttpMethod.Post,
			$"/api/folders/{Guid.NewGuid()}/export",
			new { includeSubfolders = false, includeSecrets = false });

		Assert.That(export.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
	}

	[Test]
	public async Task Import_rejects_a_non_archive_file()
	{
		var import = await ImportProfile([1, 2, 3, 4], password: null);
		var body = await ReadJson(import);

		Assert.Multiple(() =>
		{
			Assert.That(import.StatusCode, Is.EqualTo(HttpStatusCode.OK));
			Assert.That(body.GetProperty("success").GetBoolean(), Is.False);
			Assert.That(body.GetProperty("error").GetProperty("code").GetString(), Is.EqualTo("InvalidArchive"));
		});
	}

	[Test]
	public async Task Profile_import_from_path_creates_a_new_profile()
	{
		var profileId = await CreateProfile("Path Round Trip");
		var archive = await ExportProfile(profileId, password: null);
		var path = WriteArchive(archive, "profile.macroDeckProfile");

		var body = await ReadJson(await SendJson(HttpMethod.Post, "/api/profiles/import-path", new { path }));

		Assert.Multiple(() =>
		{
			Assert.That(body.GetProperty("success").GetBoolean(), Is.True);
			Assert.That(body.GetProperty("profile").GetProperty("id").GetString(), Is.Not.EqualTo(profileId));
			Assert.That(body.GetProperty("profile").GetProperty("name").GetString(), Is.EqualTo("Path Round Trip"));
		});
	}

	[Test]
	public async Task Encrypted_profile_import_from_path_enforces_the_password()
	{
		var profileId = await CreateProfile("Path Encrypted");
		var archive = await ExportProfile(profileId, "s3cret-export");
		var path = WriteArchive(archive, "encrypted.macroDeckProfile");

		var withoutPassword = await ReadJson(
			await SendJson(HttpMethod.Post, "/api/profiles/import-path", new { path }));
		var wrongPassword = await ReadJson(await SendJson(HttpMethod.Post,
			"/api/profiles/import-path",
			new { path, password = "nope" }));
		var correctPassword = await ReadJson(await SendJson(HttpMethod.Post,
			"/api/profiles/import-path",
			new { path, password = "s3cret-export" }));

		Assert.Multiple(() =>
		{
			Assert.That(withoutPassword.GetProperty("error").GetProperty("code").GetString(),
				Is.EqualTo("PasswordRequired"));
			Assert.That(wrongPassword.GetProperty("error").GetProperty("code").GetString(),
				Is.EqualTo("InvalidPassword"));
			Assert.That(correctPassword.GetProperty("success").GetBoolean(), Is.True);
		});
	}

	[Test]
	public async Task Inspect_from_path_describes_an_encrypted_archive_without_its_password()
	{
		var profileId = await CreateProfile("Path Inspect");
		var archive = await ExportProfile(profileId, "s3cret-export");
		var path = WriteArchive(archive, "inspect.macroDeckProfile");

		var body = await ReadJson(await SendJson(HttpMethod.Post, "/api/portable/inspect-path", new { path }));

		Assert.Multiple(() =>
		{
			Assert.That(body.GetProperty("success").GetBoolean(), Is.True);
			Assert.That(body.GetProperty("archive").GetProperty("kind").GetString(), Is.EqualTo("Profile"));
			Assert.That(body.GetProperty("archive").GetProperty("name").GetString(), Is.EqualTo("Path Inspect"));
			Assert.That(body.GetProperty("archive").GetProperty("encrypted").GetBoolean(), Is.True);
		});
	}

	[Test]
	public async Task Folder_import_from_path_lands_under_the_target_parent()
	{
		var profileId = await CreateProfile("Path Folder");
		var rootId = await FirstFolderId(profileId);
		var childId = await CreateFolder(profileId, "Lights", rootId);

		var export = await SendJson(HttpMethod.Post,
			$"/api/folders/{childId}/export",
			new { includeSubfolders = true, includeSecrets = false });
		var path = WriteArchive(await export.Content.ReadAsByteArrayAsync(), "folder.macroDeckFolder");

		var body = await ReadJson(await SendJson(HttpMethod.Post,
			"/api/folders/import-path",
			new { profileId, parentFolderId = rootId, path }));

		Assert.Multiple(() =>
		{
			Assert.That(body.GetProperty("success").GetBoolean(), Is.True);
			Assert.That(body.GetProperty("folder").GetProperty("parentId").GetString(), Is.EqualTo(rootId));
			Assert.That(body.GetProperty("folder").GetProperty("id").GetString(), Is.Not.EqualTo(childId));
		});
	}

	[Test]
	public async Task Widget_import_from_path_places_the_widget_at_the_anchor()
	{
		var profileId = await CreateProfile("Path Widget");
		var folderId = await FirstFolderId(profileId);
		var widgetId = await CreateWidget(folderId);

		var export = await SendJson(HttpMethod.Post,
			"/api/widgets/export",
			new { folderId, widgetIds = new[] { widgetId }, includeSecrets = false });
		var path = WriteArchive(await export.Content.ReadAsByteArrayAsync(), "widgets.macroDeckWidget");

		var body = await ReadJson(await SendJson(HttpMethod.Post,
			"/api/widgets/import-path",
			new { folderId, anchorX = 1, anchorY = 2, path }));

		Assert.Multiple(() =>
		{
			Assert.That(body.GetProperty("success").GetBoolean(), Is.True);
			Assert.That(body.GetProperty("widgets")[0].GetProperty("positionX").GetInt32(), Is.EqualTo(1));
			Assert.That(body.GetProperty("widgets")[0].GetProperty("positionY").GetInt32(), Is.EqualTo(2));
		});
	}

	[Test]
	public async Task Import_from_path_rejects_a_file_that_does_not_exist()
	{
		var path = Path.Combine(_dataDir, "nothing-here.macroDeckProfile");

		var body = await ReadJson(await SendJson(HttpMethod.Post, "/api/profiles/import-path", new { path }));

		Assert.That(body.GetProperty("error").GetProperty("code").GetString(), Is.EqualTo("NotFound"));
	}

	[Test]
	public async Task Import_from_path_rejects_an_archive_of_the_wrong_kind()
	{
		var profileId = await CreateProfile("Wrong Kind");
		var archive = await ExportProfile(profileId, password: null);
		var path = WriteArchive(archive, "misnamed.macroDeckFolder");

		var body = await ReadJson(await SendJson(HttpMethod.Post, "/api/profiles/import-path", new { path }));

		Assert.That(body.GetProperty("error").GetProperty("code").GetString(), Is.EqualTo("ValidationError"));
	}

	[Test]
	public async Task Import_from_path_rejects_a_relative_path()
	{
		var body = await ReadJson(await SendJson(HttpMethod.Post,
			"/api/profiles/import-path",
			new { path = "relative.macroDeckProfile" }));

		Assert.That(body.GetProperty("error").GetProperty("code").GetString(), Is.EqualTo("ValidationError"));
	}

	[Test]
	public async Task Import_from_path_is_refused_outside_the_desktop_shell()
	{
		var profileId = await CreateProfile("Public Listener");
		var archive = await ExportProfile(profileId, password: null);
		var path = WriteArchive(archive, "public.macroDeckProfile");

		using var request = new HttpRequestMessage(HttpMethod.Post, "/api/profiles/import-path")
		{
			Content = JsonContent.Create(new { path })
		};
		request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await AdminToken());
		var body = await ReadJson(await _client.SendAsync(request));

		Assert.Multiple(() =>
		{
			Assert.That(body.GetProperty("success").GetBoolean(), Is.False);
			Assert.That(body.GetProperty("error").GetProperty("code").GetString(), Is.EqualTo("DesktopOnly"));
		});
	}

	[Test]
	public async Task Icon_import_from_path_is_refused_outside_the_desktop_shell()
	{
		var packId = await CreateIconPack("Public Listener Icons");
		var path = WriteImage("public-icon.png");

		using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/icon-packs/{packId}/import-path")
		{
			Content = JsonContent.Create(new { paths = new[] { path } })
		};
		request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await AdminToken());
		var body = await ReadJson(await _client.SendAsync(request));

		Assert.Multiple(() =>
		{
			Assert.That(body.GetProperty("success").GetBoolean(), Is.False);
			Assert.That(body.GetProperty("error").GetProperty("code").GetString(), Is.EqualTo("DesktopOnly"));
		});
	}

	[Test]
	public async Task Icon_import_from_path_succeeds_over_the_loopback_listener()
	{
		var packId = await CreateIconPack("Loopback Icons");
		var path = WriteImage("loopback-icon.png");

		var body = await ReadJson(await SendJson(HttpMethod.Post,
			$"/api/icon-packs/{packId}/import-path",
			new { paths = new[] { path } }));

		Assert.That(body.GetProperty("success").GetBoolean(), Is.True);
	}

	private async Task<string> CreateIconPack(string name)
	{
		var response = await SendJson(HttpMethod.Post, "/api/icon-packs", new { name });
		Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), "create icon pack");
		var json = await ReadJson(response);
		return json.GetProperty("pack").GetProperty("id").GetString()!;
	}

	private string WriteImage(string fileName)
	{
		var directory = Path.Combine(_dataDir, "opened-files");
		Directory.CreateDirectory(directory);
		var path = Path.Combine(directory, $"{Guid.NewGuid():N}-{fileName}");
		File.WriteAllBytes(path, [137, 80, 78, 71, 13, 10, 26, 10]);
		return path;
	}

	private async Task<byte[]> ExportProfile(string profileId, string? password)
	{
		var export = await SendJson(HttpMethod.Post,
			$"/api/profiles/{profileId}/export",
			password is null
				? new { includeSecrets = false }
				: (object)new { includeSecrets = true, password });
		Assert.That(export.StatusCode, Is.EqualTo(HttpStatusCode.OK), "export profile");
		return await export.Content.ReadAsByteArrayAsync();
	}

	private string WriteArchive(byte[] archive, string fileName)
	{
		var directory = Path.Combine(_dataDir, "opened-files");
		Directory.CreateDirectory(directory);
		var path = Path.Combine(directory, $"{Guid.NewGuid():N}-{fileName}");
		File.WriteAllBytes(path, archive);
		return path;
	}

	// Migration takes a host filesystem path, so it falls under the same rule as the path-based archive
	// and icon imports above: a remote client must not gain an oracle for what exists on this machine.
	[Test]
	public async Task Migration_preview_is_refused_outside_the_desktop_shell()
	{
		using var request = new HttpRequestMessage(HttpMethod.Post, "/api/migration/preview")
		{
			Content = JsonContent.Create(new { sourceId = "macro-deck-2", path = _dataDir })
		};
		request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await AdminToken());
		var body = await ReadJson(await _client.SendAsync(request));

		Assert.Multiple(() =>
		{
			Assert.That(body.GetProperty("success").GetBoolean(), Is.False);
			Assert.That(body.GetProperty("error").GetProperty("code").GetString(), Is.EqualTo("DesktopOnly"));
		});
	}

	[Test]
	public async Task Migration_sources_reports_where_it_looked()
	{
		using var request = new HttpRequestMessage(HttpMethod.Get, "/api/migration/sources");
		request.Headers.Add(LoopbackHeader, "1");
		var body = await ReadJson(await _client.SendAsync(request));

		Assert.That(body.GetProperty("sources")
				.EnumerateArray()
				.Select(source => source.GetProperty("id").GetString()),
			Does.Contain("macro-deck-2"));
	}

	// The whole point of the key handshake: the host says it has no usable key rather than failing, and
	// the same folder previews cleanly once the user supplies one.
	[Test]
	public async Task Migration_preview_asks_for_a_key_it_cannot_read_and_accepts_the_one_it_is_given()
	{
		const string key = "3f5b9c21-7e44-4c8a-9e11-0d2a6b8f4c73";
		using var fixture = new MacroDeck2Fixture();
		fixture.WithProfile("0",
				"Migrated",
				rows: 3,
				columns: 5,
				folders: [MacroDeck2Fixture.Folder("root", "*Root*", [MacroDeck2Fixture.Button(0, 0)])])
			.WithPluginCredentials("vendor_plugin", key, new Dictionary<string, string> { ["token"] = "secret" })
			.Build();

		var withoutKey = await ReadJson(await SendJson(HttpMethod.Post,
			"/api/migration/preview",
			new { sourceId = "macro-deck-2", path = fixture.Root }));

		var withWrongKey = await ReadJson(await SendJson(HttpMethod.Post,
			"/api/migration/preview",
			new { sourceId = "macro-deck-2", path = fixture.Root, decryptionKey = "not-the-key" }));

		var withKey = await ReadJson(await SendJson(HttpMethod.Post,
			"/api/migration/preview",
			new { sourceId = "macro-deck-2", path = fixture.Root, decryptionKey = key }));

		Assert.Multiple(() =>
		{
			Assert.That(withoutKey.GetProperty("success").GetBoolean(),
				Is.True,
				"a key the host cannot read is not a failure");
			Assert.That(withoutKey.GetProperty("summary").GetProperty("credentialStatus").GetString(),
				Is.EqualTo("KeyUnavailable"));
			Assert.That(withoutKey.GetProperty("summary").GetProperty("widgetCount").GetInt32(),
				Is.EqualTo(1),
				"the rest of the profile is still readable");

			Assert.That(withWrongKey.GetProperty("success").GetBoolean(), Is.False);
			Assert.That(withWrongKey.GetProperty("error").GetProperty("code").GetString(),
				Is.EqualTo("InvalidDecryptionKey"));

			Assert.That(withKey.GetProperty("success").GetBoolean(), Is.True);
			Assert.That(withKey.GetProperty("summary").GetProperty("credentialStatus").GetString(),
				Is.EqualTo("Decrypted"));
		});
	}

	[Test]
	public async Task Migration_preview_of_a_folder_that_is_not_a_macro_deck_2_directory_says_so()
	{
		var body = await ReadJson(await SendJson(HttpMethod.Post,
			"/api/migration/preview",
			new { sourceId = "macro-deck-2", path = Path.Combine(_dataDir, "nothing-here") }));

		Assert.Multiple(() =>
		{
			Assert.That(body.GetProperty("success").GetBoolean(), Is.False);
			Assert.That(body.GetProperty("error").GetProperty("code").GetString(), Is.EqualTo("SourceNotFound"));
		});
	}

	// The upload endpoints deliberately are NOT desktop-only: bytes the caller already holds reveal
	// nothing about this machine's filesystem, which is the only thing that restriction protects. This is
	// the route the browser-served configuration UI takes, where there is no native picker.
	[Test]
	public async Task Migration_upload_is_allowed_outside_the_desktop_shell()
	{
		using var fixture = new MacroDeck2Fixture();
		fixture.WithProfile("0",
				"Backed up",
				rows: 3,
				columns: 5,
				folders: [MacroDeck2Fixture.Folder("root", "*Root*", [MacroDeck2Fixture.Button(0, 0)])])
			.Build();

		var backup = fixture.PackAsBackup();
		try
		{
			using var content = new MultipartFormDataContent();
			content.Add(new StringContent("macro-deck-2"), "sourceId");
			content.Add(new ByteArrayContent(await File.ReadAllBytesAsync(backup)), "file", "backup.zip");

			using var request = new HttpRequestMessage(HttpMethod.Post, "/api/migration/preview-upload")
			{
				Content = content
			};
			request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await AdminToken());
			var body = await ReadJson(await _client.SendAsync(request));

			Assert.Multiple(() =>
			{
				Assert.That(body.GetProperty("success").GetBoolean(), Is.True);
				Assert.That(body.GetProperty("summary").GetProperty("widgetCount").GetInt32(), Is.EqualTo(1));
			});
		}
		finally
		{
			File.Delete(backup);
		}
	}

	[Test]
	public async Task Migration_upload_without_a_file_says_one_is_required()
	{
		using var content = new MultipartFormDataContent();
		content.Add(new StringContent("macro-deck-2"), "sourceId");

		using var request = new HttpRequestMessage(HttpMethod.Post, "/api/migration/preview-upload")
		{
			Content = content
		};
		request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await AdminToken());
		var body = await ReadJson(await _client.SendAsync(request));

		Assert.That(body.GetProperty("success").GetBoolean(), Is.False);
	}

	private async Task<string> AdminToken()
	{
		var response = await SendJson(HttpMethod.Post,
			"/api/auth/login",
			new { username = "admin", password = "password123", scope = "admin" });
		Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), "login");
		var json = await ReadJson(response);
		return json.GetProperty("accessToken").GetString()!;
	}

	private async Task<string> CreateProfile(string name)
	{
		var response = await SendJson(HttpMethod.Post, "/api/profiles", new { name });
		Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), "create profile");
		var json = await ReadJson(response);
		return json.GetProperty("profile").GetProperty("id").GetString()!;
	}

	private async Task<string> FirstFolderId(string profileId)
	{
		using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/folders?profileId={profileId}");
		request.Headers.Add(LoopbackHeader, "1");
		var response = await _client.SendAsync(request);
		Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), "get folders");
		var json = await ReadJson(response);
		return json.GetProperty("folders")[0].GetProperty("id").GetString()!;
	}

	private async Task<string> CreateFolder(string profileId, string name, string? parentId)
	{
		var response = await SendJson(HttpMethod.Post, "/api/folders", new { profileId, name, parentId });
		Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), "create folder");
		var json = await ReadJson(response);
		return json.GetProperty("folder").GetProperty("id").GetString()!;
	}

	private async Task<string> CreateWidget(string folderId)
	{
		var response = await SendJson(HttpMethod.Post,
			"/api/widgets",
			new
			{
				folderId,
				type = "ActionButton",
				positionX = 0,
				positionY = 0,
				width = 1,
				height = 1
			});
		Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), "create widget");
		var json = await ReadJson(response);
		return json.GetProperty("widget").GetProperty("id").GetString()!;
	}

	private async Task<HttpResponseMessage> ImportProfile(byte[] archive, string? password)
	{
		using var content = new MultipartFormDataContent();
		var file = new ByteArrayContent(archive);
		file.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
		content.Add(file, "file", "profile.macroDeckProfile");
		if (password is not null)
		{
			content.Add(new StringContent(password), "password");
		}

		using var request = new HttpRequestMessage(HttpMethod.Post, "/api/profiles/import") { Content = content };
		request.Headers.Add(LoopbackHeader, "1");
		return await _client.SendAsync(request);
	}

	private async Task<HttpResponseMessage> ImportFolder(byte[] archive,
		string profileId,
		string? parentFolderId,
		string? password)
	{
		using var content = new MultipartFormDataContent();
		var file = new ByteArrayContent(archive);
		file.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
		content.Add(file, "file", "folder.macroDeckFolder");
		content.Add(new StringContent(profileId), "profileId");
		if (parentFolderId is not null)
		{
			content.Add(new StringContent(parentFolderId), "parentFolderId");
		}

		if (password is not null)
		{
			content.Add(new StringContent(password), "password");
		}

		using var request = new HttpRequestMessage(HttpMethod.Post, "/api/folders/import") { Content = content };
		request.Headers.Add(LoopbackHeader, "1");
		return await _client.SendAsync(request);
	}

	private async Task<HttpResponseMessage> SendJson(HttpMethod method, string path, object body)
	{
		using var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body) };
		request.Headers.Add(LoopbackHeader, "1");
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
