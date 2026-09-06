using System.Text;
using MacroDeck.Plugin.Packaging.Artifacts;
using MacroDeck.Plugin.Packaging.Manifest;
using MacroDeckHost.Application.Persistence.Repositories;
using MacroDeckHost.Application.Plugins;
using MacroDeckHost.Application.Plugins.Installation;
using MacroDeckHost.Application.Plugins.Trust;
using MacroDeckHost.Infrastructure.Plugins;
using MacroDeckHost.Infrastructure.Plugins.Installation;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Tests.UnitTests.Plugins.Installation;

[TestFixture]
internal sealed class PluginInstallerInspectTests
{
	private const string PluginId = ManifestJson.DefaultPluginId;

	private const string ForeignRid = "linux-loongarch64";

	private TestPaths _paths = null!;
	private PluginInstallationCatalog _catalog = null!;
	private PluginSessionRegistry _sessionRegistry = null!;
	private FakeInstallSupervisor _supervisor = null!;
	private FakeIntegrationRegistrar _integrationRegistrar = null!;
	private string _sourceDirectory = null!;

	[SetUp]
	public void SetUp()
	{
		_paths = new TestPaths();
		Directory.CreateDirectory(_paths.PluginsDirectory);
		Directory.CreateDirectory(_paths.PluginStagingDirectory);
		Directory.CreateDirectory(_paths.PluginCacheDirectory);

		_sourceDirectory = Path.Combine(_paths.BaseDirectory, "artifacts");
		Directory.CreateDirectory(_sourceDirectory);

		_integrationRegistrar = new FakeIntegrationRegistrar();
		_catalog = new PluginInstallationCatalog(_paths, Serilog.Core.Logger.None);
		_sessionRegistry = new PluginSessionRegistry(TimeProvider.System, Serilog.Core.Logger.None);
		_supervisor = new FakeInstallSupervisor(_catalog, _sessionRegistry);
	}

	[TearDown]
	public void TearDown() => _paths.Cleanup();

	[Test]
	public async Task Inspect_reports_an_unsigned_artifact_as_unsigned_with_an_advisory_warning()
	{
		var unsigned = await Inspect(BuildArtifact(), PluginTrustResult.Of(PluginTrustVerdict.Unsigned));

		Assert.Multiple(() =>
		{
			Assert.That(unsigned.Signature?.Verdict, Is.EqualTo(PluginTrustVerdict.Unsigned));
			Assert.That(WarningCodes(unsigned), Does.Contain(PluginDependencyWarningCodes.ArtifactUnsigned));
			Assert.That(WarningCodes(unsigned),
				Does.Not.Contain(PluginDependencyWarningCodes.SignatureUnverified));
		});
	}

	[Test]
	public async Task Inspect_never_blocks_on_an_unsigned_artifact_since_consent_installs_and_starts_it()
	{
		var unsigned = await Inspect(BuildArtifact(), PluginTrustResult.Of(PluginTrustVerdict.Unsigned));

		Assert.That(unsigned.Warnings,
			Has.None.Matches<PluginInstallWarning>(w => w.Severity == PluginInstallWarningSeverity.Blocking));
	}

	[Test]
	public async Task Inspect_reports_an_unverifiable_artifact_apart_from_an_unsigned_one()
	{
		var unverifiable = await Inspect(BuildArtifact(),
			PluginTrustResult.Of(PluginTrustVerdict.VerificationUnavailable, message: "cannot verify"));

		Assert.Multiple(() =>
		{
			Assert.That(unverifiable.Signature?.Verdict, Is.EqualTo(PluginTrustVerdict.VerificationUnavailable));
			Assert.That(WarningCodes(unverifiable),
				Does.Contain(PluginDependencyWarningCodes.SignatureUnverified));
			Assert.That(WarningCodes(unverifiable), Does.Not.Contain(PluginDependencyWarningCodes.ArtifactUnsigned));
		});
	}

	[Test]
	public async Task Inspect_reports_valid_only_when_the_evaluator_trusted_the_signature()
	{
		var artifact = BuildArtifact();

		var trusted = await Inspect(artifact,
			PluginTrustResult.Of(PluginTrustVerdict.Trusted, "cert-1", "Signed by the Macro Deck Creator Portal."));

		Assert.Multiple(() =>
		{
			Assert.That(trusted.Signature?.Verdict, Is.EqualTo(PluginTrustVerdict.Trusted));
			Assert.That(trusted.Signature?.Message, Is.EqualTo("Signed by the Macro Deck Creator Portal."));
			Assert.That(WarningCodes(trusted),
				Does.Not.Contain(PluginDependencyWarningCodes.SignatureUnverified));
			Assert.That(WarningCodes(trusted),
				Does.Not.Contain(PluginDependencyWarningCodes.ArtifactUnsigned));
		});
	}

	[Test]
	public async Task Inspect_reports_a_rejected_signature_as_invalid_rather_than_unsigned()
	{
		var artifact = BuildArtifact();

		var result = await Inspect(artifact, PluginTrustResult.Of(PluginTrustVerdict.SignatureInvalid));

		Assert.Multiple(() =>
		{
			Assert.That(result.Signature?.Verdict, Is.EqualTo(PluginTrustVerdict.SignatureInvalid));
			Assert.That(result.Signature?.Verdict, Is.Not.EqualTo(PluginTrustVerdict.Unsigned));
		});
	}

	[Test]
	public async Task
		Inspect_refuses_an_artifact_whose_files_do_not_match_its_declared_digests_before_evaluating_trust()
	{
		const string shipped = "something else entirely";
		var declared = PluginArtifactBuilder.Sha256Of("the reviewed payload"u8.ToArray());
		var artifact = new PluginArtifactBuilder()
			.WithManifest(ManifestJson.Build("1.0.0",
				PluginId,
				$$"""
				  "files": [
				  	{
				  		"path": "{{ManifestJson.EntrypointExecutable}}",
				  		"sha256": "{{declared}}",
				  		"size": {{shipped.Length}}
				  	}
				  ]
				  """))
			.WithFile(ManifestJson.EntrypointExecutable, shipped)
			.WriteTo(_sourceDirectory);

		var result = await Inspect(artifact, PluginTrustResult.Of(PluginTrustVerdict.Trusted, "cert-1"));

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(PluginInstallError.HashMismatch));
			Assert.That(result.Signature, Is.Null);
		});
	}

	[Test]
	public async Task Inspect_reports_the_declared_publisher_and_none_when_the_manifest_names_none()
	{
		var withPublisher = await Inspect(BuildArtifact(extraManifestBlocks:
			"""
			"publisher": { "name": "Acme Ltd" }
			"""));
		var without = await Inspect(BuildArtifact());

		Assert.Multiple(() =>
		{
			Assert.That(withPublisher.Publisher?.Name, Is.EqualTo("Acme Ltd"));
			Assert.That(without.Publisher, Is.Null);
		});
	}

	[Test]
	public async Task Inspect_reports_what_the_manifest_says_the_plugin_is()
	{
		var result = await Inspect(BuildArtifact(extraManifestBlocks:
			"""
			"description": "Does a thing."
			"""));

		Assert.Multiple(() =>
		{
			Assert.That(result.Name, Is.EqualTo("Test Plugin"));
			Assert.That(result.Description, Is.EqualTo("Does a thing."));
			Assert.That(result.SupportedOnThisPlatform, Is.True);
		});
	}

	[Test]
	public async Task Inspect_reports_an_artifact_with_no_entrypoint_for_this_machine_as_unsupported()
	{
		var artifact = new PluginArtifactBuilder()
			.WithManifest($$"""
							{
								"manifestVersion": 1,
								"id": "{{PluginId}}",
								"name": "Test Plugin",
								"version": "1.0.0",
								"entrypoints": { "{{ForeignRid}}": { "executable": "{{ManifestJson.EntrypointExecutable}}" } }
							}
							""")
			.WithFile(ManifestJson.EntrypointExecutable, "binary")
			.WriteTo(_sourceDirectory, $"foreign-{Guid.NewGuid():N}.macroDeckPlugin");

		var result = await Inspect(artifact);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True, result.ErrorMessage);
			Assert.That(result.SupportedOnThisPlatform, Is.False);
		});
	}

	[Test]
	public async Task Inspect_returns_the_declared_icon_inline()
	{
		const string svg = "<svg xmlns=\"http://www.w3.org/2000/svg\"/>";
		var artifact = new PluginArtifactBuilder()
			.WithManifest(ManifestJson.Build("1.0.0",
				PluginId,
				"""
				"icon": "Assets/icon.svg"
				"""))
			.WithFile(ManifestJson.EntrypointExecutable, "binary")
			.WithFile("Assets/icon.svg", svg)
			.WriteTo(_sourceDirectory, $"icon-{Guid.NewGuid():N}.macroDeckPlugin");

		var result = await Inspect(artifact);

		Assert.That(result.IconDataUri,
			Is.EqualTo($"data:image/svg+xml;base64,{Convert.ToBase64String(Encoding.UTF8.GetBytes(svg))}"));
	}

	[Test]
	public async Task Inspect_refuses_an_icon_path_that_points_outside_the_artifact()
	{
		var secret = Path.Combine(_paths.BaseDirectory, "secret.svg");
		File.WriteAllText(secret, "<svg>the user's own file</svg>");

		var artifact = new PluginArtifactBuilder()
			.WithManifest(ManifestJson.Build("1.0.0",
				PluginId,
				"""
				"icon": "../../../../secret.svg"
				"""))
			.WithFile(ManifestJson.EntrypointExecutable, "binary")
			.WriteTo(_sourceDirectory, $"escape-{Guid.NewGuid():N}.macroDeckPlugin");

		var result = await Inspect(artifact);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(PluginInstallError.ManifestInvalid));
			Assert.That(result.IconDataUri, Is.Null);
		});
	}

	private static IEnumerable<string> WarningCodes(PluginInstallResult result)
		=> result.Warnings.Select(warning => warning.Code);

	private string BuildArtifact(string? extraManifestBlocks = null)
	{
		return new PluginArtifactBuilder()
			.WithManifest(ManifestJson.Build("1.0.0", PluginId, extraManifestBlocks))
			.WithFile(ManifestJson.EntrypointExecutable, "binary")
			.WriteTo(_sourceDirectory, $"plugin-{Guid.NewGuid():N}.macroDeckPlugin");
	}

	private Task<PluginInstallResult> Inspect(string artifactPath, PluginTrustResult? artifactTrust = null)
	{
		return BuildInstaller(artifactTrust ?? PluginTrustResult.Of(PluginTrustVerdict.Unsigned))
			.Inspect(PluginArtifactSource.FromPath(artifactPath));
	}

	private PluginInstaller BuildInstaller(PluginTrustResult artifactTrust)
	{
		var manifestReader = new PluginManifestReader();
		var options = PluginInstallerOptions.Default;

		var services = new ServiceCollection();
		services.AddSingleton<IPluginRegistrationRepository, InMemoryPluginRegistrationRepository>();
		services.AddSingleton<IPluginTrustRecordRepository, InMemoryPluginTrustRecordRepository>();
		services.AddSingleton<IPluginTrustBaseline, FakePluginTrustBaseline>();
		var provider = services.BuildServiceProvider();

		return new PluginInstaller(_paths,
			new PluginArtifactReader(manifestReader, Serilog.Core.Logger.None),
			new PluginArtifactAcquirer(new NoHttpClientFactory(), options, Serilog.Core.Logger.None),
			new PluginArtifactCache(_paths, options, Serilog.Core.Logger.None),
			new FakePluginTrustEvaluator { InstalledResult = artifactTrust },
			new PluginDependencyResolver(_catalog, manifestReader),
			manifestReader,
			_catalog,
			_supervisor,
			_integrationRegistrar,
			_sessionRegistry,
			provider.GetRequiredService<IServiceScopeFactory>(),
			options,
			TimeProvider.System,
			Serilog.Core.Logger.None);
	}

	private sealed class NoHttpClientFactory : IHttpClientFactory
	{
		public HttpClient CreateClient(string name) => throw new NotSupportedException();
	}
}
