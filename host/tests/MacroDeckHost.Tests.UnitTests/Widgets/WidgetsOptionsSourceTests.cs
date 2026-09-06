using MacroDeckHost.Infrastructure.OptionsSources;
using MacroDeck.Sdk.Widgets;

namespace MacroDeckHost.Tests.UnitTests.Widgets;

public class WidgetsOptionsSourceTests
{
	[Test]
	public async Task ExposesAppearancePropertiesAsAdditiveTargetMetadata()
	{
		var source = new WidgetsOptionsSource(new FakeWidgetApi());

		var result = await source.GetOptionsAsync(null, CancellationToken.None);
		var option = result.Options.Single();

		Assert.Multiple(() =>
		{
			Assert.That(option.Metadata, Is.Not.Null);
			Assert.That(option.Metadata!["type"], Is.EqualTo("clock"));
			Assert.That(option.Metadata["appearanceProperties"], Is.EqualTo("Border,BorderColor"));
		});
	}

	private sealed class FakeWidgetApi : IWidgetApi
	{
		public IReadOnlyList<WidgetTargetInfo> GetWidgets() =>
		[
			new()
			{
				Id = "clock-1",
				Label = "Clock",
				Location = "Profile / Folder",
				Type = "clock",
				AppearanceProperties = [WidgetAppearanceProperty.Border, WidgetAppearanceProperty.BorderColor]
			}
		];

		public bool Exists(string widgetId) => widgetId == "clock-1";

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
