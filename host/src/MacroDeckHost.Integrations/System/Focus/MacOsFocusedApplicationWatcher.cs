using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading.Channels;
using MacroDeckHost.Integrations.Keyboard;
using MacroDeckHost.Integrations.Native;
using Serilog;

namespace MacroDeckHost.Integrations.System.Focus;

[SupportedOSPlatform("macos")]
internal sealed class MacOsFocusedApplicationWatcher : IFocusedApplicationWatcher
{
	private const string LibObjC = "/usr/lib/libobjc.dylib";
	private const string AppKit = "/System/Library/Frameworks/AppKit.framework/AppKit";
	private const string ObserverClassName = "MacroDeckFocusObserver";
	private const string ActivationSelectorName = "onApplicationActivated:";
	private const int ChannelCapacity = 16;

	private static readonly ILogger _logger = Log.ForContext<MacOsFocusedApplicationWatcher>();

	// Registered as the class's Objective-C method implementation, so this delegate's function pointer
	// is held by the ObjC runtime for the process lifetime - it must stay reachable as a static field or
	// the GC could collect it out from under a live IMP.
	private static readonly ActivationCallback _callback = OnApplicationActivated;

	private static readonly IntPtr _activationSelector = TryRegisterSelector(ActivationSelectorName);
	private static readonly IntPtr _observerClass = CreateObserverClass();

	private static readonly ConcurrentDictionary<IntPtr, MacOsFocusedApplicationWatcher> _instances = new();

	private readonly Channel<MacOsRunningApplicationInfo> _channel = Channel.CreateBounded<MacOsRunningApplicationInfo>(
		new BoundedChannelOptions(ChannelCapacity)
		{
			FullMode = BoundedChannelFullMode.DropOldest,
			SingleReader = true
		});

	// A plain object, not System.Threading.Lock: this namespace's own sibling
	// MacroDeckHost.Integrations.System.Lock shadows the BCL type name here.
	private readonly object _gate = new();
	private readonly bool _isSupported;
	private readonly IntPtr _instance;
	private readonly IntPtr _center;

	private int _disposed;

	public MacOsFocusedApplicationWatcher()
	{
		var instance = IntPtr.Zero;
		try
		{
			if (_observerClass == IntPtr.Zero)
			{
				return;
			}

			instance = objc_msgSend_noArgs(_observerClass, sel_registerName(Utf8("new")));
			if (instance == IntPtr.Zero)
			{
				return;
			}

			var workspaceClass = objc_getClass(Utf8("NSWorkspace"));
			var workspace = workspaceClass == IntPtr.Zero
				? IntPtr.Zero
				: objc_msgSend_noArgs(workspaceClass, sel_registerName(Utf8("sharedWorkspace")));
			var center = workspace == IntPtr.Zero
				? IntPtr.Zero
				: objc_msgSend_noArgs(workspace, sel_registerName(Utf8("notificationCenter")));
			var name = MacOsAccessibility.ReadGlobalRef(AppKit, "NSWorkspaceDidActivateApplicationNotification");

			if (center == IntPtr.Zero || name == IntPtr.Zero)
			{
				ReleaseInstance(instance);
				instance = IntPtr.Zero;
				return;
			}

			// Fields are set, and the instance tracked, before the observer is armed: if
			// addObserver:selector:name:object: itself were to fail, the catch block below still has
			// everything it needs to unwind cleanly.
			_instance = instance;
			_center = center;
			_instances[instance] = this;
			objc_msgSend_addObserver(center,
				sel_registerName(Utf8("addObserver:selector:name:object:")),
				instance,
				_activationSelector,
				name,
				IntPtr.Zero);

			_isSupported = true;
		}
		catch
		{
			if (instance != IntPtr.Zero)
			{
				_instances.TryRemove(instance, out _);
				ReleaseInstance(instance);
			}

			_instance = IntPtr.Zero;
			_center = IntPtr.Zero;
			_isSupported = false;
		}
	}

	public bool IsSupported => _isSupported;

	// Never a new string: the UI renders this verbatim and every existing value already ships
	// translations. macOS is always a supported platform for focus detection (MacOsFocusedWindowReader
	// always returns null here) - a failed native subscription falls back to polling instead.
	public string? UnsupportedReason => null;

	public async IAsyncEnumerable<FocusedAppInfo> WatchAsync(
		[EnumeratorCancellation] CancellationToken cancellationToken)
	{
		// The observer is installed in the constructor, so a callback may already be queueing an event
		// before this enumeration ever starts. The seed read and every callback write share one lock,
		// with the seed's read inside the lock, so "read seed -> callback writes a newer focus -> write
		// seed" can never clobber the newer value with a stale one.
		lock (_gate)
		{
			if (MacOsRunningApplication.TryReadFrontmost() is { } seed)
			{
				_channel.Writer.TryWrite(seed);
			}
		}

		await foreach (var info in _channel.Reader.ReadAllAsync(cancellationToken))
		{
			yield return new FocusedAppInfo(info.ProcessId,
				info.ExecutablePath,
				TryGetProcessName(info.ProcessId),
				info.BundleId);
		}
	}

	public void Dispose()
	{
		if (Interlocked.Exchange(ref _disposed, 1) != 0)
		{
			return;
		}

		if (_instance != IntPtr.Zero)
		{
			_instances.TryRemove(_instance, out _);

			if (_center != IntPtr.Zero)
			{
				objc_msgSend_removeObserver(_center, sel_registerName(Utf8("removeObserver:")), _instance);
			}

			// Deliberately not released: Dispose runs on whatever thread disposes the DI container while
			// the main thread may still be pumping the run loop, so releasing here could free the
			// instance between NSNotificationCenter snapshotting its observer list and calling out to it.
			// It is a single NSObject held until process exit.
		}

		_channel.Writer.TryComplete();
	}

	private static void OnApplicationActivated(IntPtr self, IntPtr selector, IntPtr notification)
	{
		var pool = objc_autoreleasePoolPush();
		try
		{
			if (!_instances.TryGetValue(self, out var watcher))
			{
				return;
			}

			var userInfo = objc_msgSend_noArgs(notification, sel_registerName(Utf8("userInfo")));
			if (userInfo == IntPtr.Zero)
			{
				return;
			}

			var key = MacOsAccessibility.ReadGlobalRef(AppKit, "NSWorkspaceApplicationKey");
			var app = key == IntPtr.Zero
				? IntPtr.Zero
				: objc_msgSend_ptr(userInfo, sel_registerName(Utf8("objectForKey:")), key);
			if (MacOsRunningApplication.TryRead(app) is not { } info)
			{
				return;
			}

			watcher.OnActivation(info);
		}
		catch (Exception e)
		{
			// This callback runs on the main thread inside NSWorkspace's call frame: an exception
			// unwinding into Objective-C is undefined behaviour (a hard crash in practice), so every
			// path out of here must be an ordinary return.
			_logger.Error(e, "The macOS application-activation callback failed");
		}
		finally
		{
			objc_autoreleasePoolPop(pool);
		}
	}

	private void OnActivation(MacOsRunningApplicationInfo info)
	{
		lock (_gate)
		{
			_channel.Writer.TryWrite(info);
		}
	}

	private static string? TryGetProcessName(int pid)
	{
		try
		{
			return KeyboardProcessName.ForPid(pid);
		}
		catch (Win32Exception)
		{
			return null;
		}
	}

	private static IntPtr TryRegisterSelector(string name)
	{
		try
		{
			return sel_registerName(Utf8(name));
		}
		catch
		{
			return IntPtr.Zero;
		}
	}

	private static IntPtr CreateObserverClass()
	{
		try
		{
			if (!NativeLibrary.TryLoad(AppKit, out _) || _activationSelector == IntPtr.Zero)
			{
				return IntPtr.Zero;
			}

			var nsObject = objc_getClass(Utf8("NSObject"));
			if (nsObject == IntPtr.Zero)
			{
				return IntPtr.Zero;
			}

			var cls = objc_allocateClassPair(nsObject, Utf8(ObserverClassName), 0);
			if (cls == IntPtr.Zero)
			{
				// This field is static readonly, so this method runs at most once per process - a class
				// name collision here means the class already exists in the process for another reason
				// (e.g. a second AssemblyLoadContext loading this assembly), so reuse it instead of failing.
				return objc_getClass(Utf8(ObserverClassName));
			}

			var added = class_addMethod(cls,
				_activationSelector,
				Marshal.GetFunctionPointerForDelegate(_callback),
				Utf8("v@:@"));
			if (!added)
			{
				return IntPtr.Zero;
			}

			objc_registerClassPair(cls);
			return cls;
		}
		catch
		{
			return IntPtr.Zero;
		}
	}

	private static void ReleaseInstance(IntPtr instance)
	{
		if (instance != IntPtr.Zero)
		{
			objc_msgSend_noArgs(instance, sel_registerName(Utf8("release")));
		}
	}

	private static byte[] Utf8(string value) => MacOsAccessibility.Utf8(value);

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	private delegate void ActivationCallback(IntPtr self, IntPtr selector, IntPtr notification);

	[DllImport(LibObjC)]
	private static extern IntPtr objc_getClass(byte[] name);

	[DllImport(LibObjC)]
	private static extern IntPtr sel_registerName(byte[] name);

	[DllImport(LibObjC)]
	private static extern IntPtr objc_allocateClassPair(IntPtr superclass, byte[] name, nint extraBytes);

	[DllImport(LibObjC)]
	[return: MarshalAs(UnmanagedType.I1)]
	private static extern bool class_addMethod(IntPtr cls, IntPtr selector, IntPtr impl, byte[] types);

	[DllImport(LibObjC)]
	private static extern void objc_registerClassPair(IntPtr cls);

	[DllImport(LibObjC, EntryPoint = "objc_msgSend")]
	private static extern IntPtr objc_msgSend_noArgs(IntPtr receiver, IntPtr selector);

	[DllImport(LibObjC, EntryPoint = "objc_msgSend")]
	private static extern IntPtr objc_msgSend_ptr(IntPtr receiver, IntPtr selector, IntPtr arg);

	[DllImport(LibObjC, EntryPoint = "objc_msgSend")]
	private static extern void objc_msgSend_addObserver(IntPtr receiver,
		IntPtr selector,
		IntPtr observer,
		IntPtr aSelector,
		IntPtr aName,
		IntPtr anObject);

	[DllImport(LibObjC, EntryPoint = "objc_msgSend")]
	private static extern void objc_msgSend_removeObserver(IntPtr receiver, IntPtr selector, IntPtr observer);

	[DllImport(LibObjC)]
	private static extern IntPtr objc_autoreleasePoolPush();

	[DllImport(LibObjC)]
	private static extern void objc_autoreleasePoolPop(IntPtr handle);
}
