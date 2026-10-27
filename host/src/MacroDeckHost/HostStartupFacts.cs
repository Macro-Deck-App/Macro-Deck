using MacroDeckHost.Application.Backups;

namespace MacroDeckHost;

/// <summary>
/// Facts established before the dependency injection container exists, so the services built later can
/// still see them.
/// </summary>
public static class HostStartupFacts
{
	public static bool RestoreApplied { get; set; }

	public static RestoreFailure RestoreFailure { get; set; }
}
