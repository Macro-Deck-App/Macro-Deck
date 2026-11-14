using System.Runtime.InteropServices;

namespace MacroDeckHost;

public sealed record ShellParent(int? ExpectedProcessId, Func<int> CurrentParentProcessId)
{
	public const string EnvironmentVariable = "MACRODECK_SHELL_PROCESS_ID";

	// Plugins and programs started from a deck inherit this process's environment, and a host started
	// from one of them must not watch a parent that was never its own.
	public static ShellParent Take()
	{
		var provided = Environment.GetEnvironmentVariable(EnvironmentVariable);
		Environment.SetEnvironmentVariable(EnvironmentVariable, null);

		var expected = !OperatingSystem.IsWindows() && int.TryParse(provided?.Trim(), out var pid) && pid > 1
			? pid
			: (int?)null;
		return new ShellParent(expected, GetParentProcessId);
	}

	[DllImport("libc", EntryPoint = "getppid")]
	private static extern int GetParentProcessId();
}
