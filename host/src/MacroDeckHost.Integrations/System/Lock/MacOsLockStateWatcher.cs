using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading.Channels;
using MacroDeckHost.Integrations.Native;
using Serilog;

namespace MacroDeckHost.Integrations.System.Lock;

[SupportedOSPlatform("macos")]
internal sealed class MacOsLockStateWatcher : ILockStateWatcher
{
	private const string CoreFoundation = MacOsAccessibility.CoreFoundation;
	private const string ScreenIsLockedNotification = "com.apple.screenIsLocked";
	private const string ScreenIsUnlockedNotification = "com.apple.screenIsUnlocked";
	private const int ChannelCapacity = 16;

	// A CFIndex-typed constant (CFNotificationSuspensionBehavior), so it is passed as nint.
	private const nint SuspensionBehaviorDeliverImmediately = 4;

	private static readonly ILogger _logger = Log.ForContext<MacOsLockStateWatcher>();

	// The function pointer handed to CFNotificationCenterAddObserver is only valid for as long as this
	// delegate is reachable; it must stay a static field or the GC could collect it while CoreFoundation
	// still holds the pointer.
	private static readonly NotificationCallback _callback = OnNotification;
	private static readonly IntPtr _callbackPointer = TryGetCallbackPointer();

	// Created once and never released: they live for the process, which removes any "callback reads a
	// freed CFString" race. A native failure degrades the name to IntPtr.Zero rather than throwing out of
	// the static initializer, and every constructor treats that as unsupported.
	private static readonly IntPtr _lockedNotificationName = TryCreateCFString(ScreenIsLockedNotification);
	private static readonly IntPtr _unlockedNotificationName = TryCreateCFString(ScreenIsUnlockedNotification);

	// Tokens are opaque values CoreFoundation only compares and never dereferences. Allocating them from
	// a process-wide counter means a token is never reused, so a callback that arrives after an instance
	// has disposed and removed its entries simply misses the dictionary and returns.
	private static long _nextToken;

	private static readonly ConcurrentDictionary<IntPtr, (MacOsLockStateWatcher Watcher, bool Locked)> _instances =
		new();

	private readonly Channel<bool> _channel = Channel.CreateBounded<bool>(new BoundedChannelOptions(ChannelCapacity)
	{
		FullMode = BoundedChannelFullMode.DropOldest,
		SingleReader = true
	});

	private readonly bool _isSupported;
	private readonly IntPtr _center;
	private readonly IntPtr _lockedToken;
	private readonly IntPtr _unlockedToken;

	private int _disposed;

	public MacOsLockStateWatcher()
	{
		var lockedToken = IntPtr.Zero;
		var unlockedToken = IntPtr.Zero;
		try
		{
			if (_callbackPointer == IntPtr.Zero ||
				_lockedNotificationName == IntPtr.Zero ||
				_unlockedNotificationName == IntPtr.Zero)
			{
				return;
			}

			var center = CFNotificationCenterGetDistributedCenter();
			if (center == IntPtr.Zero)
			{
				return;
			}

			lockedToken = checked((IntPtr)Interlocked.Increment(ref _nextToken));
			unlockedToken = checked((IntPtr)Interlocked.Increment(ref _nextToken));

			// Published before the observers are armed, so a callback can never see a token this
			// dictionary does not already know about.
			_instances[lockedToken] = (this, true);
			_instances[unlockedToken] = (this, false);

			CFNotificationCenterAddObserver(center,
				lockedToken,
				_callbackPointer,
				_lockedNotificationName,
				IntPtr.Zero,
				SuspensionBehaviorDeliverImmediately);
			CFNotificationCenterAddObserver(center,
				unlockedToken,
				_callbackPointer,
				_unlockedNotificationName,
				IntPtr.Zero,
				SuspensionBehaviorDeliverImmediately);

			_center = center;
			_lockedToken = lockedToken;
			_unlockedToken = unlockedToken;
			_isSupported = true;
		}
		catch
		{
			if (lockedToken != IntPtr.Zero)
			{
				_instances.TryRemove(lockedToken, out _);
			}

			if (unlockedToken != IntPtr.Zero)
			{
				_instances.TryRemove(unlockedToken, out _);
			}

			_center = IntPtr.Zero;
			_lockedToken = IntPtr.Zero;
			_unlockedToken = IntPtr.Zero;
			_isSupported = false;
		}
	}

	public bool IsSupported => _isSupported;

	public async IAsyncEnumerable<bool> WatchAsync([EnumeratorCancellation] CancellationToken cancellationToken)
	{
		await foreach (var locked in _channel.Reader.ReadAllAsync(cancellationToken))
		{
			yield return locked;
		}
	}

	public void Dispose()
	{
		if (Interlocked.Exchange(ref _disposed, 1) != 0)
		{
			return;
		}

		if (_center != IntPtr.Zero)
		{
			if (_lockedToken != IntPtr.Zero)
			{
				CFNotificationCenterRemoveEveryObserver(_center, _lockedToken);
			}

			if (_unlockedToken != IntPtr.Zero)
			{
				CFNotificationCenterRemoveEveryObserver(_center, _unlockedToken);
			}
		}

		if (_lockedToken != IntPtr.Zero)
		{
			_instances.TryRemove(_lockedToken, out _);
		}

		if (_unlockedToken != IntPtr.Zero)
		{
			_instances.TryRemove(_unlockedToken, out _);
		}

		_channel.Writer.TryComplete();
	}

	private static void OnNotification(IntPtr center, IntPtr observer, IntPtr name, IntPtr obj, IntPtr userInfo)
	{
		try
		{
			if (_instances.TryGetValue(observer, out var entry))
			{
				entry.Watcher._channel.Writer.TryWrite(entry.Locked);
			}
		}
		catch (Exception e)
		{
			// This callback runs on the pumped main thread inside CFNotificationCenter's call frame: an
			// exception unwinding into native code is undefined behaviour, so every path out of here
			// must be an ordinary return.
			_logger.Error(e, "The macOS lock-state notification callback failed");
		}
	}

	private static IntPtr TryGetCallbackPointer()
	{
		try
		{
			return Marshal.GetFunctionPointerForDelegate(_callback);
		}
		catch
		{
			return IntPtr.Zero;
		}
	}

	private static IntPtr TryCreateCFString(string value)
	{
		try
		{
			return MacOsCoreFoundation.CreateCFString(value);
		}
		catch
		{
			return IntPtr.Zero;
		}
	}

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	private delegate void NotificationCallback(IntPtr center,
		IntPtr observer,
		IntPtr name,
		IntPtr obj,
		IntPtr userInfo);

	[DllImport(CoreFoundation)]
	private static extern IntPtr CFNotificationCenterGetDistributedCenter();

	[DllImport(CoreFoundation)]
	private static extern void CFNotificationCenterAddObserver(IntPtr center,
		IntPtr observer,
		IntPtr callback,
		IntPtr name,
		IntPtr obj,
		nint suspensionBehavior);

	[DllImport(CoreFoundation)]
	private static extern void CFNotificationCenterRemoveEveryObserver(IntPtr center, IntPtr observer);
}
