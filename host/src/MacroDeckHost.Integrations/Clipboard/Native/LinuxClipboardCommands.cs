using System.Globalization;
using System.Text;

namespace MacroDeckHost.Integrations.Clipboard.Native;

internal static class LinuxClipboardCommands
{
	public const string WaylandPackage = "wl-clipboard";
	public const string X11Package = "xclip";
	public const string UriListType = "text/uri-list";

	private const string WlCopy = "wl-copy";
	private const string WlPaste = "wl-paste";
	private const string Xclip = "xclip";

	private static readonly string[] _xclipSelection = ["-selection", "clipboard"];

	private static readonly HashSet<string> _textTargets
		= new(StringComparer.Ordinal) { "UTF8_STRING", "STRING", "TEXT" };

	private static readonly HashSet<string> _fileTargets
		= new(StringComparer.Ordinal) { UriListType, "x-special/gnome-copied-files" };

	private static readonly HashSet<string> _metaTargets = new(StringComparer.Ordinal)
	{
		"TARGETS",
		"TIMESTAMP",
		"MULTIPLE",
		"SAVE_TARGETS",
		"DELETE",
		"INSERT_SELECTION"
	};

	private static readonly string[] _noContentMarkers =
		["nothing is copied", "no selection", "not available", "no suitable type", "no owner"];

	private static readonly Dictionary<string, string> _imageTypes = new(StringComparer.OrdinalIgnoreCase)
	{
		[".png"] = "image/png",
		[".jpg"] = "image/jpeg",
		[".jpeg"] = "image/jpeg",
		[".gif"] = "image/gif",
		[".bmp"] = "image/bmp",
		[".tif"] = "image/tiff",
		[".tiff"] = "image/tiff"
	};

	public static ClipboardAvailability Probe(bool wayland, Func<string, bool> commandExists)
	{
		string[] tools = wayland ? [WlCopy, WlPaste] : [Xclip];
		return tools.All(commandExists)
			? ClipboardAvailability.Available
			: ClipboardAvailability.Unavailable(ClipboardUnavailableReason.ToolMissing,
				wayland ? WaylandPackage : X11Package);
	}

	public static ClipboardCommand ListTypes(bool wayland)
	{
		return wayland
			? new ClipboardCommand(WlPaste, ["--list-types"])
			: new ClipboardCommand(Xclip, [.. _xclipSelection, "-t", "TARGETS", "-o"]);
	}

	public static ClipboardCommand ReadText(bool wayland)
	{
		return wayland
			? new ClipboardCommand(WlPaste, ["--no-newline", "--type", "text"])
			: new ClipboardCommand(Xclip, [.. _xclipSelection, "-o"]);
	}

	public static ClipboardCommand Copy(bool wayland, string? type)
	{
		if (wayland)
		{
			return new ClipboardCommand(WlCopy, ["--type", type ?? "text/plain;charset=utf-8"]);
		}

		return type is null
			? new ClipboardCommand(Xclip, [.. _xclipSelection, "-i"])
			: new ClipboardCommand(Xclip, [.. _xclipSelection, "-t", type, "-i"]);
	}

	// wl-copy --clear succeeds without clearing on compositors without data-control, so both tools
	// clear by copying an empty text instead.
	public static ClipboardCommand Clear(bool wayland)
	{
		return Copy(wayland, null);
	}

	public static ClipboardCommand Watch()
	{
		return new ClipboardCommand(WlPaste, ["--watch", "echo"]);
	}

	public static ClipboardContentKinds ParseKinds(string listing)
	{
		var kinds = ClipboardContentKinds.None;
		var other = false;
		foreach (var line in listing.Split('\n'))
		{
			var type = line.Trim();
			if (type.Length == 0 || _metaTargets.Contains(type))
			{
				continue;
			}

			if (type.StartsWith("text/plain", StringComparison.OrdinalIgnoreCase) || _textTargets.Contains(type))
			{
				kinds |= ClipboardContentKinds.Text;
			}
			else if (type.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
			{
				kinds |= ClipboardContentKinds.Image;
			}
			else if (_fileTargets.Contains(type))
			{
				kinds |= ClipboardContentKinds.Files;
			}
			else
			{
				other = true;
			}
		}

		return kinds == ClipboardContentKinds.None && other ? ClipboardContentKinds.Other : kinds;
	}

	// Clearing copies an empty text, whose selection still offers text targets, which is no content.
	public static ClipboardContentKinds KindsAfterTextProbe(ClipboardContentKinds kinds, ClipboardProcessResult probe)
	{
		if (kinds != ClipboardContentKinds.Text || probe.IsTruncated)
		{
			return kinds;
		}

		var empty = probe.ExitCode == 0 ? probe.Output.Length == 0 : MeansNoContent(probe.Error);
		return empty ? ClipboardContentKinds.None : kinds;
	}

	public static bool MeansNoContent(string error)
	{
		return _noContentMarkers.Any(marker => error.Contains(marker, StringComparison.OrdinalIgnoreCase));
	}

	public static string? ImageType(string path)
	{
		return _imageTypes.GetValueOrDefault(Path.GetExtension(path));
	}

	public static bool HasImageSignature(string type, ReadOnlySpan<byte> header)
	{
		return type switch
		{
			"image/png" => header.StartsWith((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]),
			"image/jpeg" => header.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xD8, 0xFF]),
			"image/gif" => header.StartsWith("GIF87a"u8) || header.StartsWith("GIF89a"u8),
			"image/bmp" => header.StartsWith("BM"u8),
			"image/tiff" => header.StartsWith((ReadOnlySpan<byte>)[0x49, 0x49, 0x2A, 0x00])
				|| header.StartsWith((ReadOnlySpan<byte>)[0x4D, 0x4D, 0x00, 0x2A]),
			_ => false
		};
	}

	public static string BuildUriList(IReadOnlyList<string> paths)
	{
		return string.Join("\r\n", paths.Select(FileUri)) + "\r\n";
	}

	public static string FileUri(string path)
	{
		var builder = new StringBuilder("file://");
		foreach (var value in Encoding.UTF8.GetBytes(path))
		{
			if (IsUnreserved(value))
			{
				builder.Append((char)value);
			}
			else
			{
				builder.Append('%').Append(value.ToString("X2", CultureInfo.InvariantCulture));
			}
		}

		return builder.ToString();
	}

	private static bool IsUnreserved(byte value)
	{
		return value is >= (byte)'A' and <= (byte)'Z'
			or >= (byte)'a' and <= (byte)'z'
			or >= (byte)'0' and <= (byte)'9'
			or (byte)'-'
			or (byte)'.'
			or (byte)'_'
			or (byte)'~'
			or (byte)'/';
	}
}
