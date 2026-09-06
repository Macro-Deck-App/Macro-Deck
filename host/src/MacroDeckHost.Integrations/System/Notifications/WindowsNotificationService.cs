using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using MacroDeck.Sdk.Logging;
using Serilog;

namespace MacroDeckHost.Integrations.System.Notifications;

[SupportedOSPlatform("windows")]
internal sealed class WindowsNotificationService : INotificationService
{
	private const int NimAdd = 0x00;
	private const int NimDelete = 0x02;
	private const int NifIcon = 0x02;
	private const int NifInfo = 0x10;
	private const int NiifInfo = 0x01;
	private const int IdiInformation = 32516;

	private static readonly IntPtr _hwndMessage = new(-3);

	private static readonly ILogger _logger =
		IntegrationLog.For<WindowsNotificationService>(SystemIntegration.IntegrationId);

	private static int _nextId;

	public bool IsSupported => true;

	public Task ShowAsync(string title, string message, CancellationToken cancellationToken = default)
	{
		var window = CreateWindowExW(0,
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
			return Task.CompletedTask;
		}

		var data = new NotifyIconData
		{
			cbSize = (uint)Marshal.SizeOf<NotifyIconData>(),
			hWnd = window,
			uID = (uint)Interlocked.Increment(ref _nextId),
			uFlags = NifIcon | NifInfo,
			hIcon = LoadIconW(IntPtr.Zero, IdiInformation),
			szInfo = Truncate(message, 255),
			szInfoTitle = Truncate(title, 63),
			dwInfoFlags = NiifInfo
		};

		if (!Shell_NotifyIconW(NimAdd, ref data))
		{
			_logger.Warning("Shell_NotifyIcon (add) failed");
			DestroyWindow(window);
			return Task.CompletedTask;
		}

		_ = Task.Run(async () =>
			{
				try
				{
					await Task.Delay(TimeSpan.FromSeconds(8), CancellationToken.None);
				}
				finally
				{
					Shell_NotifyIconW(NimDelete, ref data);
					DestroyWindow(window);
				}
			},
			CancellationToken.None);

		return Task.CompletedTask;
	}

	private static string Truncate(string value, int maxLength)
		=> value.Length <= maxLength ? value : value[..maxLength];

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
