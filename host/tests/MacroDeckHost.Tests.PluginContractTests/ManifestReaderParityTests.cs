using MacroDeck.Plugin.Hosting;
using MacroDeck.Plugin.Packaging.Manifest;

namespace MacroDeckHost.Tests.PluginContractTests;

[TestFixture]
internal sealed class ManifestReaderParityTests
{
	private string _contentRoot = string.Empty;

	[SetUp]
	public void SetUp() => _contentRoot = Directory.CreateTempSubdirectory("macro-deck-manifest-parity").FullName;

	[TearDown]
	public void TearDown()
	{
		if (Directory.Exists(_contentRoot))
		{
			Directory.Delete(_contentRoot, recursive: true);
		}
	}

	private string ManifestPath => Path.Combine(_contentRoot, PluginManifestFileReader.FileName);

	[Test]
	public void Both_readers_agree_on_the_identity_fields_of_the_same_manifest()
	{
		File.WriteAllText(Path.Combine(_contentRoot, "Parity.exe"), "stub");
		File.WriteAllText(Path.Combine(_contentRoot, "Parity"), "stub");

		File.WriteAllText(ManifestPath,
			"""
			{
			  "manifestVersion": 1,
			  "id": "com.example.parity",
			  "name": "Parity Plugin",
			  "version": "1.2.3",
			  "description": "Exercises every host-side field plus an unknown one.",
			  "icon": "assets/icon.png",
			  "entrypoints": {
			    "win-x64": { "executable": "Parity.exe" },
			    "osx-arm64": { "executable": "Parity" },
			    "linux-x64": { "executable": "Parity" }
			  },
			  "permissions": ["host:variables"],
			  "files": [
			    {
			      "path": "Parity.exe",
			      "sha256": "sha256:aa00000000000000000000000000000000000000000000000000000000000000",
			      "size": 100
			    }
			  ],
			  "signature": {
			    "algorithm": "ed25519",
			    "keyId": "key-1",
			    "value": "AAAA"
			  },
			  "compatibility": {
			    "macroDeck": ">=3.0.0"
			  },
			  "publisher": {
			    "name": "Example Publisher"
			  },
			  "somethingFromTheFuture": 123
			}
			""");

		var hostResult = new PluginManifestReader().Read(ManifestPath, "com.example.parity", "1.2.3");
		Assert.That(hostResult.Success, Is.True, hostResult.ErrorMessage);

		var problems = new List<string>();
		var sdkManifest = PluginManifestFileReader.Read(_contentRoot, problems);

		Assert.That(problems, Is.Empty, string.Join("; ", problems));
		Assert.That(sdkManifest, Is.Not.Null);

		Assert.Multiple(() =>
		{
			Assert.That(sdkManifest!.Id, Is.EqualTo(hostResult.Manifest!.Id));
			Assert.That(sdkManifest.Name, Is.EqualTo(hostResult.Manifest.Name));
			Assert.That(sdkManifest.Version, Is.EqualTo(hostResult.Manifest.Version));
			Assert.That(sdkManifest.Description, Is.EqualTo(hostResult.Manifest.Description));
			Assert.That(sdkManifest.Icon, Is.EqualTo(hostResult.Manifest.Icon));
		});
	}

	[Test]
	public void Both_readers_accept_a_manifest_whose_files_section_exceeds_64_KiB()
	{
		File.WriteAllText(Path.Combine(_contentRoot, "Parity.exe"), "stub");
		File.WriteAllText(Path.Combine(_contentRoot, "Parity"), "stub");

		// What a self-contained multi-RID plugin's recomputed files[] looks like (issue #751): far past the
		// 64 KiB a manifest used to be allowed. Both readers gate on their own copy of the limit -
		// MacroDeck.Plugin.Hosting cannot reference PluginArtifactLimits - so only a test crossing the old
		// bound catches one of them being left behind, which would make a packable artifact unreadable on
		// one side.
		var files = string.Join(",\n",
			Enumerable.Range(0, 600)
				.Select(index => $$"""
								       {
								         "path": "runtimes/linux-x64/System.Runtime.Fixture.Part{{index:D4}}.dll",
								         "sha256": "sha256:aa00000000000000000000000000000000000000000000000000000000000000",
								         "size": 4096
								       }
								   """));

		File.WriteAllText(ManifestPath,
			$$"""
			  {
			    "manifestVersion": 1,
			    "id": "com.example.parity",
			    "name": "Parity Plugin",
			    "version": "1.2.3",
			    "entrypoints": {
			      "win-x64": { "executable": "Parity.exe" },
			      "osx-arm64": { "executable": "Parity" },
			      "linux-x64": { "executable": "Parity" }
			    },
			    "files": [
			  {{files}}
			    ]
			  }
			  """);

		Assert.That(new FileInfo(ManifestPath).Length, Is.GreaterThan(64 * 1024));

		var hostResult = new PluginManifestReader().Read(ManifestPath, "com.example.parity", "1.2.3");

		var problems = new List<string>();
		var sdkManifest = PluginManifestFileReader.Read(_contentRoot, problems);

		Assert.Multiple(() =>
		{
			Assert.That(hostResult.Success, Is.True, hostResult.ErrorMessage);
			Assert.That(sdkManifest, Is.Not.Null);
			Assert.That(problems, Is.Empty, string.Join("; ", problems));
		});
	}

	[TestCase("../x.svg")]
	[TestCase("/x.svg")]
	[TestCase("a\\b.svg")]
	[TestCase("")]
	public void Both_readers_reject_the_same_unsafe_icon_paths(string icon)
	{
		var hostManifestPath = Path.Combine(_contentRoot, "host-" + PluginManifestFileReader.FileName);
		File.WriteAllText(hostManifestPath,
			$$"""
			  {
			    "manifestVersion": 1,
			    "id": "com.example.parity",
			    "name": "Parity Plugin",
			    "version": "1.2.3",
			    "icon": {{System.Text.Json.JsonSerializer.Serialize(icon)}},
			    "entrypoints": { "win-x64": { "executable": "Parity.exe" } }
			  }
			  """);

		var hostResult = new PluginManifestReader().Read(hostManifestPath, "com.example.parity", "1.2.3");

		File.WriteAllText(ManifestPath,
			$$"""
			  {
			    "manifestVersion": 1,
			    "id": "com.example.parity",
			    "name": "Parity Plugin",
			    "version": "1.2.3",
			    "icon": {{System.Text.Json.JsonSerializer.Serialize(icon)}}
			  }
			  """);

		var problems = new List<string>();
		var sdkManifest = PluginManifestFileReader.Read(_contentRoot, problems);
		var sdkIconIsSafe = sdkManifest?.Icon is { } sdkIcon &&
			PluginManifestFileReader.IsSafeRelativeIconPath(sdkIcon);

		Assert.Multiple(() =>
		{
			Assert.That(hostResult.Success, Is.False, "the host reader should reject this icon path");
			Assert.That(sdkIconIsSafe, Is.False, "the SDK reader should reject this icon path");
		});
	}
}
