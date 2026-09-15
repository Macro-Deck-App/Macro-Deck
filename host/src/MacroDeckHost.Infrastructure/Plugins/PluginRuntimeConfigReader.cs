using System.Text.Json;
using MacroDeckHost.Application.Plugins.Runtime;

namespace MacroDeckHost.Infrastructure.Plugins;

public static class PluginRuntimeConfigReader
{
	public static IReadOnlyList<DotnetFrameworkRequirement>? TryRead(string entrypointAssemblyPath)
	{
		var path = Path.ChangeExtension(entrypointAssemblyPath, ".runtimeconfig.json");

		try
		{
			using var stream = File.OpenRead(path);
			using var document = JsonDocument.Parse(stream,
				new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
			return Parse(document.RootElement);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
		{
			return null;
		}
	}

	private static List<DotnetFrameworkRequirement>? Parse(JsonElement root)
	{
		if (root.ValueKind != JsonValueKind.Object ||
			!root.TryGetProperty("runtimeOptions", out var options) ||
			options.ValueKind != JsonValueKind.Object)
		{
			return null;
		}

		var rollForward = ReadRollForward(options) ?? DotnetRollForward.Minor;
		var requirements = new List<DotnetFrameworkRequirement>();

		if (options.TryGetProperty("framework", out var single) && ReadFramework(single, rollForward) is { } framework)
		{
			requirements.Add(framework);
		}

		if (options.TryGetProperty("frameworks", out var many) && many.ValueKind == JsonValueKind.Array)
		{
			foreach (var element in many.EnumerateArray())
			{
				if (ReadFramework(element, rollForward) is { } entry)
				{
					requirements.Add(entry);
				}
			}
		}

		return requirements.Count == 0 ? null : requirements;
	}

	private static DotnetFrameworkRequirement? ReadFramework(JsonElement element, DotnetRollForward inherited)
	{
		if (element.ValueKind != JsonValueKind.Object ||
			!element.TryGetProperty("name", out var name) ||
			name.ValueKind != JsonValueKind.String ||
			string.IsNullOrWhiteSpace(name.GetString()) ||
			!element.TryGetProperty("version", out var version) ||
			version.ValueKind != JsonValueKind.String ||
			DotnetMuxerLocator.ParseVersion(version.GetString()!) is not { } parsed)
		{
			return null;
		}

		return new DotnetFrameworkRequirement
		{
			Name = name.GetString()!,
			Version = parsed,
			RollForward = ReadRollForward(element) ?? inherited
		};
	}

	private static DotnetRollForward? ReadRollForward(JsonElement element)
	{
		return element.TryGetProperty("rollForward", out var value) &&
			value.ValueKind == JsonValueKind.String &&
			Enum.TryParse<DotnetRollForward>(value.GetString(), ignoreCase: true, out var parsed)
				? parsed
				: null;
	}
}
