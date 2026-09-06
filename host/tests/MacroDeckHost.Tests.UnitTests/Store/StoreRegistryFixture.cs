using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MacroDeck.Signing.Certificates;
using MacroDeck.Signing.TestSupport;

namespace MacroDeckHost.Tests.UnitTests.Store;

/// <summary>Builds a signed registry tree in memory and serves it over a fake transport, so the store's
/// fetch-and-verify path can be exercised against real signatures without a network.</summary>
internal sealed class StoreRegistryFixture
{
	public const string BaseUrl = "https://registry.test/main/";

	private static readonly JsonSerializerOptions _json =
		new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

	private static readonly string[] _registryUsage = [SigningCertificateChain.RegistryKeyUsage];

	private readonly Dictionary<string, byte[]> _files = new(StringComparer.Ordinal);

	public StoreRegistryFixture(TestPki.IssuedCertificate? certificate = null)
	{
		Certificate = certificate ?? TestPki.IssueCertificate(subjectKind: "service", keyUsage: _registryUsage);
	}

	public TestPki.IssuedCertificate Certificate { get; }

	public long Sequence { get; set; } = 1;

	public List<string> PluginIds { get; } = [];

	public List<string> IconPackIds { get; } = [];

	public List<string> ProfileTemplateIds { get; } = [];

	public List<string> WidgetTemplateIds { get; } = [];

	public List<string> AutomationTemplateIds { get; } = [];

	public List<object> RemovedPackages { get; } = [];

	/// <summary>Curated picks written into <c>index.json</c>, or null to publish a registry that has no
	/// <c>featured</c> key at all - which is what every registry in the wild looks like today.</summary>
	public IReadOnlyList<object>? Featured { get; set; }

	public List<string> RevokedKeyIds { get; } = [];

	public Dictionary<string, byte[]> Served => _files;

	public HashSet<string> Unlisted { get; } = new(StringComparer.Ordinal);

	public Dictionary<string, long> DeclaredSizeOverrides { get; } = new(StringComparer.Ordinal);

	public Dictionary<string, string> DeclaredDigestOverrides { get; } = new(StringComparer.Ordinal);

	public List<string> DuplicateEntries { get; } = [];

	public HashSet<string> Missing { get; } = new(StringComparer.Ordinal);

	public void AddPackage(string kind,
		string id,
		string version = "1.0.0",
		string? name = null,
		string? description = null,
		string? publisher = null,
		string[]? supportedRids = null,
		string[]? languages = null,
		DateTimeOffset? createdAt = null,
		DateTimeOffset? updatedAt = null)
	{
		var segment = kind switch
		{
			"plugin" => "plugins",
			"icon-pack" => "icon-packs",
			"profile" => "templates/profiles",
			"widget" => "templates/widgets",
			_ => throw new ArgumentOutOfRangeException(nameof(kind))
		};

		switch (kind)
		{
			case "plugin": PluginIds.Add(id); break;
			case "icon-pack": IconPackIds.Add(id); break;
			case "profile": ProfileTemplateIds.Add(id); break;
			case "widget": WidgetTemplateIds.Add(id); break;
		}

		Write($"{segment}/{id}/manifest.json",
			new
			{
				kind,
				name = name ?? id,
				id,
				latestVersion = version,
				description,
				publisher,
				createdAt = createdAt ?? DateTimeOffset.UnixEpoch,
				updatedAt = updatedAt ?? DateTimeOffset.UnixEpoch,
				supportedRids,
				languages
			});

		Write($"{segment}/{id}/versions/{version}/manifest.json",
			new
			{
				url = $"https://assets.test/{id}/{version}/artifact.bin",
				sha256 = new string('a', 64),
				size = 16L,
				uploadedAt = DateTimeOffset.UnixEpoch
			});

		WriteText($"{segment}/{id}/versions/{version}/changelog.md", $"# {version}");
		WriteText($"{segment}/{id}/versions/{version}/description.md", description ?? id);
	}

	/// <summary>Publishes an additional version directory for a package that already exists, so version
	/// history can be exercised without the package's advertised latest version changing.</summary>
	public void AddVersion(string segment, string id, string version)
	{
		Write($"{segment}/{id}/versions/{version}/manifest.json",
			new
			{
				url = $"https://assets.test/{id}/{version}/artifact.bin",
				sha256 = new string('a', 64),
				size = 16L,
				uploadedAt = DateTimeOffset.UnixEpoch
			});

		WriteText($"{segment}/{id}/versions/{version}/changelog.md", $"# {version}");
	}

	public void Write(string path, object value) =>
		_files[path] = JsonSerializer.SerializeToUtf8Bytes(value, _json);

	public void WriteText(string path, string value) => _files[path] = Encoding.UTF8.GetBytes(value);

	public IHttpClientFactory Build()
	{
		var index = new Dictionary<string, object?>
		{
			["version"] = 1,
			["generatedAt"] = DateTimeOffset.UnixEpoch,
			["plugins"] = PluginIds,
			["iconPacks"] = IconPackIds,
			["templates"] = new
			{
				automations = AutomationTemplateIds,
				folders = Array.Empty<string>(),
				profiles = ProfileTemplateIds,
				widgets = WidgetTemplateIds
			}
		};

		if (Featured is not null)
		{
			index["featured"] = Featured;
		}

		Write("index.json", index);

		Write("security.json",
			new
			{
				version = 1,
				revokedKeys = RevokedKeyIds.Select(keyId => new { keyId, reason = "test" }),
				removedPackages = RemovedPackages
			});

		_files[$"certificates/{Certificate.CertificateId}.json"] = Certificate.CertificateBytes;
		_files[$"certificates/{Certificate.CertificateId}.sig"] = Certificate.CertificateSignatureBytes;

		var declared = _files.Keys
			.Where(path => !Unlisted.Contains(path))
			.Select(path => new
			{
				path,
				sha256 = DeclaredDigestOverrides.TryGetValue(path, out var digest)
					? digest
					: Convert.ToHexStringLower(SHA256.HashData(_files[path])),
				size = DeclaredSizeOverrides.TryGetValue(path, out var size) ? size : _files[path].LongLength
			})
			.Concat(DuplicateEntries.Select(path => new
			{
				path,
				sha256 = Convert.ToHexStringLower(SHA256.HashData(_files[path])),
				size = _files[path].LongLength
			}))
			.OrderBy(entry => entry.path, StringComparer.Ordinal)
			.ToList();

		var manifestBytes = JsonSerializer.SerializeToUtf8Bytes(new
			{
				schemaVersion = 1,
				sequence = Sequence,
				generatedAt = DateTimeOffset.UnixEpoch,
				files = declared
			},
			_json);

		var rawSignature = Convert.FromBase64String(Encoding.UTF8.GetString(
			TestPki.SignCertificateBytesAsBase64Text(Certificate.PrivateKey, manifestBytes)));

		// RegistrySignatureDocument is internal to MacroDeck.Signing; only the manifest bytes are
		// signature-covered, so the signature document itself is written as plain JSON here.
		var signatureBytes = JsonSerializer.SerializeToUtf8Bytes(new
			{
				schemaVersion = 1,
				algorithm = "ed25519",
				keyId = Certificate.CertificateId,
				value = Convert.ToBase64String(rawSignature),
				signedAt = SignedAt
			},
			_json);

		var served = new Dictionary<string, byte[]>(_files, StringComparer.Ordinal)
		{
			["registry-manifest.json"] = manifestBytes,
			["registry-signature.json"] = signatureBytes
		};

		foreach (var missing in Missing)
		{
			served.Remove(missing);
		}

		return new FixtureHttpClientFactory(served, this);
	}

	public DateTimeOffset SignedAt { get; set; } = DateTimeOffset.UnixEpoch;

	public bool Offline { get; set; }

	private sealed class FixtureHttpClientFactory : IHttpClientFactory
	{
		private readonly IReadOnlyDictionary<string, byte[]> _served;
		private readonly StoreRegistryFixture _fixture;

		public FixtureHttpClientFactory(IReadOnlyDictionary<string, byte[]> served,
			StoreRegistryFixture fixture)
		{
			_served = served;
			_fixture = fixture;
		}

		public HttpClient CreateClient(string name) => new(new Handler(_served, _fixture));

		private sealed class Handler : HttpMessageHandler
		{
			private readonly IReadOnlyDictionary<string, byte[]> _served;
			private readonly StoreRegistryFixture _fixture;

			public Handler(IReadOnlyDictionary<string, byte[]> served, StoreRegistryFixture fixture)
			{
				_served = served;
				_fixture = fixture;
			}

			protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
				CancellationToken cancellationToken)
			{
				if (_fixture.Offline)
				{
					return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
					{
						Content = new ByteArrayContent([])
					});
				}

				var path = request.RequestUri!.AbsolutePath;
				const string prefix = "/main/";
				var relative = path.StartsWith(prefix, StringComparison.Ordinal)
					? path[prefix.Length..]
					: path.TrimStart('/');

				return Task.FromResult(_served.TryGetValue(relative, out var body)
					? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) }
					: new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new ByteArrayContent([]) });
			}
		}
	}
}
