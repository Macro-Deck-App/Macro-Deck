using MacroDeckHost.Integrations.Keyboard;
using MacroDeckHost.Integrations.Keyboard.Native;

namespace MacroDeckHost.Tests.UnitTests.MacOS.Keyboard;

[Platform("MacOsX")]
public class KeyboardTargetingMacOsTests
{
	[Test]
	public void Provider_reports_targeting_capabilities()
	{
		var provider = KeyboardInputProviderFactory.Create();

		Assert.Multiple(() =>
		{
			Assert.That(provider.SupportsWindowTargeting, Is.True);
			Assert.That(provider.SupportsBackgroundSend, Is.True);
		});
	}

	[Test]
	public void Foreground_detection_marshals_without_throwing()
	{
		var provider = KeyboardInputProviderFactory.Create();

		string? foreground = null;
		Assert.DoesNotThrow(() => foreground = provider.GetForegroundProcessName());
		TestContext.Out.WriteLine($"Foreground process: {foreground ?? "<none>"}");
	}

	[Test]
	public void Resolving_an_unknown_process_returns_null_without_throwing()
	{
		var provider = KeyboardInputProviderFactory.Create();
		IKeyboardTargetWindow? window = null;

		Assert.DoesNotThrow(() => window = provider.ResolveTarget("definitely-not-a-real-process-xyz"));
		Assert.That(window, Is.Null);
		window?.Dispose();
	}
}
