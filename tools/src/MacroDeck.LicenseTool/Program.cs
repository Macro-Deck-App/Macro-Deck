using MacroDeck.LicenseTool;
using MacroDeck.LicenseTool.Collectors;

const string ThirdPartyNoticesFile = "THIRD-PARTY-NOTICES";
const string NoticeFile = "NOTICE";

string? repository = null;
var check = false;
for (var index = 0; index < args.Length; index++)
{
	switch (args[index])
	{
		case "--check":
			check = true;
			break;
		case "--repo" when index + 1 < args.Length:
			repository = Path.GetFullPath(args[++index]);
			break;
		default:
			Console.Error.WriteLine("usage: MacroDeck.LicenseTool [--repo <repository root>] [--check]");
			return 2;
	}
}

try
{
	repository ??= FindRepositoryRoot(Directory.GetCurrentDirectory());
	var result = NoticeGenerator.Generate(repository, new CargoProcessMetadataSource());
	if (result.Problems.Count > 0)
	{
		Console.Error.WriteLine($"third-party license check failed with {result.Problems.Count} problem(s):");
		foreach (var problem in result.Problems)
		{
			Console.Error.WriteLine($"  - {problem}");
		}

		return 1;
	}

	var outputs = new[] { (ThirdPartyNoticesFile, result.ThirdPartyNotices), (NoticeFile, result.Notice) };
	if (check)
	{
		var stale = outputs
			.Where(output => !File.Exists(Path.Combine(repository, output.Item1))
				|| File.ReadAllText(Path.Combine(repository, output.Item1)) != output.Item2)
			.Select(output => output.Item1)
			.ToList();
		if (stale.Count > 0)
		{
			Console.Error.WriteLine($"out of date: {string.Join(", ", stale)}; run: dotnet run --project tools/src/MacroDeck.LicenseTool");
			return 1;
		}

		Console.WriteLine($"{ThirdPartyNoticesFile} and {NoticeFile} are up to date.");
		return 0;
	}

	foreach (var (file, content) in outputs)
	{
		File.WriteAllText(Path.Combine(repository, file), content);
	}

	Console.WriteLine($"Wrote {ThirdPartyNoticesFile} and {NoticeFile}.");
	return 0;
}
catch (ToolException exception)
{
	Console.Error.WriteLine($"error: {exception.Message}");
	return 1;
}

static string FindRepositoryRoot(string start)
{
	for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
	{
		if (File.Exists(Path.Combine(directory.FullName, "MacroDeck.slnx")))
		{
			return directory.FullName;
		}
	}

	throw new ToolException("could not find the repository root (MacroDeck.slnx); pass --repo <path>");
}
