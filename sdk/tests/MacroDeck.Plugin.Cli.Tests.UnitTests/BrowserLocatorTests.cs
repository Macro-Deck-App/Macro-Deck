using MacroDeck.Plugin.Cli.Rendering;

namespace MacroDeck.Plugin.Cli.Tests.UnitTests;

[TestFixture]
public class BrowserLocatorTests
{
	[Test]
	public void An_explicit_path_wins_over_the_environment_variable()
	{
		var found = BrowserLocator.Find("/opt/mine/chrome", Env(("MACRODECK_BROWSER", "/opt/env/chrome")), _ => true);

		Assert.That(found, Is.EqualTo("/opt/mine/chrome"));
	}

	[Test]
	public void The_environment_variable_is_used_when_no_path_is_given()
	{
		var found = BrowserLocator.Find(null, Env(("MACRODECK_BROWSER", "/opt/env/chrome")), _ => true);

		Assert.That(found, Is.EqualTo("/opt/env/chrome"));
	}

	[Test]
	public void An_explicit_path_that_does_not_exist_is_not_replaced_by_a_guess()
	{
		var found = BrowserLocator.Find("/opt/missing", Env(("PATH", "/usr/bin")), path => path == "/usr/bin/chromium");

		Assert.That(found, Is.Null);
	}

	[Test]
	public void A_browser_on_the_path_is_found_by_its_usual_name()
	{
		var directory = Path.Combine(Path.GetTempPath(), "bin");
		var expected = Path.Combine(directory, OperatingSystem.IsWindows() ? "chromium.exe" : "chromium");

		var found = BrowserLocator.Find(null, Env(("PATH", directory)), path => path == expected);

		Assert.That(found, Is.EqualTo(expected));
	}

	[Test]
	public void Nothing_is_found_when_nothing_exists()
		=> Assert.That(BrowserLocator.Find(null, Env(("PATH", "/usr/bin")), _ => false), Is.Null);

	private static Func<string, string?> Env(params (string Name, string Value)[] values)
		=> name => values.FirstOrDefault(pair => pair.Name == name).Value;
}
