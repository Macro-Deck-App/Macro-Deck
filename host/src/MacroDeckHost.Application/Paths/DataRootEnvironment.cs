namespace MacroDeckHost.Application.Paths;

public sealed record DataRootEnvironment(
	string BaseDirectory,
	string WorkingDirectory,
	string HomeDirectory,
	DataRootPlatform Platform,
	Func<string, string?> GetEnvironmentVariable,
	Func<string, bool> FileExists)
{
	public static DataRootEnvironment Current()
	{
		var platform = OperatingSystem.IsWindows()
			? DataRootPlatform.Windows
			: OperatingSystem.IsMacOS()
				? DataRootPlatform.MacOs
				: DataRootPlatform.Linux;

		return new DataRootEnvironment(AppContext.BaseDirectory,
			Directory.GetCurrentDirectory(),
			Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
			platform,
			Environment.GetEnvironmentVariable,
			File.Exists);
	}
}
