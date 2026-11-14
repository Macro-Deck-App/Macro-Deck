using MacroDeckHost.Platform.Applications;

namespace MacroDeckHost.Tests.UnitTests.Applications;

public class ApplicationWindowStateTests
{
	private static readonly WindowBounds _monitor = new(0, 0, 1920, 1080);

	[Test]
	public void An_application_that_does_not_run_is_not_running_whatever_windows_are_reported()
	{
		Assert.That(ApplicationWindowStates.Aggregate(0, [ApplicationWindowState.Normal]),
			Is.EqualTo(ApplicationWindowState.NotRunning));
	}

	[Test]
	public void A_running_application_without_windows_has_no_window()
	{
		Assert.That(ApplicationWindowStates.Aggregate(2, []), Is.EqualTo(ApplicationWindowState.NoWindow));
	}

	[Test]
	public void The_most_visible_window_decides_the_state()
	{
		Assert.Multiple(() =>
		{
			Assert.That(ApplicationWindowStates.Aggregate(1,
					[ApplicationWindowState.Minimized, ApplicationWindowState.Minimized]),
				Is.EqualTo(ApplicationWindowState.Minimized));
			Assert.That(ApplicationWindowStates.Aggregate(1,
					[ApplicationWindowState.Minimized, ApplicationWindowState.Normal]),
				Is.EqualTo(ApplicationWindowState.Normal));
			Assert.That(ApplicationWindowStates.Aggregate(1,
				[
					ApplicationWindowState.Fullscreen, ApplicationWindowState.Maximized, ApplicationWindowState.Normal
				]),
				Is.EqualTo(ApplicationWindowState.Fullscreen));
			Assert.That(ApplicationWindowStates.Aggregate(1,
					[ApplicationWindowState.Normal, ApplicationWindowState.Maximized]),
				Is.EqualTo(ApplicationWindowState.Maximized));
		});
	}

	[Test]
	public void States_have_stable_wire_values()
	{
		Assert.That(Enum.GetValues<ApplicationWindowState>().Select(ApplicationWindowStates.ToWireValue),
			Is.EqualTo(new[] { "not_running", "no_window", "minimized", "normal", "maximized", "fullscreen" }));
	}

	[Test]
	public void A_windows_window_maps_to_the_state_the_user_sees()
	{
		var maximizedOverhang = new WindowBounds(-8, -8, 1928, 1048);
		var borderlessCovering = new WindowBounds(0, 0, 1920, 1080);
		var zoomedCoveringWithAutoHideTaskbar = new WindowBounds(-8, -8, 1928, 1088);

		Assert.Multiple(() =>
		{
			Assert.That(ApplicationWindowStateMapper.FromWindows(true, true, true, maximizedOverhang, _monitor),
				Is.EqualTo(ApplicationWindowState.Minimized));
			Assert.That(ApplicationWindowStateMapper.FromWindows(false, false, false, borderlessCovering, _monitor),
				Is.EqualTo(ApplicationWindowState.Fullscreen));
			Assert.That(ApplicationWindowStateMapper.FromWindows(false, true, true, maximizedOverhang, _monitor),
				Is.EqualTo(ApplicationWindowState.Maximized));
			Assert.That(ApplicationWindowStateMapper.FromWindows(false,
					true,
					true,
					zoomedCoveringWithAutoHideTaskbar,
					_monitor),
				Is.EqualTo(ApplicationWindowState.Maximized));
			Assert.That(ApplicationWindowStateMapper.FromWindows(false,
					true,
					false,
					borderlessCovering,
					_monitor),
				Is.EqualTo(ApplicationWindowState.Fullscreen));
			Assert.That(ApplicationWindowStateMapper.FromWindows(false,
					false,
					true,
					new WindowBounds(100, 100, 900, 700),
					_monitor),
				Is.EqualTo(ApplicationWindowState.Normal));
		});
	}

	[Test]
	public void An_x11_window_maps_its_net_wm_state()
	{
		Assert.Multiple(() =>
		{
			Assert.That(ApplicationWindowStateMapper.FromX11(true, true, true, true),
				Is.EqualTo(ApplicationWindowState.Minimized));
			Assert.That(ApplicationWindowStateMapper.FromX11(false, true, false, false),
				Is.EqualTo(ApplicationWindowState.Fullscreen));
			Assert.That(ApplicationWindowStateMapper.FromX11(false, false, true, true),
				Is.EqualTo(ApplicationWindowState.Maximized));
			Assert.That(ApplicationWindowStateMapper.FromX11(false, false, true, false),
				Is.EqualTo(ApplicationWindowState.Normal));
		});
	}

	[Test]
	public void A_macos_window_maps_minimized_fullscreen_and_filled_windows()
	{
		var display = new WindowBounds(0, 0, 1512, 982);

		Assert.Multiple(() =>
		{
			Assert.That(ApplicationWindowStateMapper.FromMacOs(true, true, display, display),
				Is.EqualTo(ApplicationWindowState.Minimized));
			Assert.That(ApplicationWindowStateMapper.FromMacOs(false, true, new WindowBounds(0, 0, 800, 600), display),
				Is.EqualTo(ApplicationWindowState.Fullscreen));
			Assert.That(ApplicationWindowStateMapper.FromMacOs(false, false, display, display),
				Is.EqualTo(ApplicationWindowState.Fullscreen));
			Assert.That(
				ApplicationWindowStateMapper.FromMacOs(false, false, new WindowBounds(0, 33, 1512, 900), display),
				Is.EqualTo(ApplicationWindowState.Maximized));
			Assert.That(ApplicationWindowStateMapper.FromMacOs(false,
					false,
					new WindowBounds(50, 60, 1450, 860),
					display),
				Is.EqualTo(ApplicationWindowState.Normal));
		});
	}
}
