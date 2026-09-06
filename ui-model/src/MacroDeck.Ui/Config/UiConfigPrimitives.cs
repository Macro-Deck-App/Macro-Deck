namespace MacroDeck.Ui.Config;

/// <summary>
/// The node type strings the configuration profile ships: thirty-four inputs and twenty chrome elements.
///
/// <para>
/// <b>Derived, not invented.</b> The first twenty-seven input names are exactly the control names the existing
/// editor's parameter mapping produces for the twenty-seven action parameter types, one to one, so the future
/// renderer's mapping from a node type to a control is the identity function. That is why the spellings look
/// inconsistent -
/// <c>multiselect</c> and <c>keyvalue</c> run together while <c>dynamic-choice</c> and
/// <c>keyboard-sequence</c> are hyphenated. Regularising them would rename a control the editor already
/// switches on. The chrome names come from the existing config flow dialog: its step, its instruction list,
/// its copy blocks, its links, its advanced-configuration switch, its banner and its busy states.
/// </para>
///
/// <para>
/// <b>The rest are Macro Deck's own.</b> The seven inputs after the parameter counterparts name editors the
/// application already ships - the action list, the pickers for actions, variables, devices and
/// integrations, the icon-framing control from the widget appearance form, and the button state-mapping
/// editor - so a widget reaches one by naming it rather than by rebuilding it from primitives. The three
/// chrome types after the flow's own name the regions of a widget's configuration surface. Both groups are
/// app-specific by design and neither has a parameter counterpart.
/// </para>
///
/// <para>
/// <b>No IsKnown, deliberately.</b> A profile vocabulary is open. The moment an <c>IsKnown</c> exists,
/// something gates on it and an unrecognised type becomes fatal, where the model's answer is to render the
/// node's <c>Fallback</c> - the same reasoning ADR 0056 states for the surface kinds.
/// </para>
/// </summary>
public static class UiConfigPrimitives
{
	// Suppressed once for the whole vocabulary: these are wire strings whose C# spelling has to match, and
	// renaming String, Boolean, Object or Array to dodge the rule would leave the constant and the value it
	// carries reading differently.
#pragma warning disable CA1720

	/// <summary>A single-line text field. Multi-line text is this type with <c>multiline</c> set - see
	/// <see cref="UiStringInput" />.</summary>
	public const string String = "string";

	/// <summary>A number field. A slider is this type with <c>showSlider</c> set - see
	/// <see cref="UiNumberInput" />.</summary>
	public const string Number = "number";

	/// <summary>A toggle.</summary>
	public const string Boolean = "boolean";

	/// <summary>A single selection from a fixed list of options.</summary>
	public const string Choice = "choice";

	/// <summary>A password field, masked and stored as a secret.</summary>
	public const string Password = "password";

	/// <summary>A secret field, masked and stored encrypted.</summary>
	public const string Secret = "secret";

	/// <summary>A single selection whose options are resolved on demand.</summary>
	public const string DynamicChoice = "dynamic-choice";

	/// <summary>A text field with suggestions, which may be resolved on demand.</summary>
	public const string Autocomplete = "autocomplete";

	/// <summary>A multiple selection.</summary>
	public const string MultiSelect = "multiselect";

	/// <summary>A colour picker.</summary>
	public const string Color = "color";

	/// <summary>A file path with a browse affordance.</summary>
	public const string File = "file";

	/// <summary>A folder path with a browse affordance.</summary>
	public const string Folder = "folder";

	/// <summary>A recorded global hotkey.</summary>
	public const string Hotkey = "hotkey";

	/// <summary>A duration, in milliseconds.</summary>
	public const string Duration = "duration";

	/// <summary>A date and time.</summary>
	public const string DateTime = "datetime";

	/// <summary>A JSON document, edited as text and validated as JSON.</summary>
	public const string Json = "json";

	/// <summary>Source code in a named language.</summary>
	public const string Code = "code";

	/// <summary>A map of string keys to string values.</summary>
	public const string KeyValue = "keyvalue";

	/// <summary>A fixed set of named fields, and an id scope for them.</summary>
	public const string Object = "object";

	/// <summary>A repeated set of items, and an id scope for them.</summary>
	public const string Array = "array";

	/// <summary>An IPv4 or IPv6 address.</summary>
	public const string IpAddress = "ipaddress";

	/// <summary>A URL.</summary>
	public const string Url = "url";

	/// <summary>An icon chosen from the icon set.</summary>
	public const string Icon = "icon";

	/// <summary>An image path with a browse affordance.</summary>
	public const string Image = "image";

	/// <summary>A multi-step keyboard sequence.</summary>
	public const string KeyboardSequence = "keyboard-sequence";

	/// <summary>A single key combination.</summary>
	public const string KeyboardCombo = "keyboard-combo";

	/// <summary>A widget the action acts on.</summary>
	public const string WidgetTarget = "widget-target";

	/// <summary>The list of action flows a widget runs, with its own triggers, ordering and nesting.</summary>
	public const string ActionsListEditor = "actions-list-editor";

	/// <summary>One action chosen from the catalog of everything installed.</summary>
	public const string ActionPicker = "action-picker";

	/// <summary>One variable chosen from the catalog, optionally narrowed to a type.</summary>
	public const string VariablePicker = "variable-picker";

	/// <summary>One connected device.</summary>
	public const string DevicePicker = "device-picker";

	/// <summary>One integration, or one of its configuration entries.</summary>
	public const string IntegrationPicker = "integration-picker";

	/// <summary>A widget icon's framing - fit, zoom, position and opacity - edited on a live preview of
	/// the button face.</summary>
	public const string IconDisplay = "icon-display";

	/// <summary>A button's state-mapping table: the rules that pick a state from a condition, and the
	/// fallback state used when none match.</summary>
	public const string StateMappingEditor = "state-mapping-editor";

	/// <summary>A multi-step configuration flow: the root of a configuration surface.</summary>
	public const string Flow = "flow";

	/// <summary>One screen of a flow. Only the active step is in the tree.</summary>
	public const string Step = "step";

	/// <summary>A run of children laid out in one direction.</summary>
	public const string Stack = "stack";

	/// <summary>A segmented set of panels, each a <see cref="Tab" />; only the active one's children
	/// render. Which panel is active is never in the tree - it is exactly the per-client state an
	/// advanced section's current expansion is not, for the same reason.</summary>
	public const string Tabs = "tabs";

	/// <summary>One panel of a <see cref="Tabs" />, named by its label.</summary>
	public const string Tab = "tab";

	/// <summary>A section caption.</summary>
	public const string Heading = "heading";

	/// <summary>A paragraph of explanatory text.</summary>
	public const string Prose = "prose";

	/// <summary>An ordered list of <see cref="Instruction" /> children.</summary>
	public const string Instructions = "instructions";

	/// <summary>One numbered setup instruction. Its position is the number the user sees.</summary>
	public const string Instruction = "instruction";

	/// <summary>A labeled value the user copies out to an external service.</summary>
	public const string CopyValue = "copy-value";

	/// <summary>A labeled link opened through the shell.</summary>
	public const string Link = "link";

	/// <summary>Fields hidden behind an "advanced configuration" affordance.</summary>
	public const string AdvancedSection = "advanced-section";

	/// <summary>A visual separator.</summary>
	public const string Divider = "divider";

	/// <summary>A message about the flow as a whole rather than about one field.</summary>
	public const string Banner = "banner";

	/// <summary>A message about one field.</summary>
	public const string ValidationMessage = "validation-message";

	/// <summary>Work in progress the user has to wait for.</summary>
	public const string Busy = "busy";

	/// <summary>A labeled control that raises <c>activate</c> and contributes no value of its own -
	/// applying a preset, running a check, revealing something. Distinct from <see cref="Link" />, which
	/// goes somewhere rather than doing something.</summary>
	public const string Button = "button";

	/// <summary>The root of a widget's configuration surface, carrying its two regions.</summary>
	public const string WidgetConfiguration = "widget-configuration";

	/// <summary>A widget's ordinary property configuration. Macro Deck draws it beside the widget preview,
	/// which it owns; the widget supplies only the fields.</summary>
	public const string WidgetProperties = "widget-properties";

	/// <summary>A widget's specialized configuration, given the room a property list would not fit in.
	/// Optional: a widget that needs no such area serves a root without one.</summary>
	public const string WidgetEditor = "widget-editor";

#pragma warning restore CA1720

	/// <summary>The types this package ships names for, inputs first and then chrome, each in the order they
	/// are declared above. Not exhaustive - see the type's remarks.</summary>
	/// <remarks>
	/// The first twenty-seven inputs are the action parameter types' counterparts and stay in that order and
	/// that position - see <c>ParameterTypeParityTests</c>. The inputs after them are Macro Deck's own
	/// high-level controls, which have no parameter counterpart by construction: they exist so a widget can
	/// reach an editor the application already ships rather than rebuilding it from primitives.
	/// </remarks>
	public static readonly IReadOnlyList<string> WellKnown =
	[
		String, Number, Boolean, Choice, Password, Secret, DynamicChoice, Autocomplete, MultiSelect, Color,
		File, Folder, Hotkey, Duration, DateTime, Json, Code, KeyValue, Object, Array, IpAddress, Url, Icon,
		Image, KeyboardSequence, KeyboardCombo, WidgetTarget, ActionsListEditor, ActionPicker, VariablePicker,
		DevicePicker, IntegrationPicker, IconDisplay, StateMappingEditor, Flow, Step, Stack, Tabs,
		Tab, Heading, Prose, Instructions, Instruction, CopyValue, Link, AdvancedSection, Divider, Banner,
		ValidationMessage, Busy, Button, WidgetConfiguration, WidgetProperties, WidgetEditor,
	];
}
