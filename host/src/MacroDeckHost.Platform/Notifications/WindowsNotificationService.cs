using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Serilog;

namespace MacroDeckHost.Platform.Notifications;

[SupportedOSPlatform("windows")]
internal sealed class WindowsNotificationService : IPlatformNotificationService
{
	private const int NimAdd = 0x00;
	private const int NimDelete = 0x02;
	private const int NifIcon = 0x02;
	private const int NifInfo = 0x10;
	private const int NiifInfo = 0x01;
	private const int IdiInformation = 32516;

	private static readonly IntPtr _hwndMessage = new(-3);
	private static readonly TimeSpan _defaultLifetime = TimeSpan.FromSeconds(8);

	private static int _nextId;

	private readonly ILogger _logger;
	private readonly TimeSpan _lifetime;

	public WindowsNotificationService(ILogger logger)
		: this(logger, _defaultLifetime)
	{
	}

	internal WindowsNotificationService(ILogger logger, TimeSpan lifetime)
	{
		_logger = logger.ForContext<WindowsNotificationService>();
		_lifetime = lifetime;
	}

	public bool IsSupported => true;

	public async Task ShowAsync(string title, string message, CancellationToken cancellationToken = default)
	{
		await ShowCoreAsync(title, message);
	}

	internal Task<IntPtr> ShowCoreAsync(string title, string message)
	{
		var data = new NotifyIconData
		{
			cbSize = (uint)Marshal.SizeOf<NotifyIconData>(),
			uID = (uint)Interlocked.Increment(ref _nextId),
			uFlags = NifIcon | NifInfo,
			hIcon = LoadIconW(IntPtr.Zero, IdiInformation),
			szInfo = Truncate(message, 255),
			szInfoTitle = Truncate(title, 63),
			dwInfoFlags = NiifInfo
		};

		var shown = new TaskCompletionSource<IntPtr>(TaskCreationOptions.RunContinuationsAsynchronously);

		// Win32 only lets the creating thread destroy a window, so one thread owns it from creation to removal.
		var thread = new Thread(() => ShowOnOwnThread(data, shown)) { IsBackground = true };
		thread.Start();
		return shown.Task;
	}

	private void ShowOnOwnThread(NotifyIconData data, TaskCompletionSource<IntPtr> shown)
	{
		var window = IntPtr.Zero;
		try
		{
			window = CreateWindowExW(0,
				"STATIC",
				"MacroDeckNotify",
				0,
				0,
				0,
				0,
				0,
				_hwndMessage,
				IntPtr.Zero,
				IntPtr.Zero,
				IntPtr.Zero);
			if (window == IntPtr.Zero)
			{
				_logger.Warning("Failed to create owner window for notification");
				return;
			}

			data.hWnd = window;
			if (!Shell_NotifyIconW(NimAdd, ref data))
			{
				_logger.Warning("Shell_NotifyIcon (add) failed");
				return;
			}

			shown.TrySetResult(window);
			Thread.Sleep(_lifetime);
			Shell_NotifyIconW(NimDelete, ref data);
		}
		catch (Exception exception)
		{
			_logger.Warning(exception, "Failed to show notification");
		}
		finally
		{
			if (window != IntPtr.Zero)
			{
				DestroyWindow(window);
			}

			shown.TrySetResult(IntPtr.Zero);
		}
	}

	private static string Truncate(string value, int maxLength)
	{
		return value.Length <= maxLength ? value : value[..maxLength];
	}

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	private struct NotifyIconData
	{
		public uint cbSize;
		public IntPtr hWnd;
		public uint uID;
		public uint uFlags;
		public uint uCallbackMessage;
		public IntPtr hIcon;

		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
		public string szTip;

		public uint dwState;
		public uint dwStateMask;

		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
		public string szInfo;

		public uint uVersionOrTimeout;

		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
		public string szInfoTitle;

		public uint dwInfoFlags;
		public Guid guidItem;
		public IntPtr hBalloonIcon;
	}

	[DllImport("shell32.dll", CharSet = CharSet.Unicode)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool Shell_NotifyIconW(int message, ref NotifyIconData data);

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	private static extern IntPtr LoadIconW(IntPtr hInstance, int lpIconName);

	[DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern IntPtr CreateWindowExW(
		uint exStyle,
		string className,
		string windowName,
		uint style,
		int x,
		int y,
		int width,
		int height,
		IntPtr parent,
		IntPtr menu,
		IntPtr instance,
		IntPtr param);

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool DestroyWindow(IntPtr hWnd);
}
