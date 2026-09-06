using MacroDeckHost.Ui;

namespace MacroDeckHost.Tests.UnitTests.Host;

/// <summary>
/// Discovery of packaged device-specific Web Client builds (issue #727). Each discovered id becomes
/// a route template and a file path, so what is not discovered matters as much as what is.
/// </summary>
[TestFixture]
public class WebClientTargetsTests
{
	private string _webRoot = string.Empty;

	[SetUp]
	public void SetUp()
	{
		_webRoot = Path.Combine(Path.GetTempPath(), "macro-deck-tests", Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(_webRoot);
	}

	[TearDown]
	public void TearDown()
	{
		if (Directory.Exists(_webRoot))
		{
			Directory.Delete(_webRoot, recursive: true);
		}
	}

	[Test]
	public void A_packaged_target_is_discovered()
	{
		GivenTarget("carthing", withShell: true);

		Assert.That(WebClientTargets.Discover(_webRoot), Is.EqualTo(new[] { "carthing" }));
	}

	[Test]
	public void A_directory_without_a_shell_is_not_a_target()
	{
		GivenTarget("carthing", withShell: true);
		GivenTarget("leftovers", withShell: false);

		Assert.That(WebClientTargets.Discover(_webRoot),
			Is.EqualTo(new[] { "carthing" }),
			"mapping a fallback at a directory with no index.html would answer every asset below it with a 404");
	}

	[Test]
	public void Several_targets_are_discovered_in_a_stable_order()
	{
		GivenTarget("zebra", withShell: true);
		GivenTarget("carthing", withShell: true);

		Assert.That(WebClientTargets.Discover(_webRoot), Is.EqualTo(new[] { "carthing", "zebra" }));
	}

	[Test]
	public void Nothing_is_discovered_when_the_client_is_not_packaged()
	{
		Assert.Multiple(() =>
		{
			Assert.That(WebClientTargets.Discover(_webRoot), Is.Empty, "no targets directory at all");
			Assert.That(WebClientTargets.Discover(null), Is.Empty);
			Assert.That(WebClientTargets.Discover(string.Empty), Is.Empty);
		});
	}

	[TestCase("carthing", ExpectedResult = true)]
	[TestCase("web-client-2", ExpectedResult = true)]
	[TestCase("a", ExpectedResult = true)]
	[TestCase("", ExpectedResult = false)]
	[TestCase("-leading", ExpectedResult = false)]
	[TestCase("Upper",
		ExpectedResult = false,
		Description = "ids appear in URLs and file paths, so case is closed too")]
	[TestCase("has space", ExpectedResult = false)]
	[TestCase("..", ExpectedResult = false)]
	[TestCase("a/b", ExpectedResult = false)]
	[TestCase("a{b}", ExpectedResult = false, Description = "route templates would read braces as a parameter")]
	public bool Ids_are_drawn_from_a_closed_character_set(string id) => WebClientTargets.IsValidId(id);

	[Test]
	public void An_id_outside_the_character_set_is_not_discovered()
	{
		GivenTarget("Not Valid", withShell: true);

		Assert.That(WebClientTargets.Discover(_webRoot), Is.Empty);
	}

	private void GivenTarget(string id, bool withShell)
	{
		var directory = Path.Combine(_webRoot, WebClientTargets.RootDirectoryName, id);
		Directory.CreateDirectory(directory);
		if (withShell)
		{
			File.WriteAllText(Path.Combine(directory, "index.html"), "<!doctype html>");
		}
	}
}
