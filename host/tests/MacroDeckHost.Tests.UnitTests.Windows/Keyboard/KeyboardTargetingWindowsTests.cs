using MacroDeckHost.Integrations.Keyboard;
using MacroDeckHost.Integrations.Keyboard.Native;

namespace MacroDeckHost.Tests.UnitTests.Windows.Keyboard;

[Platform("Win")]
public class KeyboardTargetingWindowsTests
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
	public void No_modifier_is_deliverable_to_a_background_window()
	{
		var provider = KeyboardInputProviderFactory.Create();

		Assert.That(provider.BackgroundModifiers, Is.EqualTo(KeyModifier.None));
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
