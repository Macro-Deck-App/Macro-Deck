namespace MacroDeckHost.Tests.Support;

internal static class ProbeLocator
{
	private const string ProbeName = "MacroDeckHost.Tests.InputProbe";
	private static readonly Lock _gate = new();
	private static string? _copyDirectory;

	// Targeting resolves windows by process name, so each probe instance runs as its own named executable.
	public static string Executable(string processName)
	{
		lock (_gate)
		{
			_copyDirectory ??= CopyBuildOutput();
			var source = Path.Combine(_copyDirectory, FileName(ProbeName));
			var target = Path.Combine(_copyDirectory, FileName(processName));
			if (!File.Exists(target))
			{
				File.Copy(source, target);
				if (!OperatingSystem.IsWindows())
				{
					File.SetUnixFileMode(target, File.GetUnixFileMode(source));
				}
			}

			return target;
		}
	}

	public static void DeleteCopies()
	{
		lock (_gate)
		{
			if (_copyDirectory is null)
			{
				return;
			}

			try
			{
				Directory.Delete(_copyDirectory, true);
			}
			catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
			{
				TestContext.Progress.WriteLine($"Could not remove {_copyDirectory}: {exception.Message}");
			}

			_copyDirectory = null;
		}
	}

	private static string FileName(string name)
	{
		return OperatingSystem.IsWindows() ? name + ".exe" : name;
	}

	private static string CopyBuildOutput()
	{
		var output = BuildOutput();
		var copy = Path.Combine(Path.GetTempPath(), "macrodeck-input-probe-" + Environment.ProcessId);
		Directory.CreateDirectory(copy);
		foreach (var file in Directory.EnumerateFiles(output, "*", SearchOption.AllDirectories))
		{
			var target = Path.Combine(copy, Path.GetRelativePath(output, file));
			Directory.CreateDirectory(Path.GetDirectoryName(target)!);
			File.Copy(file, target, true);
		}

		return copy;
	}

	private static string BuildOutput()
	{
		var directory = new DirectoryInfo(AppContext.BaseDirectory);
		while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MacroDeck.slnx")))
		{
			directory = directory.Parent;
		}

		if (directory is null)
		{
			throw new InvalidOperationException("Could not locate the repository root above the test output.");
		}

		var configuration = AppContext.BaseDirectory.Contains("Debug", StringComparison.Ordinal) ? "Debug" : "Release";
		var output = Path.Combine(directory.FullName,
			"host",
			"tests",
			"fixtures",
			ProbeName,
			"bin",
			configuration,
			"net10.0");
		if (!File.Exists(Path.Combine(output, FileName(ProbeName))))
		{
			throw new InvalidOperationException($"{ProbeName} was not found under '{output}'. Build it first.");
		}

		return output;
	}
}
