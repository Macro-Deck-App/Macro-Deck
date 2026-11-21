using System.Runtime.Versioning;
using MacroDeckHost.Platform.Native;

namespace MacroDeckHost.Platform.Applications;

[SupportedOSPlatform("linux")]
internal sealed class X11ApplicationWindowReader : IApplicationWindowReader
{
	private readonly global::System.Threading.Lock _gate = new();
	private X11Connection? _connection;
	private X11StateAtoms? _atoms;
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
			if (_disposed)
			{
				return result;
			}

			_connection ??= new X11Connection(Attach,
				() => new PlatformNotSupportedException("The X11 display is not available."));
			if (!_connection.TryRun(display => ReadWindows(display, processIds, result)))
			{
				result.Clear();
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
			_connection?.Dispose();
		}
	}

	private bool Attach(IntPtr display)
	{
		_atoms = InternAtoms(display);
		return true;
	}

	private static X11StateAtoms InternAtoms(IntPtr display)
	{
		return new X11StateAtoms(X11Windows.InternAtom(display, "_NET_CLIENT_LIST"),
			X11Windows.InternAtom(display, "_NET_WM_STATE"),
			X11Windows.InternAtom(display, "_NET_WM_STATE_HIDDEN"),
			X11Windows.InternAtom(display, "_NET_WM_STATE_FULLSCREEN"),
			X11Windows.InternAtom(display, "_NET_WM_STATE_MAXIMIZED_VERT"),
			X11Windows.InternAtom(display, "_NET_WM_STATE_MAXIMIZED_HORZ"),
			X11Windows.InternAtom(display, "_NET_WM_STATE_SKIP_TASKBAR"));
	}

	private void ReadWindows(
		IntPtr display,
		IReadOnlySet<int> processIds,
		Dictionary<int, IReadOnlyList<ApplicationWindowState>> result)
	{
		// A window manager creates these atoms after it starts, which can be after this display connected.
		var atoms = _atoms is { ClientList: not 0, State: not 0 } cached ? cached : InternAtoms(display);
		_atoms = atoms;

		var root = X11Windows.DefaultRootWindow(display);
		var grouped = new Dictionary<int, List<ApplicationWindowState>>();
		foreach (var window in X11Windows.ReadLongProperty(display, root, atoms.ClientList))
		{
			if (X11Windows.GetWindowPid(display, window) is not { } pid || !processIds.Contains(pid))
			{
				continue;
			}

			var states = X11Windows.ReadLongProperty(display, window, atoms.State);
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

	private sealed record X11StateAtoms(
		nuint ClientList,
		nuint State,
		nuint Hidden,
		nuint Fullscreen,
		nuint MaximizedVertically,
		nuint MaximizedHorizontally,
		nuint SkipTaskbar);
}
