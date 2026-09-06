using System.Runtime.Versioning;
using MacroDeckHost.Integrations.Native;
using Serilog;

namespace MacroDeckHost.Integrations.System.Focus;

public static class FocusedApplicationWatcherFactory
{
	private static readonly ILogger _logger = Log.ForContext(typeof(FocusedApplicationWatcherFactory));

	public static IFocusedApplicationWatcher Create()
	{
		IFocusedApplicationWatcher watcher;
		string description;

		if (OperatingSystem.IsWindows())
		{
			(watcher, description) = CreateForWindows();
		}
		else if (OperatingSystem.IsMacOS())
		{
			(watcher, description) = CreateForMacOs();
		}
		else if (OperatingSystem.IsLinux())
		{
			watcher = new PollingFocusedApplicationWatcher(FocusedWindowReaderFactory.Create());
			description = "polling (Linux/X11)";
		}
		else
		{
			watcher = new NullFocusedApplicationWatcher();
			description = "unsupported";
		}

		_logger.Information("Application-focus watcher selected: {Description}", description);
		return watcher;
	}

	[SupportedOSPlatform("windows")]
	private static (IFocusedApplicationWatcher Watcher, string Description) CreateForWindows()
	{
		var native = new WindowsFocusedApplicationWatcher();
		if (native.IsSupported)
		{
			return (native, "native SetWinEventHook (Windows)");
		}

		native.Dispose();
		return (new PollingFocusedApplicationWatcher(FocusedWindowReaderFactory.Create()),
			"polling (Windows, SetWinEventHook unavailable)");
	}

	[SupportedOSPlatform("macos")]
	private static (IFocusedApplicationWatcher Watcher, string Description) CreateForMacOs()
	{
		if (!MacOsMainRunLoop.IsPumping)
		{
			// The main-thread run loop is what makes NSWorkspaceDidActivateApplicationNotification
			// deliverable at all (ADR 0078); without it the native watcher would never receive events.
			return (new PollingFocusedApplicationWatcher(FocusedWindowReaderFactory.Create()),
				"polling (macOS, run loop not pumping)");
		}

		var native = new MacOsFocusedApplicationWatcher();
		if (native.IsSupported)
		{
			return (native, "native NSWorkspace (macOS)");
		}

		native.Dispose();
		return (new PollingFocusedApplicationWatcher(FocusedWindowReaderFactory.Create()),
			"polling (macOS, NSWorkspace unavailable)");
	}
}
