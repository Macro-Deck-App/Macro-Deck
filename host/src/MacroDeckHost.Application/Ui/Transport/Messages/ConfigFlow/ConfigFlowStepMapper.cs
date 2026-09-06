using System.Text.Json;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Widgets;
using MacroDeckHost.Application.Ui.Transport.Messages.Actions;
using SdkConfigFlowStep = MacroDeck.Sdk.ConfigFlow.ConfigFlowStep;
using WireParameterType = MacroDeckHost.Application.Ui.Transport.Messages.Actions.ActionParameterType;

namespace MacroDeckHost.Application.Ui.Transport.Messages.ConfigFlow;

public static class ConfigFlowStepMapper
{
	public static ConfigFlowStepDto Map(SdkConfigFlowStep step)
		=> new()
		{
			StepId = step.StepId,
			Title = step.Title,
			Description = step.Description,
			Values = step.Values
				.Select(value => new ConfigFlowCopyValueDto { Label = value.Label, Value = value.Value })
				.ToList(),
			Instructions = step.Instructions
				.Select(instruction => new ConfigFlowInstructionDto
				{
					Text = instruction.Text,
					Values = instruction.Values
						.Select(value => new ConfigFlowCopyValueDto { Label = value.Label, Value = value.Value })
						.ToList()
				})
				.ToList(),
			Links = step.Links.Select(link => new ConfigFlowLinkDto { Label = link.Label, Url = link.Url }).ToList(),
			Fields = step.Fields.Select(MapField).ToList(),
			AdvancedFields = step.AdvancedFields.Select(MapField).ToList()
		};

	/// <summary>
	/// A config flow belongs to an integration, not to a widget, so there is no "this widget" for a
	/// widget-target field to mean. Stripped here rather than in a renderer, so the sentinel cannot reach
	/// a step's values as a literal string on any client.
	/// </summary>
	private static ActionParameterDef MapField(ActionParameter field)
	{
		var def = ActionParameterDefMapper.Map(field);

		if (def.Type != WireParameterType.WidgetTarget)
		{
			return def;
		}

		def.AllowSelf = false;

		if (def.DefaultValue is { ValueKind: JsonValueKind.String } value && WidgetTargets.IsSelf(value.GetString()))
		{
			def.DefaultValue = null;
		}

		return def;
	}
}
