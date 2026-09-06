using System.IO.Compression;
using System.Runtime.InteropServices;

namespace MacroDeck.Plugin.Testing.Tests.ConformanceTests.Support;

/// <summary>
/// Hand-builds a minimal <c>.macroDeckPlugin</c> artifact from an already-built framework-dependent
/// executable's own output directory, for the one test that needs an artifact subject (see B10). No
/// production packer exists in <c>MacroDeck.Plugin.Packaging</c> to reuse - see that project's own remarks
/// on why <c>host/tests/.../PluginArtifactBuilder.cs</c> is deliberately hand-rolled too, for the same
/// reason: an artifact built by the code under test would only ever prove the reader agrees with itself.
/// The manifest here is a plain string, and the archive is built with a raw <see cref="ZipArchive" />.
/// </summary>
internal static class ConformanceArtifactBuilder
{
	/// <summary>
	/// Packs everything under <paramref name="buildDirectory" /> into a fresh <c>.macroDeckPlugin</c> file in
	/// a fresh temporary directory, with a <c>manifest.json</c> declaring <paramref name="entrypointDll" /> as
	/// this machine's own runtime identifier's framework-dependent entrypoint. Returns the artifact's full path.
	/// </summary>
	public static string BuildFrameworkDependentArtifact(
		string buildDirectory,
		string entrypointDll,
		string pluginId,
		string pluginName,
		string version)
	{
		ArgumentException.ThrowIfNullOrEmpty(buildDirectory);
		ArgumentException.ThrowIfNullOrEmpty(entrypointDll);
		ArgumentException.ThrowIfNullOrEmpty(pluginId);
		ArgumentException.ThrowIfNullOrEmpty(pluginName);
		ArgumentException.ThrowIfNullOrEmpty(version);

		// The same source PluginRuntimeIdentifiers.Current reads from, so the manifest key this builds
		// always matches what PluginLaunchSpec.ForManifest looks up on the machine actually running the test.
		var runtimeIdentifier = RuntimeInformation.RuntimeIdentifier;

		var manifestJson = $$"""
							 {
							   "manifestVersion": 1,
							   "id": "{{pluginId}}",
							   "name": "{{pluginName}}",
							   "version": "{{version}}",
							   "entrypoints": {
							     "{{runtimeIdentifier}}": {
							       "executable": "{{entrypointDll}}",
							       "runtime": { "kind": "FrameworkDependent", "dotnetVersion": "10.0" }
							     }
							   }
							 }
							 """;

		var targetDirectory = Directory.CreateTempSubdirectory("macrodeck-conformance-artifact-").FullName;
		var artifactPath = Path.Combine(targetDirectory, "plugin.macroDeckPlugin");

		using (var archive = ZipFile.Open(artifactPath, ZipArchiveMode.Create))
		{
			var manifestEntry = archive.CreateEntry("manifest.json");

			using (var writer = new StreamWriter(manifestEntry.Open()))
			{
				writer.Write(manifestJson);
			}

			foreach (var file in Directory.EnumerateFiles(buildDirectory, "*", SearchOption.AllDirectories))
			{
				var relativePath = Path.GetRelativePath(buildDirectory, file).Replace(Path.DirectorySeparatorChar, '/');

				// The build output now ships its own real manifest.json (#522) - skip it rather than
				// collide with the one just written above, which declares the id/name/version this
				// method was actually asked to build an artifact for.
				if (string.Equals(relativePath, "manifest.json", StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}

				archive.CreateEntryFromFile(file, relativePath);
			}
		}

		return artifactPath;
	}
}
