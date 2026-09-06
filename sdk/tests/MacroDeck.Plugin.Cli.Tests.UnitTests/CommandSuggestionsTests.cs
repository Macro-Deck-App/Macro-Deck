namespace MacroDeck.Plugin.Cli.Tests.UnitTests;

/// <summary><see cref="CommandSuggestions.For" /> as a pure function, against the five real command names -
/// never a hand-picked stand-in list, so a future command addition or rename cannot quietly desync this
/// test from what <c>CliEntryPoint</c> actually offers.</summary>
[TestFixture]
public class CommandSuggestionsTests
{
	private static List<string> RealCommandNames() =>
		CliEntryPoint.CreateRootCommand().Subcommands.Select(command => command.Name).ToList();

	[Test]
	public void A_single_character_typo_suggests_the_intended_command()
	{
		// The issue's own example: "pakc" is one transposition away from "pack".
		var suggestion = CommandSuggestions.For("pakc", RealCommandNames());

		Assert.That(suggestion, Is.EqualTo("pack"));
	}

	[Test]
	public void A_token_resembling_no_command_suggests_nothing()
	{
		var suggestion = CommandSuggestions.For("zzzzzzzz", RealCommandNames());

		Assert.That(suggestion, Is.Null);
	}
}
