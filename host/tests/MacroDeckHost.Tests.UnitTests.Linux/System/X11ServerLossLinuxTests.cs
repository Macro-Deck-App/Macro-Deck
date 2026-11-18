using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using MacroDeckHost.Integrations.Keyboard.Models;
using MacroDeckHost.Integrations.Keyboard.Native;
using MacroDeckHost.Integrations.Mouse.Models;
using MacroDeckHost.Integrations.Mouse.Native;
using MacroDeckHost.Platform.Focus;
using MacroDeckHost.Platform.Native;

namespace MacroDeckHost.Tests.UnitTests.Linux.System;

[Platform("Linux")]
[SupportedOSPlatform("linux")]
[NonParallelizable]
public class X11ServerLossLinuxTests
{
	private const string LibX11 = "libX11.so.6";
	private const int KeyPressType = 2;
	private const nint KeyPressMask = 1 << 0;
	private const nuint XaCardinal = 6;
	private const nuint XaWindow = 33;
	private const int PropModeReplace = 0;

	private static readonly TimeSpan _callLimit = TimeSpan.FromSeconds(10);

	[Test]
	public void A_connection_survives_repeated_server_losses_and_never_blocks_another_thread()
	{
		using var server = PrivateXServer.Start();
		using var connection = new X11Connection(_ => true,
			() => new PlatformNotSupportedException("lost"),
			server.Name);
		Assert.That(connection.IsAvailable, Is.True);

		for (var cycle = 0; cycle < 3; cycle++)
		{
			server.Kill();

			Assert.Throws<PlatformNotSupportedException>(() => Bounded(() => RoundTrip(connection)));
			Assert.Throws<PlatformNotSupportedException>(() => Bounded(() => RoundTrip(connection)));
			Assert.That(connection.IsAvailable, Is.False);

			server.Restart();

			WaitUntil(() => connection.IsAvailable);
			Assert.That(Bounded(() => RoundTrip(connection)), Is.Not.Zero);
		}
	}

	[Test]
	public async Task Keyboard_and_mouse_report_the_lost_display_as_unsupported_and_recover_when_it_returns()
	{
		using var server = PrivateXServer.Start();
		using var keyboardProvider = new LinuxKeyboardInputProvider(server.Name);
		using var mouseProvider = new LinuxMouseInputProvider(server.Name);
		using var focus = new LinuxFocusedWindowReader(server.Name);
		var keyboard = new KeyboardInputService(keyboardProvider, new KeyboardLayoutService());
		var mouse = new MouseInputService(mouseProvider);
		Assert.That(keyboard.IsSupported && mouse.IsSupported && focus.IsSupported, Is.True);

		server.Kill();

		var keyboardFailure = Assert.Throws<PlatformNotSupportedException>(()
			=> Bounded(() => keyboard.PressComboAsync(KeyModifier.None, KeyCode.A).GetAwaiter().GetResult()));
		Assert.Throws<PlatformNotSupportedException>(()
			=> Bounded(() => mouse.MoveAsync(MouseTarget.By(1, 1)).GetAwaiter().GetResult()));
		Assert.That(Bounded(focus.Read), Is.Null);
		Assert.Multiple(() =>
		{
			Assert.That(keyboardFailure!.Message,
				Is.EqualTo(KeyboardInputService.Unavailable(keyboardProvider.PlatformName).Message));
			Assert.That(keyboard.IsSupported, Is.False);
			Assert.That(mouse.IsSupported, Is.False);
			Assert.That(focus.IsSupported, Is.False);
		});
		Assert.Throws<PlatformNotSupportedException>(()
			=> Bounded(() => keyboard.TypeTextAsync("a").GetAwaiter().GetResult()));

		server.Restart();

		WaitUntil(() => keyboard.IsSupported && mouse.IsSupported && focus.IsSupported);
		Bounded(() => keyboard.PressComboAsync(KeyModifier.None, KeyCode.A).GetAwaiter().GetResult());
		Bounded(() => mouse.MoveAsync(MouseTarget.At(7, 9)).GetAwaiter().GetResult());
		Assert.That(mouseProvider.TryGetPosition(out var position), Is.True);
		Assert.That(position, Is.EqualTo(new MousePoint(7, 9)));
		await keyboard.ReleaseAllAsync();
	}

	[Test]
	public void Keyboard_mouse_and_focus_losing_the_display_at_the_same_time_keep_the_process_alive()
	{
		using var server = PrivateXServer.Start();
		using var keyboardProvider = new LinuxKeyboardInputProvider(server.Name);
		using var mouseProvider = new LinuxMouseInputProvider(server.Name);
		using var focus = new LinuxFocusedWindowReader(server.Name);
		var keyboard = new KeyboardInputService(keyboardProvider, new KeyboardLayoutService());
		var mouse = new MouseInputService(mouseProvider);

		for (var cycle = 0; cycle < 3; cycle++)
		{
			WaitUntil(() => keyboard.IsSupported && mouse.IsSupported && focus.IsSupported);
			Bounded(() => keyboard.TypeTextAsync("a").GetAwaiter().GetResult());
			server.Kill();

			var typing = Task.Run(() => keyboard.TypeTextAsync("aä€"));
			var moving = Task.Run(() => mouse.MoveAsync(MouseTarget.At(5, 5)));
			var reading = Task.Run(focus.Read);
			Bounded(() => Task.WhenAll(typing, moving, reading).ContinueWith(_ => true).GetAwaiter().GetResult());

			Assert.Multiple(() =>
			{
				Assert.That(typing.Exception?.InnerException, Is.TypeOf<PlatformNotSupportedException>());
				Assert.That(moving.Exception?.InnerException, Is.TypeOf<PlatformNotSupportedException>());
				Assert.That(reading.Result, Is.Null);
			});
			server.Restart();
		}
	}

	[Test]
	public void A_background_key_still_held_when_the_server_restarts_does_not_block_the_next_one()
	{
		using var server = PrivateXServer.Start();
		using var provider = new LinuxKeyboardInputProvider(server.Name);
		var keyboard = new KeyboardInputService(provider, new KeyboardLayoutService());
		var target = new KeyboardTarget(Process.GetCurrentProcess().ProcessName, KeyboardTargetMode.Background);

		using (var window = TargetWindow.Create(server.Name))
		{
			Assert.That(Bounded(() => HoldInBackground(keyboard, KeyCode.A, target)), Is.Null);
			Assert.That(window.ReceivedKeyPress(), Is.True);
			server.Kill();
		}

		server.Restart();
		WaitUntil(() => keyboard.IsSupported);

		using var restarted = TargetWindow.Create(server.Name);
		Assert.That(Bounded(() => HoldInBackground(keyboard, KeyCode.B, target)), Is.Null);
		Assert.That(restarted.ReceivedKeyPress(), Is.True);
		Bounded(() => keyboard.ReleaseAllAsync().GetAwaiter().GetResult());
	}

	[Test]
	public void Releasing_a_background_key_held_before_the_server_restarted_completes_and_holding_works_again()
	{
		using var server = PrivateXServer.Start();
		using var provider = new LinuxKeyboardInputProvider(server.Name);
		var keyboard = new KeyboardInputService(provider, new KeyboardLayoutService());
		var target = new KeyboardTarget(Process.GetCurrentProcess().ProcessName, KeyboardTargetMode.Background);

		using (TargetWindow.Create(server.Name))
		{
			Assert.That(Bounded(() => HoldInBackground(keyboard, KeyCode.A, target)), Is.Null);
			server.Kill();
		}

		server.Restart();
		WaitUntil(() => keyboard.IsSupported);

		Bounded(() => keyboard.KeyUpAsync(KeyModifier.None, KeyCode.A).GetAwaiter().GetResult());
		using var restarted = TargetWindow.Create(server.Name);
		Assert.That(Bounded(() => HoldInBackground(keyboard, KeyCode.A, target)), Is.Null);
		Assert.That(restarted.ReceivedKeyPress(), Is.True);
		Bounded(() => keyboard.ReleaseAllAsync().GetAwaiter().GetResult());
	}

	private static KeyboardSessionUnavailableReason? HoldInBackground(
		KeyboardInputService keyboard,
		KeyCode key,
		KeyboardTarget target)
	{
		return keyboard.KeyDownAsync(KeyModifier.None, key, target).GetAwaiter().GetResult();
	}

	private static nuint RoundTrip(X11Connection connection)
	{
		// Xlib answers predefined and already seen atoms locally, so only a new atom is a real round trip.
		var name = Encoding.UTF8.GetBytes($"MACRO_DECK_LOSS_PROBE_{Guid.NewGuid():N}\0");
		return connection.Run(display => XInternAtom(display, name, false));
	}

	private static T Bounded<T>(Func<T> call)
	{
		T result = default!;
		Exception? failure = null;
		var thread = new Thread(() =>
		{
			try
			{
				result = call();
			}
			catch (Exception e)
			{
				failure = e;
			}
		}) { IsBackground = true };
		thread.Start();
		if (!thread.Join(_callLimit))
		{
			Assert.Fail($"An X11 call did not return within {_callLimit.TotalSeconds}s.");
		}

		if (failure is not null)
		{
			throw failure;
		}

		return result;
	}

	private static void Bounded(Action call)
	{
		_ = Bounded(() =>
		{
			call();
			return true;
		});
	}

	private static void WaitUntil(Func<bool> condition)
	{
		var deadline = Stopwatch.StartNew();
		while (!Bounded(condition))
		{
			if (deadline.Elapsed > _callLimit)
			{
				Assert.Fail("The X11 display did not become available again.");
			}

			Thread.Sleep(100);
		}
	}

	private sealed class PrivateXServer : IDisposable
	{
		private readonly int _number;
		private Process? _process;

		private PrivateXServer(int number)
		{
			_number = number;
		}

		public string Name => $":{_number}";

		public static PrivateXServer Start()
		{
			Assume.That(X11Windows.SurvivesServerLoss,
				Is.True,
				"needs libX11 1.7 or newer, older versions always exit on a server loss");
			var number = Enumerable.Range(140, 60)
				.First(n => !File.Exists($"/tmp/.X{n}-lock") && !File.Exists($"/tmp/.X11-unix/X{n}"));
			var server = new PrivateXServer(number);
			server.Restart();
			return server;
		}

		public void Restart()
		{
			try
			{
				_process = Process.Start(new ProcessStartInfo("Xvfb",
					$"{Name} -screen 0 640x480x24 -nolisten tcp")
				{
					RedirectStandardError = true,
					RedirectStandardOutput = true
				});
			}
			catch (Win32Exception)
			{
				Assume.That(false, "needs Xvfb on PATH");
			}

			_process!.BeginErrorReadLine();
			_process.BeginOutputReadLine();
			var socket = $"/tmp/.X11-unix/X{_number}";
			var deadline = Stopwatch.StartNew();
			while (!File.Exists(socket) && deadline.Elapsed < _callLimit)
			{
				Thread.Sleep(50);
			}

			Assert.That(File.Exists(socket), Is.True, "Xvfb did not start");
		}

		public void Kill()
		{
			if (_process is null)
			{
				return;
			}

			_process.Kill();
			_process.WaitForExit();
			_process.Dispose();
			_process = null;
			File.Delete($"/tmp/.X11-unix/X{_number}");
			File.Delete($"/tmp/.X{_number}-lock");
		}

		public void Dispose()
		{
			Kill();
		}
	}

	private sealed class TargetWindow : IDisposable
	{
		private readonly IntPtr _display;
		private readonly nuint _window;

		private TargetWindow(IntPtr display, nuint window)
		{
			_display = display;
			_window = window;
		}

		public static TargetWindow Create(string displayName)
		{
			var display = X11Windows.OpenDisplay(displayName);
			Assert.That(display, Is.Not.EqualTo(IntPtr.Zero));
			var root = XDefaultRootWindow(display);
			var window = XCreateSimpleWindow(display, root, 0, 0, 10, 10, 0, 0, 0);
			_ = XSelectInput(display, window, KeyPressMask);
			_ = XChangeProperty(display,
				window,
				XInternAtom(display, "_NET_WM_PID\0"u8.ToArray(), false),
				XaCardinal,
				32,
				PropModeReplace,
				[(nint)Environment.ProcessId],
				1);
			_ = XChangeProperty(display,
				root,
				XInternAtom(display, "_NET_CLIENT_LIST\0"u8.ToArray(), false),
				XaWindow,
				32,
				PropModeReplace,
				[(nint)window],
				1);
			_ = XSync(display, false);
			return new TargetWindow(display, window);
		}

		public bool ReceivedKeyPress()
		{
			_ = XSync(_display, false);
			var buffer = Marshal.AllocHGlobal(192);
			try
			{
				return XCheckTypedWindowEvent(_display, _window, KeyPressType, buffer);
			}
			finally
			{
				Marshal.FreeHGlobal(buffer);
			}
		}

		public void Dispose()
		{
			_ = XSync(_display, false);
			if (X11Windows.IsLost(_display))
			{
				X11Windows.Forget(_display);
				return;
			}

			X11Windows.CloseDisplay(_display);
		}
	}

	[DllImport(LibX11)]
	private static extern nuint XDefaultRootWindow(IntPtr display);

	[DllImport(LibX11)]
	private static extern nuint XInternAtom(
		IntPtr display,
		byte[] name,
		[MarshalAs(UnmanagedType.Bool)] bool onlyIfExists);

	[DllImport(LibX11)]
	private static extern nuint XCreateSimpleWindow(
		IntPtr display,
		nuint parent,
		int x,
		int y,
		uint width,
		uint height,
		uint borderWidth,
		nuint border,
		nuint background);

	[DllImport(LibX11)]
	private static extern int XSelectInput(IntPtr display, nuint window, nint eventMask);

	[DllImport(LibX11)]
	private static extern int XChangeProperty(
		IntPtr display,
		nuint window,
		nuint property,
		nuint type,
		int format,
		int mode,
		nint[] data,
		int elementCount);

	[DllImport(LibX11)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool XCheckTypedWindowEvent(IntPtr display, nuint window, int eventType, IntPtr eventReturn);

	[DllImport(LibX11)]
	private static extern int XSync(IntPtr display, [MarshalAs(UnmanagedType.Bool)] bool discard);
}
