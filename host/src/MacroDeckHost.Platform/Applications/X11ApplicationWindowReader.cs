using System.Runtime.Versioning;
using MacroDeckHost.Platform.Native;

namespace MacroDeckHost.Platform.Applications;

[SupportedOSPlatform("linux")]
internal sealed class X11ApplicationWindowReader : IApplicationWindowReader
{
	private readonly global::System.Threading.Lock _gate = new();
	private IntPtr _display;
	private X11StateAtoms? _atoms;
	private bool _opened;
	private bool _disposed;

	public ApplicationWindowAvailability Availability
		=> LinuxDisplaySession.IsWayland
			? ApplicationWindowAvailability.Wayland
			: Environment.GetEnvironmentVariable("DISPLAY") is { Length: > 0 }
				? ApplicationWindowAvailability.Available
				: ApplicationWindowAvailability.Unsupported;

	public IReadOnlyDictionary<int, IReadOnlyList<ApplicationWindowState>> Read(IReadOnlySet<int> processIds)
	{
		var result = new Dictionary<int, IReadOnlyList<ApplicationWindowState>>();
		if (processIds.Count == 0 || Availability != ApplicationWindowAvailability.Available)
		{
			return result;
		}

		lock (_gate)
		{
			if (!EnsureDisplay() || _atoms is not { } atoms)
			{
				return result;
			}

			var root = X11Windows.DefaultRootWindow(_display);
			var grouped = new Dictionary<int, List<ApplicationWindowState>>();
			foreach (var window in X11Windows.ReadLongProperty(_display, root, atoms.ClientList))
			{
				if (X11Windows.GetWindowPid(_display, window) is not { } pid || !processIds.Contains(pid))
				{
					continue;
				}

				var states = X11Windows.ReadLongProperty(_display, window, atoms.State);
				if (states.Contains(atoms.SkipTaskbar))
				{
					continue;
				}

				if (!grouped.TryGetValue(pid, out var list))
				{
					list = [];
					grouped[pid] = list;
				}

				list.Add(ApplicationWindowStateMapper.FromX11(states.Contains(atoms.Hidden),
					states.Contains(atoms.Fullscreen),
					states.Contains(atoms.MaximizedVertically),
					states.Contains(atoms.MaximizedHorizontally)));
			}

			foreach (var (pid, list) in grouped)
			{
				result[pid] = list;
			}
		}

		return result;
	}

	public void Dispose()
	{
		lock (_gate)
		{
			if (_disposed)
			{
				return;
			}

			_disposed = true;
			if (_display != IntPtr.Zero)
			{
				X11Windows.CloseDisplay(_display);
				_display = IntPtr.Zero;
			}
		}
	}

	private bool EnsureDisplay()
	{
		if (_disposed)
		{
			return false;
		}

		if (!_opened)
		{
			_opened = true;
			try
			{
				_display = X11Windows.OpenDisplay();
			}
			catch (DllNotFoundException)
			{
				_display = IntPtr.Zero;
			}

			if (_display != IntPtr.Zero)
			{
				_atoms = new X11StateAtoms(X11Windows.InternAtom(_display, "_NET_CLIENT_LIST"),
					X11Windows.InternAtom(_display, "_NET_WM_STATE"),
					X11Windows.InternAtom(_display, "_NET_WM_STATE_HIDDEN"),
					X11Windows.InternAtom(_display, "_NET_WM_STATE_FULLSCREEN"),
					X11Windows.InternAtom(_display, "_NET_WM_STATE_MAXIMIZED_VERT"),
					X11Windows.InternAtom(_display, "_NET_WM_STATE_MAXIMIZED_HORZ"),
					X11Windows.InternAtom(_display, "_NET_WM_STATE_SKIP_TASKBAR"));
			}
		}

		return _display != IntPtr.Zero;
	}

	private sealed record X11StateAtoms(
		nuint ClientList,
		nuint State,
		nuint Hidden,
		nuint Fullscreen,
		nuint MaximizedVertically,
		nuint MaximizedHorizontally,
		nuint SkipTaskbar);
}
