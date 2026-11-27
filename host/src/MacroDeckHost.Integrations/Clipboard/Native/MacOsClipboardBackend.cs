using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace MacroDeckHost.Integrations.Clipboard.Native;

[SupportedOSPlatform("macos")]
internal sealed class MacOsClipboardBackend : IClipboardBackend
{
	private const string TextType = "public.utf8-plain-text";
	private const string FileUrlType = "public.file-url";
	private const nuint Utf8Encoding = 4;

	private static readonly string[] _imageTypes = ["public.png", "public.tiff", "public.jpeg"];

	private readonly Lock _gate = new();

	public ClipboardAvailability Availability
		=> MacOsObjc.IsAvailable
			? ClipboardAvailability.Available
			: ClipboardAvailability.Unavailable(ClipboardUnavailableReason.UnsupportedOs);

	public Task<ClipboardContentKinds> ReadKindsAsync(CancellationToken cancellationToken)
	{
		return RunAsync(ReadKinds, cancellationToken);
	}

	public Task<ClipboardTextReading> ReadTextAsync(int maxChars, CancellationToken cancellationToken)
	{
		return RunAsync(() => ReadText(maxChars), cancellationToken);
	}

	public Task<ClipboardWriteReceipt> SetTextAsync(string text, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(text);
		return RunAsync(() => SetText(text), cancellationToken);
	}

	public Task<ClipboardWriteReceipt> SetImageAsync(string path, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(path);
		return RunAsync(() => SetImage(path), cancellationToken);
	}

	public Task<ClipboardWriteReceipt> SetFilesAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(paths);
		return RunAsync(() => SetFiles(paths), cancellationToken);
	}

	public Task<ClipboardWriteReceipt> ClearAsync(CancellationToken cancellationToken)
	{
		return RunAsync(Clear, cancellationToken);
	}

	public IClipboardChangeSource CreateChangeSource()
	{
		return new MacOsClipboardChangeSource(ReadChangeCount);
	}

	internal long ReadChangeCount()
	{
		lock (_gate)
		{
			var pool = MacOsObjc.objc_autoreleasePoolPush();
			try
			{
				return MacOsObjc.SendInteger(Pasteboard(), MacOsObjc.Selector("changeCount"));
			}
			finally
			{
				MacOsObjc.objc_autoreleasePoolPop(pool);
			}
		}
	}

	private Task<T> RunAsync<T>(Func<T> operation, CancellationToken cancellationToken)
	{
		if (!MacOsObjc.IsAvailable)
		{
			throw new ClipboardException(ClipboardFailure.Unavailable);
		}

		return Task.Run(() =>
			{
				lock (_gate)
				{
					var pool = MacOsObjc.objc_autoreleasePoolPush();
					try
					{
						return operation();
					}
					finally
					{
						MacOsObjc.objc_autoreleasePoolPop(pool);
					}
				}
			},
			cancellationToken);
	}

	private static ClipboardContentKinds ReadKinds()
	{
		var types = ReadTypes(Pasteboard());
		var kinds = ClipboardContentKinds.None;
		foreach (var type in types)
		{
			kinds |= type switch
			{
				TextType => ClipboardContentKinds.Text,
				FileUrlType => ClipboardContentKinds.Files,
				_ when _imageTypes.Contains(type, StringComparer.Ordinal) => ClipboardContentKinds.Image,
				_ => ClipboardContentKinds.None
			};
		}

		return kinds == ClipboardContentKinds.None && types.Count > 0 ? ClipboardContentKinds.Other : kinds;
	}

	private static ClipboardTextReading ReadText(int maxChars)
	{
		var pasteboard = Pasteboard();
		if (!ReadTypes(pasteboard).Contains(TextType, StringComparer.Ordinal))
		{
			return ClipboardTextReading.None;
		}

		var value = MacOsObjc.Send(pasteboard, MacOsObjc.Selector("stringForType:"), MacOsObjc.String(TextType));
		if (value == IntPtr.Zero)
		{
			// The types promise text, so nil here means macOS withheld it, for example after a paste prompt.
			throw new ClipboardException(ClipboardFailure.Refused);
		}

		var length = MacOsObjc.SendUnsigned(value, MacOsObjc.Selector("length"));
		if (length > (nuint)maxChars)
		{
			return ClipboardTextReading.TooLarge;
		}

		return ClipboardTextReading.Of(MacOsObjc.ToManagedString(value));
	}

	private static ClipboardWriteReceipt SetText(string text)
	{
		var pasteboard = Pasteboard();
		var bytes = Encoding.UTF8.GetBytes(text);
		var allocated = MacOsObjc.Send(MacOsObjc.Class("NSString"), MacOsObjc.Selector("alloc"));
		var value = MacOsObjc.SendInitWithBytes(allocated,
			MacOsObjc.Selector("initWithBytes:length:encoding:"),
			bytes,
			(nuint)bytes.Length,
			Utf8Encoding);
		if (value == IntPtr.Zero)
		{
			throw new ClipboardException(ClipboardFailure.Failed);
		}

		try
		{
			MacOsObjc.SendInteger(pasteboard, MacOsObjc.Selector("clearContents"));
			if (!MacOsObjc.SendBool(pasteboard,
				MacOsObjc.Selector("setString:forType:"),
				value,
				MacOsObjc.String(TextType)))
			{
				throw new ClipboardException(ClipboardFailure.Failed);
			}
		}
		finally
		{
			MacOsObjc.Send(value, MacOsObjc.Selector("release"));
		}

		return Receipt(pasteboard);
	}

	private static ClipboardWriteReceipt SetImage(string path)
	{
		if (!File.Exists(path))
		{
			throw new ClipboardException(ClipboardFailure.NotFound);
		}

		var pasteboard = Pasteboard();
		var image = MacOsObjc.Send(MacOsObjc.Send(MacOsObjc.Class("NSImage"), MacOsObjc.Selector("alloc")),
			MacOsObjc.Selector("initWithContentsOfFile:"),
			MacOsObjc.String(path));
		if (image == IntPtr.Zero)
		{
			throw new ClipboardException(ClipboardFailure.UnsupportedFormat);
		}

		try
		{
			var objects = MacOsObjc.Send(MacOsObjc.Class("NSArray"), MacOsObjc.Selector("arrayWithObject:"), image);
			Write(pasteboard, objects);
		}
		finally
		{
			MacOsObjc.Send(image, MacOsObjc.Selector("release"));
		}

		return Receipt(pasteboard);
	}

	private static ClipboardWriteReceipt SetFiles(IReadOnlyList<string> paths)
	{
		var pasteboard = Pasteboard();
		var objects = MacOsObjc.Send(MacOsObjc.Class("NSMutableArray"), MacOsObjc.Selector("array"));
		foreach (var path in paths)
		{
			var url = MacOsObjc.Send(MacOsObjc.Class("NSURL"),
				MacOsObjc.Selector("fileURLWithPath:"),
				MacOsObjc.String(path));
			if (url == IntPtr.Zero)
			{
				throw new ClipboardException(ClipboardFailure.Failed);
			}

			MacOsObjc.Send(objects, MacOsObjc.Selector("addObject:"), url);
		}

		Write(pasteboard, objects);
		return Receipt(pasteboard);
	}

	private static ClipboardWriteReceipt Clear()
	{
		var pasteboard = Pasteboard();
		MacOsObjc.SendInteger(pasteboard, MacOsObjc.Selector("clearContents"));
		return Receipt(pasteboard);
	}

	private static void Write(IntPtr pasteboard, IntPtr objects)
	{
		MacOsObjc.SendInteger(pasteboard, MacOsObjc.Selector("clearContents"));
		if (!MacOsObjc.SendBool(pasteboard, MacOsObjc.Selector("writeObjects:"), objects))
		{
			throw new ClipboardException(ClipboardFailure.Failed);
		}
	}

	private static ClipboardWriteReceipt Receipt(IntPtr pasteboard)
	{
		return new ClipboardWriteReceipt(MacOsObjc.SendInteger(pasteboard, MacOsObjc.Selector("changeCount")));
	}

	private static List<string> ReadTypes(IntPtr pasteboard)
	{
		var types = MacOsObjc.Send(pasteboard, MacOsObjc.Selector("types"));
		var result = new List<string>();
		if (types == IntPtr.Zero)
		{
			return result;
		}

		var count = MacOsObjc.SendUnsigned(types, MacOsObjc.Selector("count"));
		var objectAtIndex = MacOsObjc.Selector("objectAtIndex:");
		for (nuint index = 0; index < count; index++)
		{
			var type = MacOsObjc.SendIndex(types, objectAtIndex, index);
			if (type != IntPtr.Zero)
			{
				result.Add(MacOsObjc.ToManagedString(type));
			}
		}

		return result;
	}

	private static IntPtr Pasteboard()
	{
		var pasteboard = MacOsObjc.Send(MacOsObjc.Class("NSPasteboard"), MacOsObjc.Selector("generalPasteboard"));
		return pasteboard == IntPtr.Zero ? throw new ClipboardException(ClipboardFailure.Unavailable) : pasteboard;
	}
}

[SupportedOSPlatform("macos")]
internal sealed class MacOsClipboardChangeSource : IClipboardChangeSource
{
	private static readonly TimeSpan _pollInterval = TimeSpan.FromMilliseconds(500);

	private readonly Func<long> _readChangeCount;
	private readonly Lock _gate = new();

	private Timer? _timer;
	private long? _lastCount;
	private int _polling;
	private bool _disposed;

	public MacOsClipboardChangeSource(Func<long> readChangeCount)
	{
		_readChangeCount = readChangeCount;
	}

	public event Action<long?>? Changed;

	public event Action? WatchUnsupported
	{
		add { }
		remove { }
	}

	public bool IsWatchUnsupported => false;

	public void Start()
	{
		lock (_gate)
		{
			if (_disposed || _timer is not null)
			{
				return;
			}

			_lastCount = TryRead();
			_timer = new Timer(_ => Poll(), null, _pollInterval, _pollInterval);
		}
	}

	public void Dispose()
	{
		lock (_gate)
		{
			_disposed = true;
			_timer?.Dispose();
			_timer = null;
		}
	}

	private void Poll()
	{
		if (Interlocked.Exchange(ref _polling, 1) == 1)
		{
			return;
		}

		try
		{
			if (TryRead() is not { } count || count == _lastCount)
			{
				return;
			}

			_lastCount = count;
			if (!Volatile.Read(ref _disposed))
			{
				Changed?.Invoke(count);
			}
		}
		finally
		{
			Volatile.Write(ref _polling, 0);
		}
	}

	private long? TryRead()
	{
		try
		{
			return _readChangeCount();
		}
		catch (Exception exception) when (exception is ClipboardException
			or DllNotFoundException
			or EntryPointNotFoundException)
		{
			return null;
		}
	}
}

[SupportedOSPlatform("macos")]
internal static class MacOsObjc
{
	private const string AppKit = "/System/Library/Frameworks/AppKit.framework/AppKit";
	private const string LibObjC = "/usr/lib/libobjc.dylib";

	private static readonly Lazy<bool> _available = new(() => NativeLibrary.TryLoad(AppKit, out _));

	public static bool IsAvailable => _available.Value;

	public static IntPtr Class(string name)
	{
		return objc_getClass(Utf8(name));
	}

	public static IntPtr Selector(string name)
	{
		return sel_registerName(Utf8(name));
	}

	public static IntPtr String(string value)
	{
		return Send(Class("NSString"), Selector("stringWithUTF8String:"), Utf8(value));
	}

	public static string ToManagedString(IntPtr value)
	{
		return Marshal.PtrToStringUTF8(Send(value, Selector("UTF8String"))) ?? string.Empty;
	}

	public static IntPtr Send(IntPtr receiver, IntPtr selector)
	{
		return objc_msgSend(receiver, selector);
	}

	public static IntPtr Send(IntPtr receiver, IntPtr selector, IntPtr argument)
	{
		return objc_msgSend_ptr(receiver, selector, argument);
	}

	public static IntPtr Send(IntPtr receiver, IntPtr selector, byte[] argument)
	{
		return objc_msgSend_bytes(receiver, selector, argument);
	}

	public static IntPtr SendIndex(IntPtr receiver, IntPtr selector, nuint index)
	{
		return objc_msgSend_index(receiver, selector, index);
	}

	public static IntPtr SendInitWithBytes(
		IntPtr receiver,
		IntPtr selector,
		byte[] bytes,
		nuint length,
		nuint encoding)
	{
		return objc_msgSend_initBytes(receiver, selector, bytes, length, encoding);
	}

	public static long SendInteger(IntPtr receiver, IntPtr selector)
	{
		return objc_msgSend_integer(receiver, selector);
	}

	public static nuint SendUnsigned(IntPtr receiver, IntPtr selector)
	{
		return objc_msgSend_unsigned(receiver, selector);
	}

	public static bool SendBool(IntPtr receiver, IntPtr selector, IntPtr argument)
	{
		return objc_msgSend_bool(receiver, selector, argument);
	}

	public static bool SendBool(IntPtr receiver, IntPtr selector, IntPtr first, IntPtr second)
	{
		return objc_msgSend_bool2(receiver, selector, first, second);
	}

	private static byte[] Utf8(string value)
	{
		var bytes = new byte[Encoding.UTF8.GetByteCount(value) + 1];
		Encoding.UTF8.GetBytes(value, bytes);
		return bytes;
	}

	[DllImport(LibObjC)]
	public static extern IntPtr objc_autoreleasePoolPush();

	[DllImport(LibObjC)]
	public static extern void objc_autoreleasePoolPop(IntPtr pool);

	[DllImport(LibObjC)]
	private static extern IntPtr objc_getClass(byte[] name);

	[DllImport(LibObjC)]
	private static extern IntPtr sel_registerName(byte[] name);

	[DllImport(LibObjC, EntryPoint = "objc_msgSend")]
	private static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector);

	[DllImport(LibObjC, EntryPoint = "objc_msgSend")]
	private static extern IntPtr objc_msgSend_ptr(IntPtr receiver, IntPtr selector, IntPtr argument);

	[DllImport(LibObjC, EntryPoint = "objc_msgSend")]
	private static extern IntPtr objc_msgSend_bytes(IntPtr receiver, IntPtr selector, byte[] argument);

	[DllImport(LibObjC, EntryPoint = "objc_msgSend")]
	private static extern IntPtr objc_msgSend_index(IntPtr receiver, IntPtr selector, nuint index);

	[DllImport(LibObjC, EntryPoint = "objc_msgSend")]
	private static extern IntPtr objc_msgSend_initBytes(
		IntPtr receiver,
		IntPtr selector,
		byte[] bytes,
		nuint length,
		nuint encoding);

	[DllImport(LibObjC, EntryPoint = "objc_msgSend")]
	private static extern long objc_msgSend_integer(IntPtr receiver, IntPtr selector);

	[DllImport(LibObjC, EntryPoint = "objc_msgSend")]
	private static extern nuint objc_msgSend_unsigned(IntPtr receiver, IntPtr selector);

	[DllImport(LibObjC, EntryPoint = "objc_msgSend")]
	[return: MarshalAs(UnmanagedType.I1)]
	private static extern bool objc_msgSend_bool(IntPtr receiver, IntPtr selector, IntPtr argument);

	[DllImport(LibObjC, EntryPoint = "objc_msgSend")]
	[return: MarshalAs(UnmanagedType.I1)]
	private static extern bool objc_msgSend_bool2(IntPtr receiver, IntPtr selector, IntPtr first, IntPtr second);
}
