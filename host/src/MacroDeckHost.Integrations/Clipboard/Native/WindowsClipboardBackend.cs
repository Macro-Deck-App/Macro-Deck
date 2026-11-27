using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using static MacroDeckHost.Integrations.Clipboard.Native.WindowsClipboardNative;

namespace MacroDeckHost.Integrations.Clipboard.Native;

[SupportedOSPlatform("windows")]
internal sealed class WindowsClipboardBackend : IClipboardBackend
{
	private const int OpenAttempts = 10;
	private const int OpenRetryMilliseconds = 30;
	private const int WhiteBackground = unchecked((int)0xFFFFFFFF);

	private static readonly Lazy<WindowsMessageWindow> _owner
		= new(() => new WindowsMessageWindow(), LazyThreadSafetyMode.ExecutionAndPublication);

	private static readonly Lazy<uint> _pngFormat = new(() => RegisterClipboardFormatW("PNG"));
	private static readonly Lazy<bool> _gdiplus = new(StartGdiplus);

	public ClipboardAvailability Availability => ClipboardAvailability.Available;

	public Task<ClipboardContentKinds> ReadKindsAsync(CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		return Task.FromResult(ReadKinds());
	}

	public Task<ClipboardTextReading> ReadTextAsync(int maxChars, CancellationToken cancellationToken)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(maxChars);
		return RunAsync(owner => WithClipboard(owner, () => ReadText(maxChars)), cancellationToken);
	}

	public Task<ClipboardWriteReceipt> SetTextAsync(string text, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(text);
		return RunAsync(owner => Write(owner, [Text(text)]), cancellationToken);
	}

	public Task<ClipboardWriteReceipt> SetImageAsync(string path, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(path);
		return RunAsync(owner => Write(owner, Image(path)), cancellationToken);
	}

	public Task<ClipboardWriteReceipt> SetFilesAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(paths);
		return RunAsync(owner => Write(owner, [Global(CfHdrop, WindowsDropFiles.Build(paths))]), cancellationToken);
	}

	public Task<ClipboardWriteReceipt> ClearAsync(CancellationToken cancellationToken)
	{
		return RunAsync(owner => Write(owner, []), cancellationToken);
	}

	public IClipboardChangeSource CreateChangeSource()
	{
		return new WindowsClipboardChangeSource();
	}

	internal static ClipboardContentKinds ReadKinds()
	{
		var kinds = ClipboardContentKinds.None;
		if (IsClipboardFormatAvailable(CfUnicodeText) || IsClipboardFormatAvailable(CfText))
		{
			kinds |= ClipboardContentKinds.Text;
		}

		if (IsClipboardFormatAvailable(CfBitmap)
			|| IsClipboardFormatAvailable(CfDib)
			|| IsClipboardFormatAvailable(CfDibV5)
			|| _pngFormat.Value != 0 && IsClipboardFormatAvailable(_pngFormat.Value))
		{
			kinds |= ClipboardContentKinds.Image;
		}

		if (IsClipboardFormatAvailable(CfHdrop))
		{
			kinds |= ClipboardContentKinds.Files;
		}

		return kinds == ClipboardContentKinds.None && CountClipboardFormats() > 0
			? ClipboardContentKinds.Other
			: kinds;
	}

	private static async Task<T> RunAsync<T>(Func<IntPtr, T> work, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		return await _owner.Value.InvokeAsync(work).WaitAsync(cancellationToken).ConfigureAwait(false);
	}

	private static T WithClipboard<T>(IntPtr owner, Func<T> work)
	{
		Open(owner);
		try
		{
			return work();
		}
		finally
		{
			_ = CloseClipboard();
		}
	}

	private static void Open(IntPtr owner)
	{
		for (var attempt = 0; attempt < OpenAttempts; attempt++)
		{
			if (OpenClipboard(owner))
			{
				return;
			}

			if (attempt < OpenAttempts - 1)
			{
				Thread.Sleep(OpenRetryMilliseconds);
			}
		}

		throw new ClipboardException(ClipboardFailure.Locked);
	}

	private static ClipboardTextReading ReadText(int maxChars)
	{
		if (!IsClipboardFormatAvailable(CfUnicodeText))
		{
			return ClipboardTextReading.None;
		}

		var handle = GetClipboardData(CfUnicodeText);
		if (handle == IntPtr.Zero)
		{
			throw new ClipboardException(ClipboardFailure.Failed);
		}

		var capacity = (long)(GlobalSize(handle) / sizeof(char));
		if (capacity == 0)
		{
			throw new ClipboardException(ClipboardFailure.Failed);
		}

		var pointer = GlobalLock(handle);
		if (pointer == IntPtr.Zero)
		{
			throw new ClipboardException(ClipboardFailure.Failed);
		}

		try
		{
			var count = (int)Math.Min(capacity, (long)maxChars + 1);
			var buffer = new char[count];
			Marshal.Copy(pointer, buffer, 0, count);
			var terminator = Array.IndexOf(buffer, '\0');
			var length = terminator >= 0 ? terminator : count;
			return length > maxChars
				? ClipboardTextReading.TooLarge
				: ClipboardTextReading.Of(new string(buffer, 0, length));
		}
		finally
		{
			_ = GlobalUnlock(handle);
		}
	}

	private static ClipboardWriteReceipt Write(IntPtr owner, IReadOnlyList<ClipboardData> data)
	{
		var handedOver = 0;
		try
		{
			Open(owner);
			try
			{
				if (!EmptyClipboard())
				{
					throw new ClipboardException(ClipboardFailure.Failed);
				}

				foreach (var item in data)
				{
					if (SetClipboardData(item.Format, item.Handle) == IntPtr.Zero)
					{
						throw new ClipboardException(ClipboardFailure.Failed);
					}

					// Once SetClipboardData succeeds the system owns the handle, so it must not be freed here.
					handedOver++;
				}
			}
			finally
			{
				_ = CloseClipboard();
			}
		}
		finally
		{
			for (var index = handedOver; index < data.Count; index++)
			{
				data[index].Release();
			}
		}

		return new ClipboardWriteReceipt(GetClipboardSequenceNumber());
	}

	private static ClipboardData Text(string text)
	{
		var bytes = new byte[(text.Length + 1) * sizeof(char)];
		Buffer.BlockCopy(text.ToCharArray(), 0, bytes, 0, text.Length * sizeof(char));
		return Global(CfUnicodeText, bytes);
	}

	private static ClipboardData Global(uint format, byte[] bytes)
	{
		var handle = GlobalAlloc(GmemMoveable, (nuint)bytes.Length);
		if (handle == IntPtr.Zero)
		{
			throw new ClipboardException(ClipboardFailure.Failed);
		}

		var pointer = GlobalLock(handle);
		if (pointer == IntPtr.Zero)
		{
			_ = GlobalFree(handle);
			throw new ClipboardException(ClipboardFailure.Failed);
		}

		Marshal.Copy(bytes, 0, pointer, bytes.Length);
		_ = GlobalUnlock(handle);
		return new ClipboardData(format, handle, false);
	}

	private static List<ClipboardData> Image(string path)
	{
		if (!File.Exists(path))
		{
			throw new ClipboardException(ClipboardFailure.NotFound);
		}

		if (!_gdiplus.Value)
		{
			throw new ClipboardException(ClipboardFailure.Unavailable);
		}

		var png = string.Equals(Path.GetExtension(path), ".png", StringComparison.OrdinalIgnoreCase)
			&& _pngFormat.Value != 0
				? File.ReadAllBytes(path)
				: null;

		if (GdipCreateBitmapFromFile(path, out var bitmap) != 0 || bitmap == IntPtr.Zero)
		{
			throw new ClipboardException(ClipboardFailure.UnsupportedFormat);
		}

		IntPtr hbitmap;
		try
		{
			if (GdipCreateHBITMAPFromBitmap(bitmap, out hbitmap, WhiteBackground) != 0 || hbitmap == IntPtr.Zero)
			{
				throw new ClipboardException(ClipboardFailure.UnsupportedFormat);
			}
		}
		finally
		{
			_ = GdipDisposeImage(bitmap);
		}

		var data = new List<ClipboardData>();
		if (png is not null)
		{
			try
			{
				data.Add(Global(_pngFormat.Value, png));
			}
			catch
			{
				_ = DeleteObject(hbitmap);
				throw;
			}
		}

		data.Add(new ClipboardData(CfBitmap, hbitmap, true));
		return data;
	}

	private static bool StartGdiplus()
	{
		var input = new GdiplusStartupInput { GdiplusVersion = 1 };
		try
		{
			return GdiplusStartup(out _, ref input, IntPtr.Zero) == 0;
		}
		catch (DllNotFoundException)
		{
			return false;
		}
	}

	private readonly record struct ClipboardData(uint Format, IntPtr Handle, bool IsBitmap)
	{
		public void Release()
		{
			if (IsBitmap)
			{
				_ = DeleteObject(Handle);
			}
			else
			{
				_ = GlobalFree(Handle);
			}
		}
	}
}

[SupportedOSPlatform("windows")]
internal sealed class WindowsClipboardChangeSource : IClipboardChangeSource
{
	private readonly Lock _gate = new();

	private WindowsMessageWindow? _window;
	private bool _unsupported;
	private bool _disposed;

	public event Action<long?>? Changed;

	public event Action? WatchUnsupported;

	public bool IsWatchUnsupported => Volatile.Read(ref _unsupported);

	public void Start()
	{
		lock (_gate)
		{
			if (_disposed || _window is not null)
			{
				return;
			}

			_window = new WindowsMessageWindow(OnMessage,
				AddClipboardFormatListener,
				hwnd => _ = RemoveClipboardFormatListener(hwnd));
			_ = _window.Ready.ContinueWith(OnReady,
				CancellationToken.None,
				TaskContinuationOptions.ExecuteSynchronously,
				TaskScheduler.Default);
		}
	}

	public void Dispose()
	{
		WindowsMessageWindow? window;
		lock (_gate)
		{
			_disposed = true;
			window = _window;
			_window = null;
		}

		window?.Dispose();
	}

	private void OnReady(Task<IntPtr> ready)
	{
		if (!ready.IsFaulted || Volatile.Read(ref _disposed))
		{
			return;
		}

		Volatile.Write(ref _unsupported, true);
		WatchUnsupported?.Invoke();
	}

	private bool OnMessage(uint message)
	{
		if (message != WmClipboardUpdate)
		{
			return false;
		}

		if (!Volatile.Read(ref _disposed))
		{
			Changed?.Invoke(GetClipboardSequenceNumber());
		}

		return true;
	}
}
