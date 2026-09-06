using MacroDeck.Sdk.Widgets;
using MacroDeck.Localization;

namespace MacroDeck.Sdk.Actions;

public sealed class ActionParameter
{
	public required string Name { get; init; }

	public required ActionParameterType Type { get; init; }

	public LocalizedText Label { get; init; }

	public LocalizedText Description { get; init; }

	public LocalizedText Placeholder { get; init; }

	/// <summary>
	/// Whether the action editor adds <c>https://</c> when a URL is left without a protocol.
	/// </summary>
	public bool AutoPrefixHttps { get; init; }

	public object? DefaultValue { get; init; }

	public bool Required { get; init; }

	public bool Multiline { get; init; }

	/// <summary>
	/// Whether a <c>Color</c> parameter's picker offers a leading reset control. Off by default, so an
	/// existing colour picker does not suddenly grow a button for a default the action has no way to
	/// honor; an action opts in and reads the reset with <c>WidgetActionParameters.IsReset</c> or
	/// <see cref="MacroDeck.Sdk.Widgets.WidgetAppearanceValues.IsReset" />.
	/// </summary>
	public bool SupportsReset { get; init; }

	/// <summary>The value is authored literally; no variable binding is offered.</summary>
	public bool LiteralOnly { get; init; }

	public string? ValidationRegex { get; init; }

	public int? MaxLength { get; init; }

	public double? Min { get; init; }

	public double? Max { get; init; }

	public double? Step { get; init; }

	public bool ShowSlider { get; init; }

	public IReadOnlyList<ActionParameterOption>? Options { get; init; }

	public bool DynamicOptions { get; init; }

	public string? OptionsSourceId { get; init; }

	/// <summary>
	/// Whether a <c>WidgetTarget</c> parameter offers <see cref="WidgetTargets.Self" />. Defaults to
	/// <c>true</c>, which is what a widget's own flow wants; a configuration surface has no owning widget
	/// to mean, so it turns this off and the picker demands a concrete widget.
	/// </summary>
	public bool AllowSelf { get; init; } = true;

	/// <summary>
	/// Restricts a <c>WidgetTarget</c> parameter to the named widget types, matched against
	/// <see cref="MacroDeck.Sdk.Widgets.WidgetTargetInfo.Type" />. Empty means every widget is offered.
	/// </summary>
	public IReadOnlyList<string> WidgetTypes { get; init; } = [];

	public IReadOnlyList<string>? FileExtensions { get; init; }

	public string? Language { get; init; }

	public IReadOnlyList<ActionParameter>? Children { get; init; }

	public ActionParameter? ItemTemplate { get; init; }

	/// <summary>
	/// Shows this parameter only while a sibling holds one of the listed values. Null means always
	/// visible. Set it with <see cref="OnlyWhen" /> rather than through an object initializer, so the
	/// static factories stay usable.
	/// </summary>
	public ParameterVisibility? VisibleWhen
	{
		get => _visibleWhen;
		init => _visibleWhen = value;
	}

	private ParameterVisibility? _visibleWhen;

	/// <summary>
	/// Returns a copy of this parameter that the editor shows only while
	/// <paramref name="parameterName" /> holds one of <paramref name="values" />. Chained onto a
	/// factory call, e.g.
	/// <c>ActionParameter.Text("headerName", ...).OnlyWhen("authType", "header")</c>.
	///
	/// See <see cref="ParameterVisibility" /> for the semantics - in particular that a hidden
	/// parameter is still sent to the action and still keeps the value the user typed.
	/// </summary>
	public ActionParameter OnlyWhen(string parameterName, params string[] values)
	{
		// A shallow copy is safe: every member is a string, a boxed primitive, or an immutable list.
		var copy = (ActionParameter)MemberwiseClone();
		copy._visibleWhen = new ParameterVisibility(parameterName, values);
		return copy;
	}

	public static ActionParameter Text(string name,
		LocalizedText label = default,
		LocalizedText description = default,
		LocalizedText placeholder = default,
		string? defaultValue = null,
		bool required = false,
		string? validationRegex = null,
		int? maxLength = null)
		=> new()
		{
			Name = name,
			Type = ActionParameterType.String,
			Label = label,
			Description = description,
			Placeholder = placeholder,
			DefaultValue = defaultValue,
			Required = required,
			ValidationRegex = validationRegex,
			MaxLength = maxLength
		};

	public static ActionParameter MultilineText(string name,
		LocalizedText label = default,
		LocalizedText description = default,
		LocalizedText placeholder = default,
		string? defaultValue = null,
		bool required = false,
		int? maxLength = null)
		=> new()
		{
			Name = name,
			Type = ActionParameterType.String,
			Multiline = true,
			Label = label,
			Description = description,
			Placeholder = placeholder,
			DefaultValue = defaultValue,
			Required = required,
			MaxLength = maxLength
		};

	public static ActionParameter Number(string name,
		LocalizedText label = default,
		LocalizedText description = default,
		double? min = null,
		double? max = null,
		double? step = null,
		double? defaultValue = null,
		bool required = false)
		=> new()
		{
			Name = name,
			Type = ActionParameterType.Number,
			Label = label,
			Description = description,
			Min = min,
			Max = max,
			Step = step,
			DefaultValue = defaultValue,
			Required = required
		};

	public static ActionParameter Slider(string name,
		double min,
		double max,
		LocalizedText label = default,
		LocalizedText description = default,
		double? step = null,
		double? defaultValue = null)
		=> new()
		{
			Name = name,
			Type = ActionParameterType.Number,
			ShowSlider = true,
			Label = label,
			Description = description,
			Min = min,
			Max = max,
			Step = step,
			DefaultValue = defaultValue
		};

	public static ActionParameter Toggle(string name,
		LocalizedText label = default,
		LocalizedText description = default,
		bool defaultValue = false,
		bool literalOnly = false)
		=> new()
		{
			Name = name,
			Type = ActionParameterType.Boolean,
			Label = label,
			Description = description,
			DefaultValue = defaultValue,
			LiteralOnly = literalOnly
		};

	public static ActionParameter Password(string name,
		LocalizedText label = default,
		LocalizedText description = default,
		bool required = false)
		=> new()
		{
			Name = name,
			Type = ActionParameterType.Password,
			Label = label,
			Description = description,
			Required = required
		};

	public static ActionParameter Secret(string name,
		LocalizedText label = default,
		LocalizedText description = default,
		bool required = false)
		=> new()
		{
			Name = name,
			Type = ActionParameterType.Secret,
			Label = label,
			Description = description,
			Required = required
		};

	public static ActionParameter Choice(string name,
		IReadOnlyList<ActionParameterOption> options,
		LocalizedText label = default,
		LocalizedText description = default,
		string? defaultValue = null,
		bool required = false)
		=> new()
		{
			Name = name,
			Type = ActionParameterType.Choice,
			Options = options,
			Label = label,
			Description = description,
			DefaultValue = defaultValue,
			Required = required
		};

	/// <summary>
	/// A pick list whose options the host resolves at edit time.
	/// </summary>
	/// <param name="placeholder">
	/// What an empty value means, e.g. "First available" or "Any player". Give this to every optional
	/// dynamic choice: the options are only fetched when the list is first opened, so until then the
	/// editor has no label for the current value and an empty field would otherwise read as "you still
	/// have to choose something" for a parameter that is already doing the right thing.
	/// </param>
	public static ActionParameter DynamicChoice(string name,
		LocalizedText label = default,
		LocalizedText description = default,
		string? optionsSourceId = null,
		LocalizedText placeholder = default,
		bool required = false)
		=> new()
		{
			Name = name,
			Type = ActionParameterType.DynamicChoice,
			DynamicOptions = optionsSourceId is null,
			OptionsSourceId = optionsSourceId,
			Label = label,
			Description = description,
			Placeholder = placeholder,
			Required = required
		};

	public static ActionParameter Autocomplete(string name,
		LocalizedText label = default,
		LocalizedText description = default,
		IReadOnlyList<ActionParameterOption>? options = null,
		string? optionsSourceId = null,
		LocalizedText placeholder = default,
		bool required = false)
		=> new()
		{
			Name = name,
			Type = ActionParameterType.Autocomplete,
			Options = options,
			DynamicOptions = options is null && optionsSourceId is null,
			OptionsSourceId = optionsSourceId,
			Label = label,
			Description = description,
			Placeholder = placeholder,
			Required = required
		};

	public static ActionParameter MultiSelect(string name,
		IReadOnlyList<ActionParameterOption>? options = null,
		LocalizedText label = default,
		LocalizedText description = default,
		string? optionsSourceId = null,
		bool required = false)
		=> new()
		{
			Name = name,
			Type = ActionParameterType.MultiSelect,
			Options = options,
			DynamicOptions = options is null && optionsSourceId is null,
			OptionsSourceId = optionsSourceId,
			Label = label,
			Description = description,
			Required = required
		};

	public static ActionParameter Color(string name,
		LocalizedText label = default,
		LocalizedText description = default,
		string? defaultValue = null,
		bool supportsReset = false)
		=> new()
		{
			Name = name,
			Type = ActionParameterType.Color,
			Label = label,
			Description = description,
			DefaultValue = defaultValue,
			SupportsReset = supportsReset
		};

	public static ActionParameter File(string name,
		LocalizedText label = default,
		LocalizedText description = default,
		IReadOnlyList<string>? fileExtensions = null,
		bool required = false)
		=> new()
		{
			Name = name,
			Type = ActionParameterType.File,
			FileExtensions = fileExtensions,
			Label = label,
			Description = description,
			Required = required
		};

	public static ActionParameter Folder(string name,
		LocalizedText label = default,
		LocalizedText description = default,
		bool required = false)
		=> new()
		{
			Name = name,
			Type = ActionParameterType.Folder,
			Label = label,
			Description = description,
			Required = required
		};

	public static ActionParameter Hotkey(string name,
		LocalizedText label = default,
		LocalizedText description = default,
		bool required = false)
		=> new()
		{
			Name = name,
			Type = ActionParameterType.Hotkey,
			Label = label,
			Description = description,
			Required = required
		};

	public static ActionParameter Duration(string name,
		LocalizedText label = default,
		LocalizedText description = default,
		double? min = null,
		double? max = null,
		double? defaultMilliseconds = null,
		bool required = false)
		=> new()
		{
			Name = name,
			Type = ActionParameterType.Duration,
			Label = label,
			Description = description,
			Min = min,
			Max = max,
			DefaultValue = defaultMilliseconds,
			Required = required
		};

	public static ActionParameter DateTime(string name,
		LocalizedText label = default,
		LocalizedText description = default,
		bool required = false)
		=> new()
		{
			Name = name,
			Type = ActionParameterType.DateTime,
			Label = label,
			Description = description,
			Required = required
		};

	public static ActionParameter Json(string name,
		LocalizedText label = default,
		LocalizedText description = default,
		string? defaultValue = null,
		bool required = false)
		=> new()
		{
			Name = name,
			Type = ActionParameterType.Json,
			Label = label,
			Description = description,
			DefaultValue = defaultValue,
			Required = required
		};

	public static ActionParameter Code(string name,
		string language,
		LocalizedText label = default,
		LocalizedText description = default,
		string? defaultValue = null,
		bool required = false)
		=> new()
		{
			Name = name,
			Type = ActionParameterType.Code,
			Language = language,
			Label = label,
			Description = description,
			DefaultValue = defaultValue,
			Required = required
		};

	public static ActionParameter KeyValue(string name,
		LocalizedText label = default,
		LocalizedText description = default,
		bool required = false)
		=> new()
		{
			Name = name,
			Type = ActionParameterType.KeyValue,
			Label = label,
			Description = description,
			Required = required
		};

#pragma warning disable CA1720 // Benennung folgt dem Parametertyp
	public static ActionParameter Object(string name,
		IReadOnlyList<ActionParameter> children,
		LocalizedText label = default,
		LocalizedText description = default)
		=> new()
		{
			Name = name,
			Type = ActionParameterType.Object,
			Children = children,
			Label = label,
			Description = description
		};
#pragma warning restore CA1720

	public static ActionParameter Array(string name,
		ActionParameter itemTemplate,
		LocalizedText label = default,
		LocalizedText description = default)
		=> new()
		{
			Name = name,
			Type = ActionParameterType.Array,
			ItemTemplate = itemTemplate,
			Label = label,
			Description = description
		};

	public static ActionParameter IpAddress(string name,
		LocalizedText label = default,
		LocalizedText description = default,
		bool required = false)
		=> new()
		{
			Name = name,
			Type = ActionParameterType.IpAddress,
			Label = label,
			Description = description,
			Required = required
		};

	public static ActionParameter Url(string name,
		LocalizedText label = default,
		LocalizedText description = default,
		LocalizedText placeholder = default,
		bool required = false,
		bool autoPrefixHttps = false)
		=> new()
		{
			Name = name,
			Type = ActionParameterType.Url,
			Label = label,
			Description = description,
			Placeholder = placeholder,
			Required = required,
			AutoPrefixHttps = autoPrefixHttps
		};

	public static ActionParameter Icon(string name,
		LocalizedText label = default,
		LocalizedText description = default,
		bool required = false)
		=> new()
		{
			Name = name,
			Type = ActionParameterType.Icon,
			Label = label,
			Description = description,
			Required = required
		};

	public static ActionParameter Image(string name,
		LocalizedText label = default,
		LocalizedText description = default,
		bool required = false)
		=> new()
		{
			Name = name,
			Type = ActionParameterType.Image,
			Label = label,
			Description = description,
			Required = required
		};

	/// <summary>
	/// A multi-step keyboard sequence (key combos, text, delays, key down/up) edited by the dedicated
	/// sequence editor. The value is a JSON object matching the keyboard sequence model.
	/// </summary>
	public static ActionParameter KeyboardSequence(string name,
		LocalizedText label = default,
		LocalizedText description = default,
		bool required = false)
		=> new()
		{
			Name = name,
			Type = ActionParameterType.KeyboardSequence,
			Label = label,
			Description = description,
			Required = required
		};

	/// <summary>
	/// A single key combination (modifiers + key) edited with the keyboard combo editor (record a key
	/// or pick a supported key from a dropdown, plus modifier toggles).
	/// </summary>
	public static ActionParameter KeyboardCombo(string name,
		LocalizedText label = default,
		LocalizedText description = default,
		bool required = false)
		=> new()
		{
			Name = name,
			Type = ActionParameterType.KeyboardCombo,
			Label = label,
			Description = description,
			Required = required
		};

	/// <summary>
	/// Picks the widget an action acts on. The stored value is either a widget id or
	/// <see cref="WidgetTargets.Self" />, which resolves to
	/// <see cref="ActionExecutionContext.OwnerWidgetId" /> at run time.
	///
	/// The editor renders <c>$self</c> as a "This widget" chip while the flow belongs to a widget and
	/// as an empty picker everywhere else, because a script and an automation have no widget to mean
	/// - which is why <paramref name="required" /> defaults to true.
	/// </summary>
	public static ActionParameter WidgetTarget(string name,
		LocalizedText label = default,
		LocalizedText description = default,
		bool required = true)
		=> WidgetTarget(name,
			new WidgetTargetOptions
			{
				Label = label,
				Description = description,
				Required = required
			});

	/// <summary>
	/// Picks a widget, with the selection rules spelled out. An overload rather than more optional
	/// parameters on the four-argument form, which a plugin compiled against an older SDK still calls.
	///
	/// With <see cref="WidgetTargetOptions.AllowSelf" /> off there is no owning widget to fall back on,
	/// so the default value is empty and the user has to choose one.
	/// </summary>
	public static ActionParameter WidgetTarget(string name, WidgetTargetOptions options)
	{
		ArgumentNullException.ThrowIfNull(options);

		return new ActionParameter
		{
			Name = name,
			Type = ActionParameterType.WidgetTarget,
			OptionsSourceId = WidgetOptionsSources.Widgets,
			DefaultValue = options.AllowSelf ? WidgetTargets.Self : null,
			Label = options.Label,
			Description = options.Description,
			Required = options.Required,
			AllowSelf = options.AllowSelf,
			WidgetTypes = options.WidgetTypes
		};
	}
}

/// <summary>The selection rules of a widget-target parameter - see
/// <see cref="ActionParameter.WidgetTarget(string, WidgetTargetOptions)" />.</summary>
public sealed record WidgetTargetOptions
{
	public LocalizedText Label { get; init; }

	public LocalizedText Description { get; init; }

	/// <summary>Defaults to <c>true</c>, matching the four-argument factory.</summary>
	public bool Required { get; init; } = true;

	/// <summary>Whether <see cref="WidgetTargets.Self" /> is offered. Defaults to <c>true</c>.</summary>
	public bool AllowSelf { get; init; } = true;

	/// <summary>Widget type names the choice is limited to. Empty means every widget.</summary>
	public IReadOnlyList<string> WidgetTypes { get; init; } = [];
}
