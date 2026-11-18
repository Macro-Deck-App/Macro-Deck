using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using MacroDeckHost.Application.Paths;
using MacroDeckHost.Infrastructure.Autostart;

namespace MacroDeckHost.Mcp;

public sealed record ClaudeDesktopExtensionContent(
	string ShellExecutable,
	string Platform,
	Guid TokenId,
	string Version,
	string Description);

public static class ClaudeDesktopExtension
{
	public const string FileName = "macro-deck.mcpb";
	public const string ManifestEntry = "manifest.json";
	public const string IconEntry = "icon.png";
	public const string ManifestVersion = "0.3";
	public const string Name = "macro-deck";
	public const string DisplayName = "Macro Deck";
	public const string BridgeArgument = "mcp";
	public const string TokenIdEnvironmentVariable = "MACRODECK_MCP_TOKEN_ID";
	private const string Homepage = "https://macro-deck.app";
	private const string IconResource = "MacroDeckHost.Mcp.ClaudeDesktopExtensionIcon.png";

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
		return IsAvailable(DataRootEnvironment.Current());
	}

	public static bool IsAvailable(DataRootEnvironment environment)
	{
		return CurrentPlatform() is not null
			&& ShellExecutable() is not null
			&& !MacroDeckDataRootResolver.IsPortable(environment);
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
			["icon"] = IconEntry,
			["server"] = new JsonObject
			{
				["type"] = "binary",
				["entry_point"] = content.ShellExecutable,
				["mcp_config"] = new JsonObject
				{
					["command"] = content.ShellExecutable,
					["args"] = new JsonArray(BridgeArgument),
					["env"] = new JsonObject { [TokenIdEnvironmentVariable] = content.TokenId.ToString("D") }
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
			using (var writer = entry.Open())
			{
				JsonSerializer.Serialize(writer, Manifest(content), _manifestOptions);
			}

			using var icon = typeof(ClaudeDesktopExtension).Assembly.GetManifestResourceStream(IconResource)
				?? throw new InvalidOperationException($"Missing embedded resource {IconResource}");
			using var iconWriter = archive.CreateEntry(IconEntry, CompressionLevel.NoCompression).Open();
			icon.CopyTo(iconWriter);
		}

		return stream.ToArray();
	}
}
