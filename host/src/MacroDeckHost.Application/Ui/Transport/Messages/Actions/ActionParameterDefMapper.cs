using System.Text.Json;
using SdkActionParameter = MacroDeck.Sdk.Actions.ActionParameter;
using SdkActionParameterType = MacroDeck.Sdk.Actions.ActionParameterType;

namespace MacroDeckHost.Application.Ui.Transport.Messages.Actions;

public static class ActionParameterDefMapper
{
	public static ActionParameterDef Map(SdkActionParameter param)
	{
		return new ActionParameterDef
		{
			Name = param.Name,
			Type = MapType(param.Type),
			Description = param.Description,
			Label = param.Label,
			Placeholder = param.Placeholder,
			AutoPrefixHttps = param.AutoPrefixHttps,
			DefaultValue = param.DefaultValue is null
				? null
				: JsonSerializer.SerializeToElement(param.DefaultValue),
			Required = param.Required,
			Multiline = param.Multiline,
			SupportsReset = param.SupportsReset,
			LiteralOnly = param.LiteralOnly,
			ValidationRegex = param.ValidationRegex,
			MaxLength = param.MaxLength,
			Min = param.Min,
			Max = param.Max,
			Step = param.Step,
			ShowSlider = param.ShowSlider,
			Options = param.Options?
				.Select(o => new ActionParameterOptionDto
				{
					Value = o.Value,
					Label = o.Label,
					Metadata = o.Metadata?.ToDictionary(pair => pair.Key, pair => pair.Value)
				})
				.ToList(),
			DynamicOptions = param.DynamicOptions,
			OptionsSourceId = param.OptionsSourceId,
			AllowSelf = param.AllowSelf,
			WidgetTypes = param.WidgetTypes,
			FileExtensions = param.FileExtensions?.ToList(),
			Language = param.Language,
			Children = param.Children?.Select(Map).ToList(),
			ItemTemplate = param.ItemTemplate is null ? null : Map(param.ItemTemplate),
			VisibleWhen = param.VisibleWhen is null
				? null
				: new ParameterVisibilityDto
				{
					ParameterName = param.VisibleWhen.ParameterName,
					Values = [.. param.VisibleWhen.Values]
				}
		};
	}

	public static ActionParameterType MapType(SdkActionParameterType type)
	{
		return type switch
		{
			SdkActionParameterType.String => ActionParameterType.String,
			SdkActionParameterType.Number => ActionParameterType.Number,
			SdkActionParameterType.Boolean => ActionParameterType.Boolean,
			SdkActionParameterType.Password => ActionParameterType.Password,
			SdkActionParameterType.Secret => ActionParameterType.Secret,
			SdkActionParameterType.Choice => ActionParameterType.Choice,
			SdkActionParameterType.DynamicChoice => ActionParameterType.DynamicChoice,
			SdkActionParameterType.Autocomplete => ActionParameterType.Autocomplete,
			SdkActionParameterType.MultiSelect => ActionParameterType.MultiSelect,
			SdkActionParameterType.Color => ActionParameterType.Color,
			SdkActionParameterType.File => ActionParameterType.File,
			SdkActionParameterType.Folder => ActionParameterType.Folder,
			SdkActionParameterType.Hotkey => ActionParameterType.Hotkey,
			SdkActionParameterType.Duration => ActionParameterType.Duration,
			SdkActionParameterType.DateTime => ActionParameterType.DateTime,
			SdkActionParameterType.Json => ActionParameterType.Json,
			SdkActionParameterType.Code => ActionParameterType.Code,
			SdkActionParameterType.KeyValue => ActionParameterType.KeyValue,
			SdkActionParameterType.Object => ActionParameterType.Object,
			SdkActionParameterType.Array => ActionParameterType.Array,
			SdkActionParameterType.IpAddress => ActionParameterType.IpAddress,
			SdkActionParameterType.Url => ActionParameterType.Url,
			SdkActionParameterType.Icon => ActionParameterType.Icon,
			SdkActionParameterType.Image => ActionParameterType.Image,
			SdkActionParameterType.KeyboardSequence => ActionParameterType.KeyboardSequence,
			SdkActionParameterType.KeyboardCombo => ActionParameterType.KeyboardCombo,
			SdkActionParameterType.WidgetTarget => ActionParameterType.WidgetTarget,
			_ => ActionParameterType.String
		};
	}
}
