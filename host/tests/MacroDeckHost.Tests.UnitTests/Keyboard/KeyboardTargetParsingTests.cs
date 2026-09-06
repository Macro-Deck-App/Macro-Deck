using MacroDeckHost.Integrations.Keyboard;
using MacroDeckHost.Integrations.Keyboard.Actions;

namespace MacroDeckHost.Tests.UnitTests.Keyboard;

public class KeyboardTargetParsingTests
{
	[Test]
	public void Missing_process_is_none()
	{
		var target = KeyboardActionValues.ReadTarget(new Dictionary<string, object>());
		Assert.That(target.HasProcess, Is.False);
	}

	[Test]
	public void Blank_process_is_none_even_with_a_mode()
	{
		var target = KeyboardActionValues.ReadTarget(new Dictionary<string, object>
			{ ["targetProcess"] = "   ", ["targetMode"] = "background" });

		Assert.That(target.HasProcess, Is.False);
	}

	[TestCase("focused", KeyboardTargetMode.WhenFocused)]
	[TestCase("focus-send", KeyboardTargetMode.FocusThenSend)]
	[TestCase("background", KeyboardTargetMode.Background)]
	[TestCase("", KeyboardTargetMode.WhenFocused)]
	[TestCase("nonsense", KeyboardTargetMode.WhenFocused)]
	public void Parses_process_and_mode(string mode, KeyboardTargetMode expected)
	{
		var target = KeyboardActionValues.ReadTarget(new Dictionary<string, object>
			{ ["targetProcess"] = "code", ["targetMode"] = mode });

		Assert.Multiple(() =>
		{
			Assert.That(target.HasProcess, Is.True);
			Assert.That(target.ProcessName, Is.EqualTo("code"));
			Assert.That(target.Mode, Is.EqualTo(expected));
		});
	}
}

public class KeyboardProcessNameTests
{
	[TestCase("Code", "code", true)]
	[TestCase("Code.exe", "code", true)]
	[TestCase("code", "Code.app", true)]
	[TestCase("explorer", "code", false)]
	[TestCase(null, "code", false)]
	[TestCase("", "code", false)]
	public void Matches_is_case_and_extension_insensitive(string? actual, string configured, bool expected)
		=> Assert.That(KeyboardProcessName.Matches(actual, configured), Is.EqualTo(expected));
}
