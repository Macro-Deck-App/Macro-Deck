using System.Text.RegularExpressions;
using MacroDeckHost.Application.Actions.Options;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Application.Widgets;
using MacroDeckHost.Domain.Enums;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Widgets;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Infrastructure.OptionsSources;

public sealed partial class WidgetsOptionsSource : IHostOptionsSource
{
	private readonly IWidgetApi _widgets;
	private readonly IServiceScopeFactory _scopeFactory;

	public WidgetsOptionsSource(IWidgetApi widgets, IServiceScopeFactory scopeFactory)
	{
		_widgets = widgets;
		_scopeFactory = scopeFactory;
	}

	public string Id => WidgetOptionsSources.Widgets;

	public async Task<DynamicOptionsResult> GetOptionsAsync(string? filter, CancellationToken cancellationToken)
	{
		using var scope = _scopeFactory.CreateScope();
		var renderer = scope.ServiceProvider.GetRequiredService<IVariableTemplateRenderer>();

		var options = new List<ActionParameterOption>();
		foreach (var widget in _widgets.GetWidgets())
		{
			var label = $"{await DisplayLabelAsync(renderer, widget)} ({widget.Location})";
			if (filter is not null && !label.Contains(filter, StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			options.Add(new ActionParameterOption
			{
				Value = widget.Id,
				Label = label,
				Metadata = new Dictionary<string, string>
				{
					["type"] = widget.Type,
					["stateIds"] = string.Join(',', widget.States.Select(state => state.Id)),
					["appearanceProperties"] = string.Join(',', widget.AppearanceProperties)
				}
			});
		}

		return new DynamicOptionsResult
		{
			Options = options.OrderBy(option => option.Label.Literal, StringComparer.OrdinalIgnoreCase).ToList(),
			AllowsCustomValue = false,
			CacheSeconds = 5
		};
	}

	private static async Task<string> DisplayLabelAsync(IVariableTemplateRenderer renderer, WidgetTargetInfo widget)
	{
		if (!VariableTemplateRenderer.ContainsLiquid(widget.Label))
		{
			return widget.Label;
		}

		try
		{
			var rendered = await renderer.RenderAsync(widget.Label, VariableScope.Widget, widget.Id);
			var collapsed = Whitespace().Replace(rendered, " ").Trim();
			if (collapsed.Length > 0)
			{
				return collapsed;
			}
		}
		catch (Exception)
		{
		}

		return WidgetAppearanceService.DescribeType(widget.Type);
	}

	[GeneratedRegex(@"\s+")]
	private static partial Regex Whitespace();
}
