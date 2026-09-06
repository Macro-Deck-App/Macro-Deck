using System.Runtime.Versioning;
using MacroDeckHost.Integrations.Native;
using Serilog;

namespace MacroDeckHost.Integrations.System.Lock;

public static class LockStateWatcherFactory
{
	private static readonly ILogger _logger = Log.ForContext(typeof(LockStateWatcherFactory));

	public static ILockStateWatcher Create()
	{
		ILockStateWatcher watcher;
		string description;

		if (OperatingSystem.IsMacOS())
		{
			(watcher, description) = CreateForMacOs();
		}
		else
		{
			watcher = new NullLockStateWatcher();
			description = "unsupported";
		}

		_logger.Information("Lock-state watcher selected: {Description}", description);
		return watcher;
	}

	[SupportedOSPlatform("macos")]
	private static (ILockStateWatcher Watcher, string Description) CreateForMacOs()
	{
		if (!MacOsMainRunLoop.IsPumping)
		{
			// The main-thread run loop is what makes the CFNotificationCenter events deliverable at all
			// (ADR 0078); without it the native watcher would never receive events.
			return (new NullLockStateWatcher(), "unsupported (macOS, run loop not pumping)");
		}

		var native = new MacOsLockStateWatcher();
		if (native.IsSupported)
		{
			return (native, "native CFNotificationCenter (macOS)");
		}

		native.Dispose();
		return (new NullLockStateWatcher(), "unsupported (macOS, CFNotificationCenter unavailable)");
	}
}
