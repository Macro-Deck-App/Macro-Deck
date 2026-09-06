using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MacroDeck.Plugin.Cli.Scaffolding;

/// <summary>
/// Rewrites the template's generated <c>manifest.json</c> with the values <c>new</c> collected -
/// description, publisher, license, repository, homepage and entrypoints - leaving everything else the
/// template wrote (<c>$schema</c>, <c>id</c>, <c>name</c>, <c>version</c>, <c>icon</c>, ...) untouched and
/// in place. Works over <see cref="JsonNode" /> rather than a typed model so document order and every
/// unmodified property survive exactly as the template wrote them.
/// </summary>
internal static class ScaffoldManifestWriter
{
	private static readonly JsonSerializerOptions _writeOptions =
		new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

	public static string BuildManifestJson(string existingJson, PluginScaffoldRequest request)
	{
		var root = JsonNode.Parse(existingJson)?.AsObject() ??
			throw new InvalidOperationException("The generated manifest.json did not parse as a JSON object.");

		root["description"] = request.Description;
		root["publisher"] = new JsonObject { ["name"] = request.Publisher };
		root["license"] = request.License;

		SetIfPresent(root, "repository", request.Repository);
		SetIfPresent(root, "homepage", request.Homepage);

		// An open-ended lower bound, so no future host version is locked out. Deliberately not
		// 'compatibility.protocol', which would pin {minimum:1,maximum:1} and get the plugin rejected by a
		// future protocol-2 host.
		root["compatibility"] = new JsonObject { ["macroDeck"] = ">=3.0.0" };

		root["entrypoints"] = BuildEntrypoints(request);

		return root.ToJsonString(_writeOptions);
	}

	private static void SetIfPresent(JsonObject root, string property, string? value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			root.Remove(property);
			return;
		}

		root[property] = value;
	}

	private static JsonObject BuildEntrypoints(PluginScaffoldRequest request)
	{
		var entrypoints = new JsonObject();

		foreach (var rid in PluginScaffoldDefaults.KnownPlatforms)
		{
			if (!request.Platforms.Contains(rid, StringComparer.Ordinal))
			{
				continue;
			}

			// No 'runtime' property: absent means self-contained, and the schema then forbids a '.dll'
			// executable for a self-contained entrypoint - see
			// PluginManifestReader.ValidateEntrypointRuntimes. The build config this writer's sibling
			// produces always publishes self-contained, so this holds for every platform selected here.
			var executable = rid.StartsWith("win-", StringComparison.Ordinal)
				? $"runtimes/{rid}/{request.ProjectName}.exe"
				: $"runtimes/{rid}/{request.ProjectName}";

			entrypoints[rid] = new JsonObject { ["executable"] = executable };
		}

		return entrypoints;
	}
}
