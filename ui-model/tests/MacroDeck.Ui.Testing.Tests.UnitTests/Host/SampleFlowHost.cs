using MacroDeck.Ui.Config;
using MacroDeck.Ui.Dsl;

namespace MacroDeck.Ui.Testing.Tests.UnitTests.Host;

/// <summary>An item in the SampleFlow fixture's <c>headers</c> array. The key comes from
/// <see cref="Id" />, never from the item's position in the list.</summary>
internal sealed record HeaderItem(string Id);

/// <summary>
/// The shared SampleFlow fixture, authored once for the host fixtures in this project: a <c>setup</c> flow with
/// a <c>credentials</c> step (an apiKey string, a mode choice, an endpoint object with host and port, a headers
/// array repeating name and value pairs, and an advanced section gated behind a condition) and a <c>verify</c>
/// step. Both steps are present, which is what makes it a fixture for the query contract rather than for the
/// step-transition one.
/// </summary>
internal static class SampleFlowHost
{
	/// <summary>Renders the fixture with two header items, keyed <c>h1</c> and <c>h2</c>.</summary>
	internal static UiTestHost Render() => UiTestHost.Render(Build([new HeaderItem("h1"), new HeaderItem("h2")]));

	private static UiFlow Build(IReadOnlyList<HeaderItem> headers)
		=> new()
		{
			Key = "setup",
			Children =
			[
				new UiStep
				{
					Key = "credentials",
					Children =
					[
						new UiStringInput { Key = "apiKey", Label = "API key" },
						new UiChoiceInput { Key = "mode" },
						new UiObjectInput
						{
							Key = "endpoint",
							Children = [new UiStringInput { Key = "host" }, new UiStringInput { Key = "port" }],
						},
						new UiArrayInput
						{
							Key = "headers",
							Children =
							[
								new UiRepeat<HeaderItem>
								{
									Key = "headerItems",
									Items = UiValue.Of(headers),
									KeySelector = item => item.Id,
									Template = (item, _) => new UiObjectInput
									{
										Key = item.Id,
										Children =
										[
											new UiStringInput { Key = "name" },
											new UiStringInput { Key = "value" },
										],
									},
								},
							],
						},
						new UiWhen
						{
							Key = "adv",
							Condition = () => false,
							Content = () => new UiAdvancedSection
							{
								Key = "advanced",
								Children = [new UiDurationInput { Key = "timeout" }],
							},
						},
					],
				},
				new UiStep { Key = "verify" },
			],
		};
}
