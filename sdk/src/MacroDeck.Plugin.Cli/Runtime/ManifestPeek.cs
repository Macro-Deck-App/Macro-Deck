using System.Text.Json;
using MacroDeck.Plugin.Packaging.Artifacts;

namespace MacroDeck.Plugin.Cli.Runtime;

/// <summary>The result of a best-effort <see cref="ManifestPeek" /> read: <c>null</c> members mean the
/// manifest was absent, unreadable, or did not declare that value - never that reading it failed in a way
/// the caller needs to react to.</summary>
internal readonly record struct ManifestPeekResult(string? Id, TimeSpan? GracePeriod);

/// <summary>
/// Best-effort peek at whatever <c>manifest.json</c> sits next to a resolved launch target, reading its
/// <c>id</c> and <c>shutdown.gracefulTimeoutSeconds</c> in one pass. Not a validating read -
/// <c>PluginHostBuilder.Build</c> inside the launched process (managed id agreement) and <c>validate</c>
/// (schema/permission conformance) are the real authorities; this only picks values <c>run</c> needs before
/// the process starts.
/// </summary>
internal static class ManifestPeek
{
	public static ManifestPeekResult Read(string workingDirectory)
	{
		var manifestPath = Path.Combine(workingDirectory, PluginArtifactFiles.ManifestFileName);

		if (!File.Exists(manifestPath))
		{
			return new ManifestPeekResult(null, null);
		}

		try
		{
			using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
			var root = document.RootElement;

			// A manifest whose root is not an object is still parseable JSON, and every lookup below would
			// throw InvalidOperationException on it. That is validate's verdict to give, not a reason for run
			// to fail before it has launched anything.
			if (root.ValueKind != JsonValueKind.Object)
			{
				return new ManifestPeekResult(null, null);
			}

			// Case-insensitive, unlike MacroDeckTestHost.TryPeekManifestId's exact-case lookup: that peek
			// only ever chooses a synthetic dev id for a stub host it also owns, while this id can end up
			// compared, ordinally, against whatever casing a real manifest reader accepts - so it deliberately
			// matches ManifestValidator.ProbeIdAndVersion's tolerant lookup instead.
			var id = GetStringPropertyCaseInsensitive(root, "id");

			TimeSpan? grace = null;

			// Every kind is checked before it is read: TryGetProperty and TryGetInt32 throw rather than return
			// false when the element is the wrong kind, and a manifest shaped like `"shutdown": null` must
			// leave this peek with nothing to say rather than take run down before it launches anything.
			if (root.TryGetProperty("shutdown", out var shutdown) &&
				shutdown.ValueKind == JsonValueKind.Object &&
				shutdown.TryGetProperty("gracefulTimeoutSeconds", out var seconds) &&
				seconds.ValueKind == JsonValueKind.Number &&
				seconds.TryGetInt32(out var value))
			{
				grace = TimeSpan.FromSeconds(Math.Clamp(value, 1, 60));
			}

			return new ManifestPeekResult(id, grace);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
		{
			return new ManifestPeekResult(null, null);
		}
	}

	private static string? GetStringPropertyCaseInsensitive(JsonElement element, string name)
	{
		foreach (var property in element.EnumerateObject())
		{
			if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase) &&
				property.Value.ValueKind == JsonValueKind.String)
			{
				return property.Value.GetString();
			}
		}

		return null;
	}
}
