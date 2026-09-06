using System.Security.Cryptography;
using MacroDeck.Plugin.Packaging.Artifacts;
using MacroDeckHost.Application.Plugins.Installation;
using MacroDeckHost.Infrastructure.Plugins.Installation;
using MacroDeckHost.Tests.UnitTests.TestSupport;

namespace MacroDeckHost.Tests.UnitTests.Plugins.Installation;

[TestFixture]
internal sealed class PluginArtifactAcquirerTests
{
	private TestPaths _paths = null!;
	private string _sourceDirectory = null!;
	private string _stagingDirectory = null!;

	[SetUp]
	public void SetUp()
	{
		_paths = new TestPaths();
		_sourceDirectory = Path.Combine(_paths.BaseDirectory, "sources");
		_stagingDirectory = Path.Combine(_paths.BaseDirectory, "staging");
		Directory.CreateDirectory(_sourceDirectory);
		Directory.CreateDirectory(_stagingDirectory);
	}

	[TearDown]
	public void TearDown() => _paths.Cleanup();

	private static PluginArtifactAcquirer CreateAcquirer()
	{
		return new PluginArtifactAcquirer(new NoHttpClientFactory(),
			PluginInstallerOptions.Default,
			Serilog.Core.Logger.None);
	}

	private string WriteSourceFile(byte[] content, string fileName = "plugin.macroDeckPlugin")
	{
		var path = Path.Combine(_sourceDirectory, fileName);
		File.WriteAllBytes(path, content);
		return path;
	}

	private static string Sha256Of(byte[] content) => "sha256:" + Convert.ToHexStringLower(SHA256.HashData(content));


	[Test]
	public async Task A_local_path_source_is_copied_into_staging_so_verification_and_extraction_read_the_same_bytes()
	{
		var path = WriteSourceFile("binary"u8.ToArray());
		var acquirer = CreateAcquirer();

		var result = await acquirer.Acquire(PluginArtifactSource.FromPath(path), _stagingDirectory);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True, result.ErrorMessage);
			Assert.That(result.HostOwned, Is.True);
			Assert.That(result.SourceKind, Is.EqualTo(PluginArtifactSourceKind.LocalPath));
			Assert.That(result.ArtifactPath, Is.Not.EqualTo(path));
			Assert.That(result.ArtifactPath, Does.StartWith(_stagingDirectory));
			Assert.That(File.Exists(path), Is.True, "the caller's own file must be left untouched");
		});
	}

	[Test]
	public async Task A_local_path_source_pointing_at_a_missing_file_reports_artifact_not_found()
	{
		var missingPath = Path.Combine(_sourceDirectory, "does-not-exist.macroDeckPlugin");
		var acquirer = CreateAcquirer();

		var result = await acquirer.Acquire(PluginArtifactSource.FromPath(missingPath), _stagingDirectory);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(PluginInstallError.ArtifactNotFound));
		});
	}

	[Test]
	public async Task A_local_path_source_without_the_artifact_extension_is_refused()
	{
		var path = WriteSourceFile("binary"u8.ToArray(), "plugin.zip");
		var acquirer = CreateAcquirer();

		var result = await acquirer.Acquire(PluginArtifactSource.FromPath(path), _stagingDirectory);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(PluginInstallError.InvalidArchive));
		});
	}


	[Test]
	public async Task An_upload_source_is_host_owned_and_the_staged_bytes_match_what_was_uploaded()
	{
		var content = "uploaded-bytes"u8.ToArray();
		await using var upload = new MemoryStream(content);
		var acquirer = CreateAcquirer();

		var result = await acquirer.Acquire(PluginArtifactSource.FromUpload(upload), _stagingDirectory);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True, result.ErrorMessage);
			Assert.That(result.HostOwned, Is.True);
			Assert.That(result.ArtifactPath, Is.Not.Null);
			Assert.That(File.ReadAllBytes(result.ArtifactPath!), Is.EqualTo(content));
		});
	}

	[Test]
	public async Task An_upload_whose_content_does_not_match_the_expected_hash_is_rejected()
	{
		var content = "uploaded-bytes"u8.ToArray();
		var wrongExpected = Sha256Of("something-else"u8.ToArray());
		await using var upload = new MemoryStream(content);
		var acquirer = CreateAcquirer();

		var source = new PluginArtifactSource
		{
			Kind = PluginArtifactSourceKind.Upload,
			Content = upload,
			ExpectedSha256 = wrongExpected
		};

		var result = await acquirer.Acquire(source, _stagingDirectory);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(PluginInstallError.HashMismatch));
			Assert.That(Directory.EnumerateFiles(_stagingDirectory),
				Is.Empty,
				"a staged file that failed its hash check must not survive");
		});
	}


	[Test]
	public async Task A_non_https_url_is_refused_without_any_network_call()
	{
		var acquirer = CreateAcquirer();
		var source = PluginArtifactSource.FromUrl(new Uri("http://example.invalid/plugin.macroDeckPlugin"));

		var result = await acquirer.Acquire(source, _stagingDirectory);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(PluginInstallError.InvalidArchive));
		});
	}


	[Test]
	public async Task The_computed_hash_is_the_sha256_prefixed_lowercase_hex_form_of_the_real_content()
	{
		var content = "hash-me"u8.ToArray();
		var expectedHex = Convert.ToHexStringLower(SHA256.HashData(content));
		var path = WriteSourceFile(content);
		var acquirer = CreateAcquirer();

		var result = await acquirer.Acquire(PluginArtifactSource.FromPath(path), _stagingDirectory);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True, result.ErrorMessage);
			Assert.That(result.Sha256, Is.EqualTo("sha256:" + expectedHex));
		});
	}

	private sealed class NoHttpClientFactory : IHttpClientFactory
	{
		public HttpClient CreateClient(string name)
			=> throw new InvalidOperationException("A unit test must not reach the network.");
	}
}
