using System.Globalization;

namespace MacroDeckHost.Infrastructure.Icons.AppIcons;

internal static class XdgIconResolver
{
	private const int MaxDepth = 8;

	private const int TierScalable = 3;

	private const int TierSized = 2;

	private const int TierUnsized = 1;

	private const int TierLegacy = 0;

	private const string ScalableDirectoryName = "scalable";

	private static readonly string[] _iconExtensions = [".png", ".svg", ".xpm"];

	private static readonly char[] _directorySeparators = ['/', '\\'];

	public static IReadOnlyList<string> DefaultSearchRoots()
	{
		var roots = new List<string>();
		var seen = new HashSet<string>(StringComparer.Ordinal);
		var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
		var dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
		if (!string.IsNullOrWhiteSpace(dataHome))
		{
			Add(roots, seen, Path.Combine(dataHome, "icons"));
		}
		else if (!string.IsNullOrWhiteSpace(home))
		{
			Add(roots, seen, Path.Combine(home, ".local", "share", "icons"));
		}

		if (!string.IsNullOrWhiteSpace(home))
		{
			Add(roots, seen, Path.Combine(home, ".icons"));
		}

		var dataDirs = Environment.GetEnvironmentVariable("XDG_DATA_DIRS");
		if (!string.IsNullOrWhiteSpace(dataDirs))
		{
			var entries = dataDirs.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
			foreach (var entry in entries)
			{
				Add(roots, seen, Path.Combine(entry, "icons"));
				Add(roots, seen, Path.Combine(entry, "pixmaps"));
			}
		}

		Add(roots, seen, "/usr/share/icons");
		Add(roots, seen, "/usr/share/pixmaps");
		return roots;
	}

	public static string? TryResolve(string iconName, IEnumerable<string> searchRoots)
	{
		if (string.IsNullOrWhiteSpace(iconName))
		{
			return null;
		}

		var name = iconName.Trim();
		if (name.IndexOfAny(_directorySeparators) >= 0)
		{
			return File.Exists(name) ? name : null;
		}

		var visited = new HashSet<string>(StringComparer.Ordinal);
		Candidate? best = null;
		foreach (var root in searchRoots)
		{
			if (!string.IsNullOrWhiteSpace(root))
			{
				Walk(root, name, visited, ref best);
			}
		}

		return best?.FilePath;
	}

	private static void Add(List<string> roots, HashSet<string> seen, string root)
	{
		if (seen.Add(root))
		{
			roots.Add(root);
		}
	}

	private static void Walk(string root, string iconName, HashSet<string> visited, ref Candidate? best)
	{
		var pending = new Stack<(string Directory, int Depth)>();
		pending.Push((root, 0));
		while (pending.Count > 0)
		{
			var (directory, depth) = pending.Pop();
			if (!visited.Add(directory))
			{
				continue;
			}

			foreach (var file in SafeEnumerate(directory, false))
			{
				if (!Matches(file, iconName))
				{
					continue;
				}

				var candidate = Rank(file);
				if (best is null || IsBetter(candidate, best.Value))
				{
					best = candidate;
				}
			}

			if (depth >= MaxDepth)
			{
				continue;
			}

			foreach (var child in SafeEnumerate(directory, true))
			{
				pending.Push((child, depth + 1));
			}
		}
	}

	private static List<string> SafeEnumerate(string directory, bool directories)
	{
		try
		{
			var entries = directories ? Directory.EnumerateDirectories(directory) : Directory.EnumerateFiles(directory);
			return [..entries];
		}
		catch (IOException)
		{
			return [];
		}
		catch (UnauthorizedAccessException)
		{
			return [];
		}
	}

	private static bool Matches(string file, string iconName)
	{
		var fileName = Path.GetFileName(file.AsSpan());
		var dot = fileName.LastIndexOf('.');
		if (dot <= 0 || !fileName[..dot].SequenceEqual(iconName))
		{
			return false;
		}

		var extension = fileName[dot..];
		foreach (var known in _iconExtensions)
		{
			if (extension.Equals(known, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}

		return false;
	}

	private static Candidate Rank(string file)
	{
		var extension = Path.GetExtension(file.AsSpan());
		var size = ReadPathSize(file);
		if (extension.Equals(".xpm", StringComparison.OrdinalIgnoreCase))
		{
			return new Candidate(file, TierLegacy, size.Pixels);
		}

		if (extension.Equals(".svg", StringComparison.OrdinalIgnoreCase) || size.Scalable)
		{
			return new Candidate(file, TierScalable, 0);
		}

		if (size.Pixels > 0)
		{
			return new Candidate(file, TierSized, size.Pixels);
		}

		return new Candidate(file, TierUnsized, ReadLength(file));
	}

	private static bool IsBetter(Candidate candidate, Candidate current)
		=> candidate.Tier > current.Tier || (candidate.Tier == current.Tier && candidate.Score > current.Score);

	private static PathSize ReadPathSize(string file)
	{
		var directory = Path.GetDirectoryName(file.AsSpan());
		while (!directory.IsEmpty)
		{
			var segment = Path.GetFileName(directory);
			if (segment.Equals(ScalableDirectoryName, StringComparison.OrdinalIgnoreCase))
			{
				return new PathSize(0, true);
			}

			if (TryParseSizeSegment(segment, out var pixels))
			{
				return new PathSize(pixels, false);
			}

			directory = Path.GetDirectoryName(directory);
		}

		return default;
	}

	private static bool TryParseSizeSegment(ReadOnlySpan<char> segment, out int pixels)
	{
		pixels = 0;
		var separator = segment.IndexOf('x');
		if (separator <= 0)
		{
			return false;
		}

		if (!TryParseDimension(segment[..separator], out var width))
		{
			return false;
		}

		var rest = segment[(separator + 1)..];
		var scaleMarker = rest.IndexOf('@');
		if (!TryParseDimension(scaleMarker < 0 ? rest : rest[..scaleMarker], out var height))
		{
			return false;
		}

		var scale = 1;
		if (scaleMarker >= 0)
		{
			var scaleSpan = rest[(scaleMarker + 1)..];
			if (scaleSpan.Length < 2 || (scaleSpan[^1] is not 'x' and not 'X'))
			{
				return false;
			}

			if (!TryParseDimension(scaleSpan[..^1], out scale))
			{
				return false;
			}
		}

		pixels = Math.Max(width, height) * scale;
		return true;
	}

	private static bool TryParseDimension(ReadOnlySpan<char> value, out int parsed)
		=> int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out parsed) && parsed > 0;

	private static long ReadLength(string file)
	{
		try
		{
			return new FileInfo(file).Length;
		}
		catch (IOException)
		{
			return 0;
		}
		catch (UnauthorizedAccessException)
		{
			return 0;
		}
	}

	private readonly record struct Candidate(string FilePath, int Tier, long Score);

	private readonly record struct PathSize(int Pixels, bool Scalable);
}
