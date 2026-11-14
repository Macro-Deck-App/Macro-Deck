namespace MacroDeckHost.Platform.Applications;

internal enum ApplicationWindowState
{
	NotRunning,
	NoWindow,
	Minimized,
	Normal,
	Maximized,
	Fullscreen
}

internal static class ApplicationWindowStates
{
	public static ApplicationWindowState Aggregate(int processCount, IEnumerable<ApplicationWindowState> windows)
	{
		if (processCount <= 0)
		{
			return ApplicationWindowState.NotRunning;
		}

		var mostVisible = ApplicationWindowState.NoWindow;
		foreach (var window in windows)
		{
			if (window > mostVisible)
			{
				mostVisible = window;
			}
		}

		return mostVisible;
	}

	public static string ToWireValue(ApplicationWindowState state)
	{
		return state switch
		{
			ApplicationWindowState.NotRunning => "not_running",
			ApplicationWindowState.NoWindow => "no_window",
			ApplicationWindowState.Minimized => "minimized",
			ApplicationWindowState.Normal => "normal",
			ApplicationWindowState.Maximized => "maximized",
			ApplicationWindowState.Fullscreen => "fullscreen",
			_ => throw new ArgumentOutOfRangeException(nameof(state), state, null)
		};
	}
}
