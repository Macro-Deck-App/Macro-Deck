using MacroDeck.Plugin.Cli.Manifests;
using MacroDeck.Plugin.Packaging.Manifest;

namespace MacroDeck.Plugin.Cli.Tests.UnitTests;

/// <summary>
/// <see cref="EntrypointPresence" /> as a pure function of a manifest and a present-entry-name set - the
/// check issue #556's P1 finding asks <c>pack</c> and <c>inspect</c> to share, since
/// <c>IPluginManifestReader</c> only ever existence-checks the entrypoint for the current runtime
/// identifier. No file ever touches disk here; the "present" set is handed in directly.
/// </summary>
[TestFixture]
public class EntrypointPresenceTests
{
	private static PluginManifest Manifest(params (string Rid, string Executable)[] entrypoints)
	{
		return new PluginManifest
		{
			ManifestVersion = 1,
			Id = "com.example.test-plugin",
			Name = "Test Plugin",
			Version = "1.0.0",
			Entrypoints = entrypoints.ToDictionary(e => e.Rid, e => new PluginEntrypoint { Executable = e.Executable })
		};
	}

	[Test]
	public void An_entrypoint_whose_file_is_absent_is_reported_missing()
	{
		var manifest = Manifest(("win-x64", "app.exe"),
			("osx-arm64", "app"),
			("linux-x64", "app"));

		// Only osx-arm64's file is actually present.
		var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "app" };

		var missing = EntrypointPresence.Missing(manifest, present, "artifact");

		Assert.Multiple(() =>
		{
			Assert.That(missing, Has.Count.EqualTo(1));
			Assert.That(missing[0].Message, Does.Contain("win-x64"));
		});
	}

	[Test]
	public void Every_entrypoint_present_reports_nothing()
	{
		var manifest = Manifest(("win-x64", "app.exe"),
			("osx-arm64", "app"),
			("linux-x64", "app"));

		var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "app.exe", "app" };

		var missing = EntrypointPresence.Missing(manifest, present, "artifact");

		Assert.That(missing, Is.Empty);
	}

	[Test]
	public void A_backslash_declared_entrypoint_matches_a_forward_slash_entry()
	{
		// A manifest-declared executable is carried verbatim from JSON (never rewritten - see
		// EntrypointPresence.Normalize's own remarks), so a Windows-style separator really can arrive here
		// and still has to match an archive entry recorded with '/'. A naive string-equality comparison
		// would report this as missing on every platform, macOS and Linux CI included.
		var manifest = Manifest(("win-x64", @"sub\plugin"));

		var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "sub/plugin" };

		var missing = EntrypointPresence.Missing(manifest, present, "artifact");

		Assert.That(missing, Is.Empty);
	}
}
