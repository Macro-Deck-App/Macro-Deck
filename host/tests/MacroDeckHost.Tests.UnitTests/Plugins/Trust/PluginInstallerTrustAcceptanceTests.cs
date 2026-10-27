using System.Text.Json.Nodes;
using MacroDeck.Plugin.Packaging.Artifacts;
using MacroDeck.Plugin.Packaging.Manifest;
using MacroDeck.Signing.TestSupport;
using MacroDeckHost.Application.Persistence.Repositories;
using MacroDeckHost.Application.Plugins;
using MacroDeckHost.Application.Plugins.Installation;
using MacroDeckHost.Application.Plugins.Trust;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Infrastructure.Plugins;
using MacroDeckHost.Infrastructure.Plugins.Installation;
using MacroDeckHost.Infrastructure.Plugins.Trust;
using MacroDeckHost.Tests.UnitTests.Plugins.Installation;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using MacroDeckHost.Application.Plugins.Runtime;
using MacroDeckHost.Application.Store;
using MacroDeckHost.Application.Store.Model;

namespace MacroDeckHost.Tests.UnitTests.Plugins.Trust;

/// <summary>Acceptance tests for host-side plugin signature enforcement (issue #610), run against real
/// signed/tampered <c>.macroDeckPlugin</c> archives built with <see cref="SignedPluginArtifacts" /> and a
/// real <see cref="PluginTrustEvaluator" /> anchored to <see cref="TestPki.Root" />.
///
/// <para>A "ContentMismatch" fixture at install time is deliberately not exercised here: any tampering that
/// would make MacroDeck.Signing's file validator report FileDigestMismatch/FileSizeMismatch/UndeclaredFile/
/// DeclaredFileMissing is, on the exact same extracted files, also exactly what <c>PluginInstaller</c>'s
/// own pre-existing <c>VerifyDigests</c> check reports as <c>HashMismatch</c> - and that check runs first
/// (see <c>InstallLocked</c>), so it always wins for a freshly staged artifact. ContentMismatch only
/// becomes reachable once a version is already on disk and re-evaluated without a fresh digest check
/// alongside it - see <c>PluginSupervisorTrustAcceptanceTests</c>' launch-time tamper test.</para>
/// </summary>
[TestFixture]
internal sealed class PluginInstallerTrustAcceptanceTests
{
	private const string PluginId = "com.example.trust-plugin";

	private TestPaths _paths = null!;
	private FakeInstallSupervisor _supervisor = null!;
	private PluginInstallationCatalog _catalog = null!;
	private FakeIntegrationRegistrar _integrationRegistrar = null!;
	private FakeUrlHttpClientFactory _httpClientFactory = null!;
	private InMemoryPluginTrustRecordRepository _trustRecords = null!;
	private FakeDeveloperModePreferences _preferences = null!;
	private PluginInstaller _installer = null!;
	private string _sourceDirectory = null!;
	private StoreCatalog _storeCatalog = null!;

	[SetUp]
	public void SetUp()
	{
		_paths = new TestPaths();
		Directory.CreateDirectory(_paths.PluginsDirectory);
		Directory.CreateDirectory(_paths.PluginStagingDirectory);
		Directory.CreateDirectory(_paths.PluginCacheDirectory);

		_sourceDirectory = Path.Combine(_paths.BaseDirectory, "artifacts");
		Directory.CreateDirectory(_sourceDirectory);

		_catalog = new PluginInstallationCatalog(_paths, Serilog.Core.Logger.None);
		var sessionRegistry = new PluginSessionRegistry(TimeProvider.System, Serilog.Core.Logger.None);
		_supervisor = new FakeInstallSupervisor(_catalog, sessionRegistry);
		_integrationRegistrar = new FakeIntegrationRegistrar();
		_httpClientFactory = new FakeUrlHttpClientFactory();
		_trustRecords = new InMemoryPluginTrustRecordRepository();
		_storeCatalog = new StoreCatalog();
		_preferences = new FakeDeveloperModePreferences();

		var manifestReader = new PluginManifestReader();
		var options = PluginInstallerOptions.Default with { ActivationHealthTimeout = TimeSpan.FromSeconds(2) };

		var services = new ServiceCollection();
		services.AddSingleton<IPluginRegistrationRepository, InMemoryPluginRegistrationRepository>();
		services.AddSingleton<IPluginTrustRecordRepository>(_trustRecords);
		services.AddSingleton(TestLocalization.Resolver);
		services.AddSingleton<IPluginTrustBaseline, FakePluginTrustBaseline>();
		services.AddScoped<IAppPreferenceService>(_ => _preferences);
		var provider = services.BuildServiceProvider();

		var trustEvaluator = new PluginTrustEvaluator(manifestReader,
			new StoreRegistryRevocationSource(_storeCatalog),
			new PluginTrustOptions { RootPublicKeyOverride = TestPki.Root.PublicKey },
			Serilog.Core.Logger.None);

		_installer = new PluginInstaller(_paths,
			new PluginArtifactReader(manifestReader, Serilog.Core.Logger.None),
			new PluginArtifactAcquirer(_httpClientFactory, options, Serilog.Core.Logger.None),
			new PluginArtifactCache(_paths, options, Serilog.Core.Logger.None),
			trustEvaluator,
			new PluginDependencyResolver(_catalog, manifestReader),
			manifestReader,
			_catalog,
			_supervisor,
			_integrationRegistrar,
			sessionRegistry,
			new PluginTakeoverRegistry(),
			provider.GetRequiredService<IServiceScopeFactory>(),
			options,
			TimeProvider.System,
			Serilog.Core.Logger.None);
	}

	[TearDown]
	public void TearDown()
	{
		_paths.Cleanup();
	}

	private Task<string> TrustedArtifact(string version = "1.0.0", string pluginId = PluginId)
	{
		return SignedPluginArtifacts.CreateSignedAsync(_sourceDirectory,
			pluginId,
			version,
			fileName: $"trusted-{pluginId}-{version}-{Guid.NewGuid():N}.macroDeckPlugin");
	}

	private async Task<string> SignatureInvalidArtifact(string version = "1.0.0", string pluginId = PluginId)
	{
		var artifact = await TrustedArtifact(version, pluginId);
		var manifestJson = PackageArchiveFixtures.ReadEntryText(artifact, "manifest.json");
		var tampered = FlipSignatureValueCharacter(manifestJson);
		PackageArchiveFixtures.WriteEntryText(artifact, "manifest.json", tampered);
		return artifact;
	}

	private Task<string> UntrustedRootArtifact(string version = "1.0.0", string pluginId = PluginId)
	{
		return SignedPluginArtifacts.CreateSignedAsync(_sourceDirectory,
			pluginId,
			version,
			fileName: $"untrusted-root-{pluginId}-{version}-{Guid.NewGuid():N}.macroDeckPlugin",
			signingRoot: TestPki.OtherRoot);
	}

	private Task<string> WrongCertificatePurposeArtifact(string version = "1.0.0", string pluginId = PluginId)
	{
		return SignedPluginArtifacts.CreateSignedAsync(_sourceDirectory,
			pluginId,
			version,
			fileName: $"wrong-purpose-{pluginId}-{version}-{Guid.NewGuid():N}.macroDeckPlugin",
			keyUsage: ["something-else"]);
	}

	private Task<string> NotYetValidArtifact(string version = "1.0.0", string pluginId = PluginId)
	{
		return SignedPluginArtifacts.CreateSignedAsync(_sourceDirectory,
			pluginId,
			version,
			fileName: $"not-yet-valid-{pluginId}-{version}-{Guid.NewGuid():N}.macroDeckPlugin",
			notBefore: DateTimeOffset.UtcNow.AddDays(30));
	}

	private async Task<string> MalformedArtifact(string version = "1.0.0", string pluginId = PluginId)
	{
		var artifact = await TrustedArtifact(version, pluginId);
		PackageArchiveFixtures.ReplaceEntry(artifact, PluginArtifactFiles.CertificateFileName, "not a certificate");
		return artifact;
	}

	private Task<string> UnsignedArtifact(
		string version = "1.0.0",
		string pluginId = PluginId,
		string content = "binary")
	{
		return Task.FromResult(new PluginArtifactBuilder()
			.WithManifest(ManifestJson.Build(version, pluginId, null))
			.WithFile(ManifestJson.EntrypointExecutable, content)
			.WriteTo(_sourceDirectory, $"unsigned-{pluginId}-{version}-{Guid.NewGuid():N}.macroDeckPlugin"));
	}

	// Flips a character well inside the base64 body, never the last character: the signature is 64 bytes,
	// so its base64 form is padded, and mutating the pad character would make it fail to decode at all
	// (SignatureMalformed) rather than decode to the wrong bytes (SignatureInvalid), which is the failure
	// this fixture needs.
	private static string FlipSignatureValueCharacter(string manifestJson)
	{
		var node = JsonNode.Parse(manifestJson)!.AsObject();
		var signature = node["signature"]!.AsObject();
		var value = signature["value"]!.GetValue<string>();

		const int tamperIndex = 10;
		var flipped = value[tamperIndex] == 'A' ? 'B' : 'A';
		signature["value"] = value[..tamperIndex] + flipped + value[(tamperIndex + 1)..];

		return node.ToJsonString();
	}

	private async Task<PluginInstallResult> InstallFromKind(
		PluginArtifactSourceKind kind,
		string artifactPath,
		PluginInstallRequest request)
	{
		switch (kind)
		{
			case PluginArtifactSourceKind.LocalPath:
				return await _installer.Install(PluginArtifactSource.FromPath(artifactPath), request);
			case PluginArtifactSourceKind.Upload:
			{
				await using var stream = File.OpenRead(artifactPath);
				return await _installer.Install(PluginArtifactSource.FromUpload(stream), request);
			}
			case PluginArtifactSourceKind.Url:
			{
				_httpClientFactory.Body = await File.ReadAllBytesAsync(artifactPath);
				var url = new Uri($"https://plugins.example/{Path.GetFileName(artifactPath)}");
				return await _installer.Install(PluginArtifactSource.FromUrl(url), request);
			}
			case PluginArtifactSourceKind.TestBuild:
			{
				var bytes = await File.ReadAllBytesAsync(artifactPath);
				_httpClientFactory.Body = bytes;
				var url = new Uri($"https://staging.example/{Path.GetFileName(artifactPath)}");
				return await _installer.Install(PluginArtifactSource.FromTestBuild(url,
						"sha256:"
						+ Convert.ToHexStringLower(global::System.Security.Cryptography.SHA256.HashData(bytes))),
					request);
			}
			default:
				throw new NotSupportedException(kind.ToString());
		}
	}

	private bool PluginTreeExists(string pluginId = PluginId)
	{
		return Directory.Exists(PluginInstallPaths.PluginDirectory(_paths.PluginsDirectory, pluginId));
	}

	[Test]
	public async Task S18_a_tampered_signed_package_with_consent_leaves_no_plugin_tree_at_all()
	{
		var artifact = await SignatureInvalidArtifact();

		var result = await _installer.Install(PluginArtifactSource.FromPath(artifact),
			new PluginInstallRequest { AllowUnsigned = true });

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(PluginInstallError.SignatureInvalid));
			Assert.That(PluginTreeExists(), Is.False);
		});
	}

	[TestCase(PluginArtifactSourceKind.LocalPath)]
	[TestCase(PluginArtifactSourceKind.Upload)]
	[TestCase(PluginArtifactSourceKind.Url)]
	public async Task S19_21_the_same_tampered_fixture_reports_the_same_error_from_every_source_kind(
		PluginArtifactSourceKind kind)
	{
		var artifact = await SignatureInvalidArtifact();

		var result = await InstallFromKind(kind, artifact, new PluginInstallRequest { AllowUnsigned = true });

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(PluginInstallError.SignatureInvalid));
			Assert.That(result.Signature?.Verdict, Is.EqualTo(PluginTrustVerdict.SignatureInvalid));
			Assert.That(PluginTreeExists(), Is.False);
		});
	}

	[Test]
	public async Task S23_unsigned_local_path_with_consent_installs_and_reads_back_as_unsigned()
	{
		var artifact = await UnsignedArtifact();

		var install = await _installer.Install(PluginArtifactSource.FromPath(artifact),
			new PluginInstallRequest { AllowUnsigned = true });

		Assert.That(install.Success, Is.True, install.ErrorMessage);

		var record = await _trustRecords.GetVersion(PluginId, "1.0.0");
		Assert.That(record?.AdmittedVerdict, Is.EqualTo(PluginTrustRecordVerdicts.Unsigned));
	}

	[Test]
	public async Task S24_unsigned_local_path_without_consent_is_refused_with_no_plugin_tree()
	{
		var artifact = await UnsignedArtifact();

		var result = await _installer.Install(PluginArtifactSource.FromPath(artifact), new PluginInstallRequest());

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(PluginInstallError.UnsignedNotPermitted));
			Assert.That(PluginTreeExists(), Is.False);
		});
	}

	[Test]
	public async Task S25_unsigned_url_with_consent_true_is_refused_while_developer_mode_is_off()
	{
		_preferences.DeveloperMode = false;
		var artifact = await UnsignedArtifact();

		var result = await InstallFromKind(PluginArtifactSourceKind.Url,
			artifact,
			new PluginInstallRequest { AllowUnsigned = true });

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(PluginInstallError.UnsignedNotPermitted));
			Assert.That(PluginTreeExists(), Is.False);
		});
	}

	[Test]
	public async Task S25b_unsigned_url_with_consent_true_installs_as_unsigned_while_developer_mode_is_on()
	{
		_preferences.DeveloperMode = true;
		var artifact = await UnsignedArtifact();

		var result = await InstallFromKind(PluginArtifactSourceKind.Url,
			artifact,
			new PluginInstallRequest { AllowUnsigned = true });

		var record = await _trustRecords.GetVersion(PluginId, "1.0.0");
		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True, result.ErrorMessage);
			Assert.That(record?.AdmittedVerdict, Is.EqualTo(PluginTrustRecordVerdicts.Unsigned));
		});
	}

	[Test]
	public async Task S25c_developer_mode_does_not_make_a_failed_signature_consentable_from_a_url()
	{
		_preferences.DeveloperMode = true;
		var artifact = await MalformedArtifact();

		var result = await InstallFromKind(PluginArtifactSourceKind.Url,
			artifact,
			new PluginInstallRequest { AllowUnsigned = true });

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.Not.EqualTo(PluginInstallError.UnsignedNotPermitted));
			Assert.That(PluginTreeExists(), Is.False);
		});
	}

	[Test]
	public async Task
		S26_every_signature_failure_category_is_refused_from_every_source_kind_and_never_reads_as_unsigned_not_permitted()
	{
		var fixtures = new (string Name, Func<Task<string>> Build)[]
		{
			("SignatureInvalid", () => SignatureInvalidArtifact()),
			("UntrustedRoot", () => UntrustedRootArtifact()),
			("WrongCertificatePurpose", () => WrongCertificatePurposeArtifact()),
			("CertificateNotValidAtSignature", () => NotYetValidArtifact()),
			("Malformed", () => MalformedArtifact())
		};

		var failures = new List<string>();

		foreach (var (name, build) in fixtures)
		{
			foreach (var kind in Enum.GetValues<PluginArtifactSourceKind>())
			{
				var artifact = await build();
				var result = await InstallFromKind(kind, artifact, new PluginInstallRequest { AllowUnsigned = true });

				if (result.Success)
				{
					failures.Add($"{name}/{kind} unexpectedly succeeded.");
				}

				if (PluginTreeExists())
				{
					failures.Add($"{name}/{kind} left a plugin tree behind.");
				}

				if (result.Error == PluginInstallError.UnsignedNotPermitted)
				{
					failures.Add($"{name}/{kind} collapsed into the consentable unsigned bucket.");
				}
			}
		}

		Assert.That(failures, Is.Empty, string.Join(Environment.NewLine, failures));
	}

	[Test]
	public async Task S28_a_tampered_update_is_refused_and_the_trusted_running_incumbent_stays_active()
	{
		var installed = await _installer.Install(PluginArtifactSource.FromPath(await TrustedArtifact()),
			new PluginInstallRequest());
		Assert.That(installed.Success, Is.True, installed.ErrorMessage);
		Assert.That(_supervisor.IsRunning(PluginId), Is.True);

		var tampered = await SignatureInvalidArtifact("1.1.0");
		var result = await _installer.Install(PluginArtifactSource.FromPath(tampered),
			new PluginInstallRequest { AllowUnsigned = true });

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(_catalog.Discover()
					.First(p => p.PluginId == PluginId)
					.ActiveVersion?.Version,
				Is.EqualTo("1.0.0"));
			Assert.That(_supervisor.IsRunning(PluginId), Is.True);
		});
	}

	[Test]
	public async Task S29_a_trusted_incumbent_does_not_launder_the_updates_verdict()
	{
		await _installer.Install(PluginArtifactSource.FromPath(await TrustedArtifact()), new PluginInstallRequest());

		var untrusted = await UntrustedRootArtifact("1.1.0");
		var result = await _installer.Install(PluginArtifactSource.FromPath(untrusted),
			new PluginInstallRequest { AllowUnsigned = true });

		Assert.That(result.Signature?.Verdict, Is.EqualTo(PluginTrustVerdict.UntrustedRoot));
	}

	[Test]
	public async Task S30_an_unsigned_update_over_a_trusted_version_is_a_monotonic_downgrade()
	{
		await _installer.Install(PluginArtifactSource.FromPath(await TrustedArtifact()), new PluginInstallRequest());

		var unsigned = await UnsignedArtifact("1.1.0");
		var result = await _installer.Install(PluginArtifactSource.FromPath(unsigned),
			new PluginInstallRequest { AllowUnsigned = true });

		Assert.Multiple(() =>
		{
			Assert.That(result.Error, Is.EqualTo(PluginInstallError.TrustDowngrade));
			Assert.That(_catalog.Discover()
					.First(p => p.PluginId == PluginId)
					.ActiveVersion?.Version,
				Is.EqualTo("1.0.0"));
		});
	}

	[Test]
	public async Task S50_the_downgrade_check_is_keyed_on_the_highest_admitted_tier_not_just_current_json()
	{
		await _installer.Install(PluginArtifactSource.FromPath(await TrustedArtifact()), new PluginInstallRequest());

		// Simulates current.json going missing or unreadable: ActiveVersionOf(...) can no longer name the
		// previously-trusted version, so a downgrade check keyed on that version alone would find no
		// record at all and let an unsigned update through with consent.
		File.Delete(PluginInstallPaths.CurrentFilePath(_paths.PluginsDirectory, PluginId));
		_catalog.Invalidate();

		var unsigned = await UnsignedArtifact("1.1.0");
		var result = await _installer.Install(PluginArtifactSource.FromPath(unsigned),
			new PluginInstallRequest { AllowUnsigned = true });

		Assert.That(result.Error, Is.EqualTo(PluginInstallError.TrustDowngrade));
	}

	private Task<string> OtherPublisherArtifact(string version = "1.0.0")
	{
		return SignedPluginArtifacts.CreateSignedAsync(_sourceDirectory,
			PluginId,
			version,
			fileName: $"other-publisher-{version}-{Guid.NewGuid():N}.macroDeckPlugin",
			subjectId: "creator-2");
	}

	private string ActiveVersion()
	{
		_catalog.Invalidate();
		return _catalog.Discover().First(p => p.PluginId == PluginId).ActiveVersion!.Version;
	}

	private async Task InstallTrusted(string version = "1.0.0")
	{
		var installed = await _installer.Install(PluginArtifactSource.FromPath(await TrustedArtifact(version)),
			new PluginInstallRequest());
		Assert.That(installed.Success, Is.True, installed.ErrorMessage);
	}

	[TestCase(PluginArtifactSourceKind.LocalPath)]
	[TestCase(PluginArtifactSourceKind.Upload)]
	[TestCase(PluginArtifactSourceKind.Url)]
	[TestCase(PluginArtifactSourceKind.TestBuild)]
	public async Task A_trusted_update_signed_by_another_publisher_is_refused_and_keeps_the_installed_version(
		PluginArtifactSourceKind kind)
	{
		await InstallTrusted();
		var recordBefore = await _trustRecords.GetVersion(PluginId, "1.0.0");

		var result = await InstallFromKind(kind,
			await OtherPublisherArtifact("1.1.0"),
			new PluginInstallRequest { AllowUnsigned = true });

		await Assert.MultipleAsync(async () =>
		{
			Assert.That(result.Error, Is.EqualTo(PluginInstallError.SignerMismatch), result.ErrorMessage);
			Assert.That(result.ErrorMessage, Is.Not.Empty);
			Assert.That(ActiveVersion(), Is.EqualTo("1.0.0"));
			Assert.That(Directory.Exists(PluginInstallPaths.VersionDirectory(_paths.PluginsDirectory,
					PluginId,
					"1.1.0")),
				Is.False);
			Assert.That(await _trustRecords.GetVersion(PluginId, "1.1.0"), Is.Null);
			Assert.That(await _trustRecords.GetVersion(PluginId, "1.0.0"), Is.SameAs(recordBefore));
		});
	}

	[Test]
	public async Task A_forced_reinstall_of_the_same_version_signed_by_another_publisher_is_refused()
	{
		await InstallTrusted();
		var certificateBefore = (await _trustRecords.GetVersion(PluginId, "1.0.0"))!.CertificateId;

		var result = await _installer.Install(PluginArtifactSource.FromPath(await OtherPublisherArtifact()),
			new PluginInstallRequest { Force = true });

		await Assert.MultipleAsync(async () =>
		{
			Assert.That(result.Error, Is.EqualTo(PluginInstallError.SignerMismatch), result.ErrorMessage);
			Assert.That((await _trustRecords.GetVersion(PluginId, "1.0.0"))!.CertificateId,
				Is.EqualTo(certificateBefore));
		});
	}

	[Test]
	public async Task An_update_from_the_same_publisher_under_a_rotated_certificate_installs()
	{
		var installed = await _installer.Install(PluginArtifactSource.FromPath(await TrustedArtifact()),
			new PluginInstallRequest());
		Assert.That(installed.Success, Is.True, installed.ErrorMessage);

		var update = await _installer.Install(PluginArtifactSource.FromPath(await TrustedArtifact("1.1.0")),
			new PluginInstallRequest());

		Assert.Multiple(() =>
		{
			Assert.That(update.Success, Is.True, update.ErrorMessage);
			Assert.That(update.Signature?.CertificateId, Is.Not.EqualTo(installed.Signature?.CertificateId));
			Assert.That(ActiveVersion(), Is.EqualTo("1.1.0"));
		});
	}

	[Test]
	public async Task An_update_signed_through_a_new_issuer_by_the_same_publisher_installs()
	{
		await InstallTrusted();

		var update = await _installer.Install(
			PluginArtifactSource.FromPath(await IssuerSignedArtifact(TestPki.IssueIssuer(), "1.1.0")),
			new PluginInstallRequest());

		Assert.That(update.Success, Is.True, update.ErrorMessage);
	}

	[Test]
	public async Task The_admitted_signer_is_recorded_with_the_trust_record()
	{
		await InstallTrusted();

		var record = await _trustRecords.GetVersion(PluginId, "1.0.0");

		Assert.Multiple(() =>
		{
			Assert.That(record?.SignerKind, Is.EqualTo("creator"));
			Assert.That(record?.SignerId, Is.EqualTo("creator-1"));
		});
	}

	[Test]
	public async Task An_unsigned_test_build_in_between_does_not_release_the_signer_pin()
	{
		await InstallTrusted();
		var testBuild = await InstallFromKind(PluginArtifactSourceKind.TestBuild,
			await UnsignedArtifact("1.1.0"),
			new PluginInstallRequest { AllowUnsigned = true });
		Assert.That(testBuild.Success, Is.True, testBuild.ErrorMessage);

		var result = await _installer.Install(PluginArtifactSource.FromPath(await OtherPublisherArtifact("1.2.0")),
			new PluginInstallRequest());

		Assert.That(result.Error, Is.EqualTo(PluginInstallError.SignerMismatch), result.ErrorMessage);
	}

	[Test]
	public async Task Pruning_the_only_trusted_version_keeps_its_record_and_the_signer_pin()
	{
		await InstallTrusted();
		foreach (var version in new[] { "1.1.0-t1", "1.1.0-t2", "1.1.0-t3" })
		{
			var testBuild = await InstallFromKind(PluginArtifactSourceKind.TestBuild,
				await UnsignedArtifact(version),
				new PluginInstallRequest { AllowUnsigned = true });
			Assert.That(testBuild.Success, Is.True, testBuild.ErrorMessage);
		}

		Assert.That(Directory.Exists(PluginInstallPaths.VersionDirectory(_paths.PluginsDirectory, PluginId, "1.0.0")),
			Is.False,
			"the trusted version was expected to be pruned");

		var result = await _installer.Install(PluginArtifactSource.FromPath(await OtherPublisherArtifact("1.2.0")),
			new PluginInstallRequest());

		await Assert.MultipleAsync(async () =>
		{
			Assert.That(await _trustRecords.GetVersion(PluginId, "1.0.0"), Is.Not.Null);
			Assert.That(result.Error, Is.EqualTo(PluginInstallError.SignerMismatch), result.ErrorMessage);
		});
	}

	[Test]
	public async Task A_first_signed_version_over_unsigned_ones_pins_its_publisher()
	{
		var unsigned = await _installer.Install(PluginArtifactSource.FromPath(await UnsignedArtifact()),
			new PluginInstallRequest { AllowUnsigned = true });
		Assert.That(unsigned.Success, Is.True, unsigned.ErrorMessage);
		await InstallTrusted("1.1.0");

		var result = await _installer.Install(PluginArtifactSource.FromPath(await OtherPublisherArtifact("1.2.0")),
			new PluginInstallRequest());

		Assert.That(result.Error, Is.EqualTo(PluginInstallError.SignerMismatch), result.ErrorMessage);
	}

	[Test]
	public async Task An_unsigned_update_over_an_unsigned_plugin_still_installs_with_consent()
	{
		var first = await _installer.Install(PluginArtifactSource.FromPath(await UnsignedArtifact()),
			new PluginInstallRequest { AllowUnsigned = true });
		Assert.That(first.Success, Is.True, first.ErrorMessage);

		var update = await _installer.Install(PluginArtifactSource.FromPath(await UnsignedArtifact("1.1.0")),
			new PluginInstallRequest { AllowUnsigned = true });

		Assert.That(update.Success, Is.True, update.ErrorMessage);
	}

	[Test]
	public async Task A_record_without_a_stored_signer_admits_only_its_own_certificate()
	{
		var artifact = await TrustedArtifact();
		var installed = await _installer.Install(PluginArtifactSource.FromPath(artifact), new PluginInstallRequest());
		Assert.That(installed.Success, Is.True, installed.ErrorMessage);
		var record = (await _trustRecords.GetVersion(PluginId, "1.0.0"))!;
		record.SignerKind = null;
		record.SignerId = null;

		var rotated = await _installer.Install(PluginArtifactSource.FromPath(await TrustedArtifact("1.1.0")),
			new PluginInstallRequest());
		var sameCertificate = await _installer.Install(PluginArtifactSource.FromPath(artifact),
			new PluginInstallRequest { Force = true });

		Assert.Multiple(() =>
		{
			Assert.That(rotated.Error, Is.EqualTo(PluginInstallError.SignerMismatch), rotated.ErrorMessage);
			Assert.That(sameCertificate.Success, Is.True, sameCertificate.ErrorMessage);
		});
	}

	[Test]
	public async Task S49_force_does_not_imply_unsigned_consent()
	{
		var artifact = await UnsignedArtifact();

		var result = await _installer.Install(PluginArtifactSource.FromPath(artifact),
			new PluginInstallRequest { Force = true });

		Assert.That(result.Error, Is.EqualTo(PluginInstallError.UnsignedNotPermitted));
	}

	private void LoadRegistry(params string[] revokedKeyIds)
	{
		_storeCatalog.Swap(new StoreCatalogSnapshot { Sequence = 1, Entries = [], RevokedKeyIds = revokedKeyIds });
	}

	private Task<string> IssuerSignedArtifact(TestPki.IssuedCertificate issuer, string version = "1.0.0")
	{
		return SignedPluginArtifacts.CreateSignedAsync(_sourceDirectory,
			PluginId,
			version,
			fileName: $"issuer-signed-{version}-{Guid.NewGuid():N}.macroDeckPlugin",
			issuer: issuer);
	}

	[Test]
	public async Task A_plugin_signed_through_an_issuer_installs_as_trusted()
	{
		LoadRegistry();

		var install = await _installer.Install(
			PluginArtifactSource.FromPath(await IssuerSignedArtifact(TestPki.IssueIssuer())),
			new PluginInstallRequest());

		Assert.Multiple(() =>
		{
			Assert.That(install.Success, Is.True, install.ErrorMessage);
			Assert.That(install.Signature?.Verdict, Is.EqualTo(PluginTrustVerdict.Trusted));
			Assert.That(install.Signature?.Revocation, Is.EqualTo(PluginRevocationStatus.NotRevoked));
		});
	}

	[Test]
	public async Task A_plugin_whose_issuer_the_registry_revoked_is_refused_at_install()
	{
		var issuer = TestPki.IssueIssuer();
		LoadRegistry(issuer.CertificateId);

		var install = await _installer.Install(PluginArtifactSource.FromPath(await IssuerSignedArtifact(issuer)),
			new PluginInstallRequest());

		Assert.Multiple(() =>
		{
			Assert.That(install.Success, Is.False);
			Assert.That(install.Error, Is.EqualTo(PluginInstallError.SignatureRevoked), install.ErrorMessage);
			Assert.That(_catalog.Discover(), Is.Empty);
		});
	}

	[Test]
	public async Task A_plugin_whose_certificate_the_registry_revoked_is_refused_at_install()
	{
		var artifact = await TrustedArtifact();
		using var certificate = global::System.Text.Json.JsonDocument.Parse(
			PackageArchiveFixtures.ReadEntryText(artifact, PluginArtifactFiles.CertificateFileName));
		LoadRegistry(certificate.RootElement.GetProperty("certificateId").GetString()!);

		var install = await _installer.Install(PluginArtifactSource.FromPath(artifact), new PluginInstallRequest());

		Assert.That(install.Error, Is.EqualTo(PluginInstallError.SignatureRevoked), install.ErrorMessage);
	}

	[Test]
	public async Task An_update_signed_under_a_revoked_issuer_is_refused_as_revoked_and_keeps_the_installed_version()
	{
		LoadRegistry();
		var installed = await _installer.Install(PluginArtifactSource.FromPath(await TrustedArtifact()),
			new PluginInstallRequest());
		Assert.That(installed.Success, Is.True, installed.ErrorMessage);
		var issuer = TestPki.IssueIssuer();
		LoadRegistry(issuer.CertificateId);

		var update = await _installer.Install(
			PluginArtifactSource.FromPath(await IssuerSignedArtifact(issuer, "1.1.0")),
			new PluginInstallRequest());

		Assert.Multiple(() =>
		{
			Assert.That(update.Error, Is.EqualTo(PluginInstallError.SignatureRevoked), update.ErrorMessage);
			Assert.That(_catalog.Discover().Single().ActiveVersion!.Version, Is.EqualTo("1.0.0"));
		});
	}

	[Test]
	public async Task An_installed_plugin_whose_issuer_is_revoked_later_still_activates()
	{
		var issuer = TestPki.IssueIssuer();
		LoadRegistry();
		var installed = await _installer.Install(PluginArtifactSource.FromPath(await IssuerSignedArtifact(issuer)),
			new PluginInstallRequest());
		Assert.That(installed.Success, Is.True, installed.ErrorMessage);
		LoadRegistry(issuer.CertificateId);

		var activation = await _installer.Activate(PluginId, "1.0.0");

		Assert.That(activation.Success, Is.True, activation.ErrorMessage);
	}

	[Test]
	public async Task Without_a_loaded_registry_revocation_cannot_be_checked_and_does_not_block_install()
	{
		var install = await _installer.Install(
			PluginArtifactSource.FromPath(await IssuerSignedArtifact(TestPki.IssueIssuer())),
			new PluginInstallRequest());

		Assert.Multiple(() =>
		{
			Assert.That(install.Success, Is.True, install.ErrorMessage);
			Assert.That(install.Signature?.Revocation, Is.EqualTo(PluginRevocationStatus.Unavailable));
		});
	}

}
