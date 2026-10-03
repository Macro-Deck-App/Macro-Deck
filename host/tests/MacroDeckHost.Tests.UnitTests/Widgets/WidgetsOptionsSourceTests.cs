using MacroDeckHost.Application.Variables;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Domain.Widgets;
using MacroDeckHost.Infrastructure.OptionsSources;
using MacroDeck.Sdk.Widgets;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Tests.UnitTests.Widgets;

public class WidgetsOptionsSourceTests
{
	[Test]
	public async Task ExposesAppearancePropertiesAsAdditiveTargetMetadata()
	{
		var source = Source(Widget("Clock"));

		var result = await source.GetOptionsAsync(null, CancellationToken.None);
		var option = result.Options.Single();

		Assert.Multiple(() =>
		{
			Assert.That(option.Metadata, Is.Not.Null);
			Assert.That(option.Metadata!["type"], Is.EqualTo("clock"));
			Assert.That(option.Metadata["appearanceProperties"], Is.EqualTo("Border,BorderColor"));
		});
	}

	[Test]
	public async Task A_plain_label_is_listed_unchanged()
	{
		var result = await Source(Widget("Clock")).GetOptionsAsync(null, CancellationToken.None);

		Assert.That(result.Options.Single().Label.Literal, Is.EqualTo("Clock (Profile / Folder)"));
	}

	[Test]
	public async Task A_templated_label_is_listed_as_the_text_it_renders()
	{
		var volume = Variable("ytmdesktop_volume", "42");
		var source = Source(Widget("{{ vars.ytmdesktop_volume }}"), volume);

		var result = await source.GetOptionsAsync(null, CancellationToken.None);

		Assert.That(result.Options.Single().Label.Literal, Is.EqualTo("42 (Profile / Folder)"));
	}

	[Test]
	public async Task A_label_rendering_over_several_lines_is_listed_on_one_line()
	{
		var source = Source(Widget("{% if true %}A{% endif %}\n\n   B"));

		var result = await source.GetOptionsAsync(null, CancellationToken.None);

		Assert.That(result.Options.Single().Label.Literal, Is.EqualTo("A B (Profile / Folder)"));
	}

	[Test]
	public async Task A_label_rendering_to_nothing_falls_back_to_the_widget_type_name()
	{
		var source = Source(Widget("{{ vars.missing }}", type: WidgetTypeIds.Clock));

		var result = await source.GetOptionsAsync(null, CancellationToken.None);

		Assert.That(result.Options.Single().Label.Literal, Is.EqualTo("Clock (Profile / Folder)"));
	}

	[Test]
	public async Task A_label_that_cannot_be_rendered_falls_back_to_the_widget_type_name()
	{
		var source = Source(Widget("{{ 1 +", type: WidgetTypeIds.Clock));

		var result = await source.GetOptionsAsync(null, CancellationToken.None);

		Assert.That(result.Options.Single().Label.Literal, Is.EqualTo("Clock (Profile / Folder)"));
	}

	[Test]
	public async Task The_filter_matches_the_rendered_text_not_the_template_source()
	{
		var volume = Variable("v", "loud");
		var source = Source(Widget("{{ vars.v }}"), volume);

		var byRendered = await source.GetOptionsAsync("loud", CancellationToken.None);
		var bySource = await source.GetOptionsAsync("vars", CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(byRendered.Options, Has.Count.EqualTo(1));
			Assert.That(bySource.Options, Is.Empty);
		});
	}

	private static WidgetsOptionsSource Source(WidgetTargetInfo widget, params VariableEntity[] variables)
	{
		var registry = new VariableRegistry();
		foreach (var variable in variables)
		{
			registry.Upsert(variable);
		}

		var services = new ServiceCollection();
		services.AddScoped<IVariableTemplateRenderer>(_ => new VariableTemplateRenderer(registry));
		var provider = services.BuildServiceProvider();
		return new WidgetsOptionsSource(new FakeWidgetApi(widget), provider.GetRequiredService<IServiceScopeFactory>());
	}

	private static WidgetTargetInfo Widget(string label, string type = "clock") => new()
	{
		Id = "clock-1",
		Label = label,
		Location = "Profile / Folder",
		Type = type,
		AppearanceProperties = [WidgetAppearanceProperty.Border, WidgetAppearanceProperty.BorderColor]
	};

	private static VariableEntity Variable(string name, string value) => new()
	{
		Id = Guid.NewGuid(),
		Name = name,
		Scope = VariableScope.Global,
		Type = VariableType.Text,
		Classification = VariableClassification.User,
		Value = value
	};

	private sealed class FakeWidgetApi(WidgetTargetInfo widget) : IWidgetApi
	{
		public IReadOnlyList<WidgetTargetInfo> GetWidgets() => [widget];

		public bool Exists(string widgetId) => widgetId == widget.Id;

		public Task<bool> ApplyAsync(WidgetAppearanceRequest request, CancellationToken cancellationToken = default) =>
			Task.FromResult(false);

		public Task<WidgetStateWriteResult> SetStateAsync(
			string widgetId,
			string stateId,
			CancellationToken cancellationToken = default)
			=> Task.FromResult(WidgetStateWriteResult.Failed(WidgetStateWriteError.NotFound));

		public Task<WidgetStateWriteResult> AdvanceStateAsync(string widgetId,
			CancellationToken cancellationToken = default)
			=> Task.FromResult(WidgetStateWriteResult.Failed(WidgetStateWriteError.NotFound));
	}
}
