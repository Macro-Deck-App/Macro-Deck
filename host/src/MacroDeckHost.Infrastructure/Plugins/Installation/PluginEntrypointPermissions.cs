namespace MacroDeckHost.Infrastructure.Plugins.Installation;

// Archives carry no Unix modes, so a plugin's native entrypoint can reach disk without the executable
// bit: an install extracts it that way, and a backup restore writes it back that way too.
internal static class PluginEntrypointPermissions
{
	private const UnixFileMode ExecutableMode = UnixFileMode.UserRead
		| UnixFileMode.UserWrite
		| UnixFileMode.UserExecute
		| UnixFileMode.GroupRead
		| UnixFileMode.GroupExecute
		| UnixFileMode.OtherRead
		| UnixFileMode.OtherExecute;

	public static void MakeExecutable(string path)
	{
		if (OperatingSystem.IsWindows() || !File.Exists(path))
		{
			return;
		}

		try
		{
			File.SetUnixFileMode(path, ExecutableMode);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
		}
	}
}
