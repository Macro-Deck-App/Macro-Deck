using MacroDeck.LicenseTool.Configuration;

namespace MacroDeck.LicenseTool.Model;

internal static class AssetPolicy
{
	private static readonly string[] SkippedDirectories = ["bin", "obj", "node_modules", "dist", "target", "tests"];

	public static void Check(string repositoryRoot, AssetsConfiguration assets, IReadOnlySet<string> attributedFiles, Problems problems)
	{
		var firstParty = new HashSet<string>(StringComparer.Ordinal);
		foreach (var pattern in assets.FirstParty)
		{
			var matches = Glob.Expand(repositoryRoot, pattern);
			if (matches.Count == 0)
			{
				problems.Add($"config.yml: first-party path '{pattern}' matches no file");
			}

			foreach (var match in matches.Where(attributedFiles.Contains))
			{
				problems.Add($"asset {match} is listed as first-party and attributed in third-party/attributions.yml");
			}

			firstParty.UnionWith(matches);
		}

		foreach (var root in assets.Roots)
		{
			var directory = Path.Combine(repositoryRoot, root);
			if (!Directory.Exists(directory))
			{
				problems.Add($"config.yml: asset root {root} does not exist");
				continue;
			}

			foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
			{
				var relative = Path.GetRelativePath(repositoryRoot, file).Replace('\\', '/');
				if (!assets.Extensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase)
					|| relative.Split('/').Any(segment => SkippedDirectories.Contains(segment, StringComparer.Ordinal))
					|| attributedFiles.Contains(relative)
					|| firstParty.Contains(relative))
				{
					continue;
				}

				problems.Add($"asset {relative} is neither attributed in third-party/attributions.yml nor listed as first-party in third-party/config.yml");
			}
		}
	}
}
