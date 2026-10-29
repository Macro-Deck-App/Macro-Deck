using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using MacroDeckHost.Infrastructure.Autostart;

namespace MacroDeckHost.Mcp;

public sealed record ClaudeDesktopExtensionContent(
	string ShellExecutable,
	string Platform,
	string Token,
	string Version,
	string Description);

public static class ClaudeDesktopExtension
{
	public const string FileName = "macro-deck.mcpb";
	public const string ManifestEntry = "manifest.json";
	public const string ManifestVersion = "0.3";
	public const string Name = "macro-deck";
	public const string DisplayName = "Macro Deck";
	public const string BridgeArgument = "mcp";
	public const string TokenEnvironmentVariable = "MACRODECK_MCP_TOKEN";
	private const string Homepage = "https://macro-deck.app";

	private static readonly JsonSerializerOptions _manifestOptions = new() { WriteIndented = true };

	public static string? ShellExecutable()
	{
		return AutostartService.ResolveShellExecutable();
	}

	public static string? CurrentPlatform()
	{
		if (OperatingSystem.IsMacOS())
		{
			return "darwin";
		}

		return OperatingSystem.IsWindows() ? "win32" : null;
	}

	public static bool IsAvailable()
	{
		return CurrentPlatform() is not null && ShellExecutable() is not null;
	}

	public static string VersionOf(Version? version)
	{
		return version is null
			? "0.0.0"
			: $"{Math.Max(version.Major, 0)}.{Math.Max(version.Minor, 0)}.{Math.Max(version.Build, 0)}";
	}

	public static JsonObject Manifest(ClaudeDesktopExtensionContent content)
	{
		return new JsonObject
		{
			["manifest_version"] = ManifestVersion,
			["name"] = Name,
			["display_name"] = DisplayName,
			["version"] = content.Version,
			["description"] = content.Description,
			["author"] = new JsonObject { ["name"] = DisplayName, ["url"] = Homepage },
			["homepage"] = Homepage,
			["server"] = new JsonObject
			{
				["type"] = "binary",
				["entry_point"] = content.ShellExecutable,
				["mcp_config"] = new JsonObject
				{
					["command"] = content.ShellExecutable,
					["args"] = new JsonArray(BridgeArgument),
					["env"] = new JsonObject { [TokenEnvironmentVariable] = content.Token }
				}
			},
			["compatibility"] = new JsonObject { ["platforms"] = new JsonArray(content.Platform) }
		};
	}

	public static byte[] Build(ClaudeDesktopExtensionContent content)
	{
		using var stream = new MemoryStream();
		using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
		{
			var entry = archive.CreateEntry(ManifestEntry, CompressionLevel.Optimal);
			using var writer = entry.Open();
			JsonSerializer.Serialize(writer, Manifest(content), _manifestOptions);
		}

		return stream.ToArray();
	}
}
