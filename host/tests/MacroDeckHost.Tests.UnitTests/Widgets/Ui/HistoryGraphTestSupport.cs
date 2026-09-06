using System.Text.Json;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Widgets.HistoryGraph;

namespace MacroDeckHost.Tests.UnitTests.Widgets.Ui;

internal static class HistoryGraphTestSupport
{
	public const string Metric = "system_cpu_usage_percent";

	public const string Caption = "system_cpu_name";

	/// <summary>The unit is a property of the variable, not of the widget - see ADR 0081 - so a test that
	/// wants the card to show one declares it here rather than in the widget data.</summary>
	public static VariableRegistry Registry(string value = "73",
		int? decimalPlaces = 0,
		string? caption = "Apple M3 Max",
		string? unit = null)
	{
		var registry = new VariableRegistry();

		registry.Upsert(new VariableEntity
		{
			Id = Guid.NewGuid(),
			Name = Metric,
			Scope = VariableScope.Global,
			Type = VariableType.Numeric,
			Classification = VariableClassification.User,
			Value = value,
			DecimalPlaces = decimalPlaces,
			Unit = unit,
			UpdatedAt = DateTime.UtcNow,
		});

		if (caption is not null)
		{
			registry.Upsert(new VariableEntity
			{
				Id = Guid.NewGuid(),
				Name = Caption,
				Scope = VariableScope.Global,
				Type = VariableType.Text,
				Classification = VariableClassification.User,
				Value = caption,
				UpdatedAt = DateTime.UtcNow,
			});
		}

		return registry;
	}

	public static HistoryGraphWidgetData Config(object data)
		=> HistoryGraphWidgetData.Parse(JsonSerializer.SerializeToElement(data));

	public static HistoryGraphViewStateResolver Resolver(object data, VariableRegistry? variables = null)
		=> new(Config(data), variables ?? Registry());
}
