using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;
using MacroDeckHost.Application.Mcp;
using MacroDeckHost.Application.Paths;
using MacroDeckHost.Infrastructure.Autostart;
using MacroDeckHost.Mcp;
using MacroDeckHost.Tests.UnitTests.Auth;

namespace MacroDeckHost.Tests.UnitTests.Mcp;

public partial class McpEndpointTests
{
	private const string GermanExtensionDescription
		= "Lässt Claude mit deinem Macro-Deck-Setup auf diesem Computer arbeiten: Buttons, Ordner und "
		+ "Automationen ansehen und erstellen und Aktionen ausführen. Claude sieht deine Geheimnisse nie.";

	private static readonly byte[] _pngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

	private readonly RecordingExtensionTokenStore _extensionTokens = new();

	private static bool ClaudeDesktopPlatform => OperatingSystem.IsMacOS() || OperatingSystem.IsWindows();

	[Test]
	public async Task A_live_token_downloads_an_mcpb_bundle_that_runs_the_installed_shell_without_the_token()
	{
		Assume.That(ClaudeDesktopPlatform, "Claude Desktop runs on macOS and Windows only");
		using var shell = new FakeShellExecutable();
		var token = await CreateToken();
		await SetLocalization(new { culture = "de" });

		HttpResponseMessage response;
		try
		{
			response = await DownloadExtension(token.Id, token.Plaintext);
		}
		finally
		{
			await SetLocalization(new { followSystem = true });
		}

		var bytes = await response.Content.ReadAsByteArrayAsync();
		var entries = new List<string>();
		using (var scan = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read))
		{
			foreach (var entry in scan.Entries)
			{
				using var reader = new StreamReader(entry.Open());
				entries.Add(await reader.ReadToEndAsync());
			}
		}

		using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
		var icon = new byte[_pngSignature.Length];
		var iconEntry = archive.GetEntry("icon.png");
		Assert.That(iconEntry, Is.Not.Null);
		await using (var iconStream = iconEntry!.Open())
		{
			await iconStream.ReadExactlyAsync(icon);
		}

		var manifestEntry = archive.GetEntry("manifest.json");
		Assert.That(manifestEntry, Is.Not.Null);
		await using var manifestStream = manifestEntry!.Open();
		var manifest = (await JsonNode.ParseAsync(manifestStream))!.AsObject();
		var schema = JsonSchema.FromText(await File.ReadAllTextAsync(Path.Combine(
			TestContext.CurrentContext.TestDirectory,
			"Mcp",
			"Fixtures",
			"mcpb-manifest-v0.3.schema.json")));
		var validation = schema.Evaluate(JsonDocument.Parse(manifest.ToJsonString()).RootElement,
			new EvaluationOptions { OutputFormat = OutputFormat.List });
		var server = manifest["server"]!;

		await Assert.MultipleAsync(() =>
		{
			Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
			Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/zip"));
			Assert.That(response.Content.Headers.ContentDisposition?.FileName?.Trim('"'),
				Is.EqualTo("macro-deck.mcpb"));
			Assert.That(response.Headers.CacheControl?.NoStore, Is.True);
			Assert.That(validation.IsValid, Is.True, JsonSerializer.Serialize(validation));
			Assert.That(manifest["manifest_version"]!.GetValue<string>(), Is.EqualTo("0.3"));
			Assert.That(manifest["display_name"]!.GetValue<string>(), Is.EqualTo("Macro Deck"));
			Assert.That(manifest["description"]!.GetValue<string>(), Is.EqualTo(GermanExtensionDescription));
			Assert.That(server["type"]!.GetValue<string>(), Is.EqualTo("binary"));
			Assert.That(server["mcp_config"]!["command"]!.GetValue<string>(), Is.EqualTo(shell.Executable));
			Assert.That(server["mcp_config"]!["args"]!.AsArray().Select(arg => arg!.GetValue<string>()),
				Is.EqualTo(new[] { "mcp" }));
			Assert.That(server["mcp_config"]!["env"]!.AsObject().Select(pair => pair.Key),
				Is.EqualTo(new[] { "MACRODECK_MCP_TOKEN_ID" }));
			Assert.That(server["mcp_config"]!["env"]!["MACRODECK_MCP_TOKEN_ID"]!.GetValue<string>(),
				Is.EqualTo(token.Id.ToString("D")));
			Assert.That(entries, Has.None.Contains(McpTokenService.TokenPrefix));
			Assert.That(_extensionTokens.StoredToken(token.Id), Is.EqualTo(token.Plaintext));
			Assert.That(manifest["icon"]!.GetValue<string>(), Is.EqualTo("icon.png"));
			Assert.That(icon, Is.EqualTo(_pngSignature));
			Assert.That(manifest["compatibility"]!["platforms"]!.AsArray().Select(p => p!.GetValue<string>()),
				Is.EqualTo(new[] { OperatingSystem.IsMacOS() ? "darwin" : "win32" }));
			return Task.CompletedTask;
		});
	}

	[Test]
	public async Task The_endpoint_offers_the_extension_only_when_the_installed_shell_is_known()
	{
		bool withShell;
		using (new FakeShellExecutable())
		{
			withShell = await ExtensionAvailable();
		}

		var withoutShell = await ExtensionAvailable();
		var token = await CreateToken();
		var download = await DownloadExtension(token.Id, token.Plaintext);
		var stored = _extensionTokens.StoredToken(token.Id);

		await Assert.MultipleAsync(async () =>
		{
			Assert.That(withShell, Is.EqualTo(ClaudeDesktopPlatform));
			Assert.That(withoutShell, Is.False);
			Assert.That(download.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
			Assert.That(await FailureCode(download), Is.EqualTo("extension_unavailable"));
			Assert.That(stored, Is.Null);
		});
	}

	[Test]
	public void A_portable_installation_does_not_offer_the_extension()
	{
		Assume.That(ClaudeDesktopPlatform, "Claude Desktop runs on macOS and Windows only");
		using var shell = new FakeShellExecutable();

		Assert.Multiple(() =>
		{
			Assert.That(ClaudeDesktopExtension.IsAvailable(DataRootEnvironmentWith(true)), Is.False);
			Assert.That(ClaudeDesktopExtension.IsAvailable(DataRootEnvironmentWith(false)), Is.True);
		});
	}

	[Test]
	public async Task A_token_that_cannot_be_stored_securely_is_never_put_into_an_extension()
	{
		Assume.That(ClaudeDesktopPlatform, "Claude Desktop runs on macOS and Windows only");
		using var shell = new FakeShellExecutable();
		var token = await CreateToken();

		_extensionTokens.Fails = true;
		HttpResponseMessage response;
		try
		{
			response = await DownloadExtension(token.Id, token.Plaintext);
		}
		finally
		{
			_extensionTokens.Fails = false;
		}

		await Assert.MultipleAsync(async () =>
		{
			Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
			Assert.That(response.Content.Headers.ContentType?.MediaType, Is.Not.EqualTo("application/zip"));
			Assert.That(await FailureCode(response), Is.EqualTo("secure_store_unavailable"));
		});
	}

	[Test]
	public async Task Revoking_or_deleting_a_token_removes_its_stored_extension_token()
	{
		Assume.That(ClaudeDesktopPlatform, "Claude Desktop runs on macOS and Windows only");
		using var shell = new FakeShellExecutable();
		var token = await CreateToken();
		await DownloadExtension(token.Id, token.Plaintext);
		var storedBeforeRevoke = _extensionTokens.StoredToken(token.Id);

		await Send(HttpMethod.Post, $"/api/mcp/tokens/{token.Id}/revoke", FakeConnectionShape.Loopback);
		var storedAfterRevoke = _extensionTokens.StoredToken(token.Id);
		var removalsAfterRevoke = _extensionTokens.Removals(token.Id);
		await Send(HttpMethod.Delete, $"/api/mcp/tokens/{token.Id}", FakeConnectionShape.Loopback);

		await Assert.MultipleAsync(() =>
		{
			Assert.That(storedBeforeRevoke, Is.EqualTo(token.Plaintext));
			Assert.That(storedAfterRevoke, Is.Null);
			Assert.That(removalsAfterRevoke, Is.EqualTo(1));
			Assert.That(_extensionTokens.Removals(token.Id), Is.EqualTo(2));
			return Task.CompletedTask;
		});
	}

	private static DataRootEnvironment DataRootEnvironmentWith(bool portable)
	{
		var current = DataRootEnvironment.Current();
		return current with
		{
			GetEnvironmentVariable = name => name switch
			{
				MacroDeckDataRootResolver.PortableEnvironmentVariable => portable ? "1" : null,
				MacroDeckDataRootResolver.DataDirEnvironmentVariable => null,
				MacroDeckDataRootResolver.DataDirectoryEnvironmentVariable => null,
				_ => current.GetEnvironmentVariable(name)
			},
			FileExists = _ => false
		};
	}

	[Test]
	public async Task The_extension_is_built_only_for_the_live_token_it_was_requested_for()
	{
		Assume.That(ClaudeDesktopPlatform, "Claude Desktop runs on macOS and Windows only");
		using var shell = new FakeShellExecutable();
		var token = await CreateToken();
		var other = await CreateToken();
		var revoked = await CreateToken();
		await Send(HttpMethod.Post, $"/api/mcp/tokens/{revoked.Id}/revoke", FakeConnectionShape.Loopback);

		var mismatched = await DownloadExtension(token.Id, other.Plaintext);
		var unknown = await DownloadExtension(token.Id, McpTokenService.TokenPrefix + new string('0', 64));
		var missing = await DownloadExtension(token.Id, null);
		var revokedDownload = await DownloadExtension(revoked.Id, revoked.Plaintext);
		var anonymous = await Send(HttpMethod.Post,
			$"/api/mcp/tokens/{token.Id}/claude-desktop-extension",
			FakeConnectionShape.PublicLan,
			body: new { token = token.Plaintext });

		await Assert.MultipleAsync(async () =>
		{
			Assert.That(mismatched.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
			Assert.That(await FailureCode(mismatched), Is.EqualTo("invalid_token"));
			Assert.That(await FailureCode(unknown), Is.EqualTo("invalid_token"));
			Assert.That(await FailureCode(missing), Is.EqualTo("invalid_token"));
			Assert.That(revokedDownload.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
			Assert.That(await FailureCode(revokedDownload), Is.EqualTo("token_revoked"));
			Assert.That(anonymous.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
			Assert.That(_extensionTokens.StoredToken(token.Id), Is.Null);
			Assert.That(_extensionTokens.StoredToken(revoked.Id), Is.Null);
		});
	}

	private Task<HttpResponseMessage> DownloadExtension(Guid id, string? token)
	{
		return Send(HttpMethod.Post,
			$"/api/mcp/tokens/{id}/claude-desktop-extension",
			FakeConnectionShape.Loopback,
			body: new { token });
	}

	private async Task<bool> ExtensionAvailable()
	{
		var response = await Send(HttpMethod.Get, "/api/mcp/endpoint", FakeConnectionShape.Loopback);
		var json = await response.Content.ReadFromJsonAsync<JsonElement>();
		return json.GetProperty("extensionAvailable").GetBoolean();
	}

	private async Task SetLocalization(object body)
	{
		var response = await Send(HttpMethod.Put,
			"/api/settings/localization",
			FakeConnectionShape.Loopback,
			body: body);
		Assert.That(response.IsSuccessStatusCode, Is.True);
	}

	private static async Task<string?> FailureCode(HttpResponseMessage response)
	{
		var json = await response.Content.ReadFromJsonAsync<JsonElement>();
		return json.GetProperty("error").GetProperty("code").GetString();
	}

	private sealed class RecordingExtensionTokenStore : IMcpExtensionTokenStore
	{
		private readonly Dictionary<Guid, string> _stored = [];
		private readonly Dictionary<Guid, int> _removals = [];
		private readonly Lock _gate = new();

		public bool Fails { get; set; }

		public Task<bool> Store(Guid tokenId, string token)
		{
			lock (_gate)
			{
				if (Fails)
				{
					return Task.FromResult(false);
				}

				_stored[tokenId] = token;
				return Task.FromResult(true);
			}
		}

		public Task Remove(Guid tokenId)
		{
			lock (_gate)
			{
				_stored.Remove(tokenId);
				_removals[tokenId] = _removals.GetValueOrDefault(tokenId) + 1;
				return Task.CompletedTask;
			}
		}

		public string? StoredToken(Guid tokenId)
		{
			lock (_gate)
			{
				return _stored.GetValueOrDefault(tokenId);
			}
		}

		public int Removals(Guid tokenId)
		{
			lock (_gate)
			{
				return _removals.GetValueOrDefault(tokenId);
			}
		}
	}

	private sealed class FakeShellExecutable : IDisposable
	{
		private readonly string? _previous;

		public FakeShellExecutable()
		{
			Executable = Path.Combine(Path.GetTempPath(), $"macro-deck-shell-{Guid.NewGuid():N}");
			File.WriteAllText(Executable, string.Empty);
			_previous = Environment.GetEnvironmentVariable(AutostartService.ShellExecutableEnvironmentVariable);
			Environment.SetEnvironmentVariable(AutostartService.ShellExecutableEnvironmentVariable, Executable);
		}

		public string Executable { get; }

		public void Dispose()
		{
			Environment.SetEnvironmentVariable(AutostartService.ShellExecutableEnvironmentVariable, _previous);
			File.Delete(Executable);
		}
	}
}
