using MacroDeckHost.Application.Actions.Options;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Widgets;

namespace MacroDeckHost.Infrastructure.OptionsSources;

public sealed class WidgetsOptionsSource : IHostOptionsSource
{
	private readonly IWidgetApi _widgets;

	public WidgetsOptionsSource(IWidgetApi widgets)
	{
		_widgets = widgets;
	}

	public string Id => WidgetOptionsSources.Widgets;

	public Task<DynamicOptionsResult> GetOptionsAsync(string? filter, CancellationToken cancellationToken)
	{
		var options = _widgets.GetWidgets()
			.Select(widget => new ActionParameterOption
			{
				Value = widget.Id,
				Label = $"{widget.Label} ({widget.Location})",
				Metadata = new Dictionary<string, string>
				{
					["type"] = widget.Type,
					["stateIds"] = string.Join(',', widget.States.Select(state => state.Id)),
					["appearanceProperties"] = string.Join(',', widget.AppearanceProperties)
				}
			})
			.Where(option => filter is null ||
				(option.Label.Literal ?? string.Empty).Contains(filter, StringComparison.OrdinalIgnoreCase))
			.OrderBy(option => option.Label.Literal, StringComparer.OrdinalIgnoreCase)
			.ToList();

		return Task.FromResult(new DynamicOptionsResult
		{
			Options = options,
			AllowsCustomValue = false,
			CacheSeconds = 5
		});
	}
}
