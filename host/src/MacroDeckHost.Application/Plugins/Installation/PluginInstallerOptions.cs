namespace MacroDeckHost.Application.Plugins.Installation;

public sealed record PluginInstallerOptions
{
	public static readonly PluginInstallerOptions Default = new();

	public bool RetainDownloads { get; init; }

	public long CacheBudgetBytes { get; init; } = 512L * 1024 * 1024;

	public TimeSpan DownloadTimeout { get; init; } = TimeSpan.FromMinutes(10);

	public TimeSpan ActivationHealthTimeout { get; init; } = TimeSpan.FromSeconds(45);

	public int VersionsToRetain { get; init; } = 2;
}
