using System.Text.Json;
using MacroDeck.Sdk.Colors;

namespace MacroDeck.Sdk.Tests.UnitTests.Colors;

[TestFixture]
public class ColorGrammarVectorTests
{
	private static readonly JsonElement _fixture = Load();

	private static JsonElement Load()
	{
		var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
		while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "ui-model", "fixtures")))
		{
			directory = directory.Parent;
		}

		var path = Path.Combine(directory!.FullName, "ui-model", "fixtures", "colors", "color-references.json");
		return JsonDocument.Parse(File.ReadAllText(path)).RootElement.Clone();
	}

	private static IEnumerable<TestCaseData> Cases(string section)
		=> _fixture.GetProperty(section)
			.EnumerateArray()
			.Select((vector, index) => new TestCaseData(vector).SetName($"{section}[{index}]"));

	private static IEnumerable<TestCaseData> ParseCases() => Cases("parse");

	private static IEnumerable<TestCaseData> ReferenceCases() => Cases("references");

	[TestCaseSource(nameof(ParseCases))]
	public async Task A_fixed_colour_resolves_to_its_canonical_form(JsonElement vector)
	{
		var expected = vector.GetProperty("canonical") is { ValueKind: JsonValueKind.String } canonical
			? canonical.GetString()
			: null;
		IIntegrationContext context = new MinimalContext();

		Assert.That(await context.Colors.ResolveAsync(vector.GetProperty("input").GetString()!), Is.EqualTo(expected));
	}

	[TestCaseSource(nameof(ReferenceCases))]
	public void Only_the_reference_grammar_within_its_bounds_is_a_reference(JsonElement vector)
		=> Assert.That(ColorReference.IsReference(vector.GetProperty("text").GetString()),
			Is.EqualTo(vector.GetProperty("isReference").GetBoolean()));

	[Test]
	public async Task A_context_from_a_macro_deck_without_colours_resolves_fixed_colours_and_no_references()
	{
		IIntegrationContext context = new MinimalContext();
		var received = new TaskCompletionSource<string?>();

		var literal = await context.Colors.ResolveAsync("#ABC");
		var reference = await context.Colors.ResolveAsync("{{ vars.primary | color }}");
		await context.Colors.WatchAsync("#3366FF", (color, _) =>
		{
			received.TrySetResult(color);
			return Task.CompletedTask;
		});

		Assert.Multiple(async () =>
		{
			Assert.That(literal, Is.EqualTo("#aabbcc"));
			Assert.That(reference, Is.Null);
			Assert.That(await received.Task.WaitAsync(TimeSpan.FromSeconds(5)), Is.EqualTo("#3366ff"));
		});
	}

	private sealed class MinimalContext : IIntegrationContext
	{
		public MacroDeck.Sdk.Variables.IVariableApi Variables => throw new NotSupportedException();

		public MacroDeck.Sdk.Variables.IUserVariableApi UserVariables => throw new NotSupportedException();

		public MacroDeck.Sdk.ConfigFlow.IIntegrationConfig Config => throw new NotSupportedException();

		public MacroDeck.Sdk.Decks.IDeckNavigator Deck => throw new NotSupportedException();

		public MacroDeck.Sdk.Scripts.IScriptApi Scripts => throw new NotSupportedException();

		public MacroDeck.Sdk.Widgets.IWidgetApi Widgets => throw new NotSupportedException();

		public MacroDeck.Sdk.Events.IEventPublisher Events => throw new NotSupportedException();

		public MacroDeck.Sdk.Notifications.IUserNotifier Notifications => throw new NotSupportedException();
	}
}
