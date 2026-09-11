namespace MacroDeck.Ui.Config;

/// <summary>
/// The property keys the configuration profile's nodes carry. Each one is a property the existing action
/// parameter schema or config flow step already has, under the same name the existing editor reads it as, so
/// nothing here needs a translation layer.
///
/// <para>
/// <b>No IsKnown, deliberately</b>, for the reason <see cref="UiConfigPrimitives" /> gives: an unrecognised
/// property is ignored by the renderer that does not know it, which is what makes adding one a compatible
/// change.
/// </para>
///
/// <para>
/// Absent on purpose: anything that is per-client state. Focus, scroll position, hover and current expansion
/// belong to the client, and a shared session would broadcast one user's caret to everyone else. That is why
/// an advanced section carries <see cref="DefaultExpanded" /> - what the author decided - and never a current
/// <c>expanded</c>.
/// </para>
/// </summary>
public static class UiConfigProperties
{
#pragma warning disable CA1720

	/// <summary>The event names a node accepts.</summary>
	public const string Events = "events";

	/// <summary>An input's current value, a copy block's copied text.</summary>
	public const string Value = "value";

	/// <summary>A field's caption, a link's or a copy block's label, an advanced section's switch text.
	/// </summary>
	public const string Label = "label";

	/// <summary>Help text under a field, or a step's introductory sentence.</summary>
	public const string Description = "description";

	/// <summary>What an empty field means, rendered inside the control.</summary>
	public const string Placeholder = "placeholder";

	/// <summary>The value a reset returns to.</summary>
	public const string DefaultValue = "defaultValue";

	/// <summary>Whether an empty value blocks submission.</summary>
	public const string Required = "required";

	/// <summary>Whether the control rejects input.</summary>
	public const string Disabled = "disabled";

	/// <summary>Whether the value is authored literally, with no variable binding offered.</summary>
	public const string LiteralOnly = "literalOnly";

	/// <summary>Whether the control offers a reset affordance.</summary>
	public const string SupportsReset = "supportsReset";

	/// <summary>Whether the input is operated but contributes no key to the stored configuration - see
	/// <see cref="MacroDeck.Ui.Dsl.UiInput{T}.Transient" />.</summary>
	public const string Transient = "transient";

	/// <summary>A pattern the value has to match.</summary>
	public const string ValidationRegex = "validationRegex";

	/// <summary>The longest accepted value, in characters.</summary>
	public const string MaxLength = "maxLength";

	/// <summary>The sibling value that decides whether this field is shown.</summary>
	public const string VisibleWhen = "visibleWhen";

	/// <summary>Whether the control renders as failing validation.</summary>
	public const string Invalid = "invalid";

	/// <summary>Why the value is rejected.</summary>
	public const string ValidationMessage = "validationMessage";

	/// <summary>The lowest accepted number.</summary>
	public const string Min = "min";

	/// <summary>The highest accepted number.</summary>
	public const string Max = "max";

	/// <summary>The increment a number field steps by.</summary>
	public const string Step = "step";

	/// <summary>Whether a number field renders a slider alongside its box.</summary>
	public const string ShowSlider = "showSlider";

	/// <summary>Whether a text field accepts line breaks.</summary>
	public const string Multiline = "multiline";

	/// <summary>The options offered by a selection control.</summary>
	public const string Options = "options";

	/// <summary>The host-side source a dynamic option list is resolved from.</summary>
	public const string OptionsSourceId = "optionsSourceId";

	/// <summary>Whether the option list is resolved on demand rather than shipped with the node.</summary>
	public const string DynamicOptions = "dynamicOptions";

	/// <summary>Whether a value outside the option list is accepted.</summary>
	public const string AllowsCustomValue = "allowsCustomValue";

	/// <summary>Present, and only present, while an option list is being fetched.</summary>
	public const string Loading = "loading";

	/// <summary>Why fetching an option list failed.</summary>
	public const string Error = "error";

	/// <summary>How long an option list stays usable, as a hint to the client's own cache. The runtime owns no
	/// clock and honours nothing itself.</summary>
	public const string CacheSeconds = "cacheSeconds";

	/// <summary>How long the client should wait before refetching on a filter keystroke, as a hint. The
	/// runtime debounces nothing itself.</summary>
	public const string FilterDebounceMs = "filterDebounceMs";

	/// <summary>The extensions a browse dialog offers.</summary>
	public const string FileExtensions = "fileExtensions";

	/// <summary>The language a code editor highlights.</summary>
	public const string Language = "language";

	/// <summary>Whether a URL field adds <c>https://</c> to a value without a protocol.</summary>
	public const string AutoPrefixHttps = "autoPrefixHttps";

	/// <summary>A flow's or a step's title.</summary>
	public const string Title = "title";

	/// <summary>The id a step submits under, and the id of the step a flow currently shows.</summary>
	public const string StepId = "stepId";

	/// <summary>Where a flow is: showing a step, waiting on an external authorization, finished, failed.
	/// </summary>
	public const string State = "state";

	/// <summary>Whether a flow's submit affordance is enabled.</summary>
	public const string CanSubmit = "canSubmit";

	/// <summary>Which way a stack lays its children out.</summary>
	public const string Direction = "direction";

	/// <summary>A heading's, a paragraph's, an instruction's, a banner's, a message's or a busy indicator's
	/// text.</summary>
	public const string Text = "text";

	/// <summary>A link's target.</summary>
	public const string Url = "url";

	/// <summary>Whether an advanced section starts open. Never the current expansion, which is per-client.
	/// </summary>
	public const string DefaultExpanded = "defaultExpanded";

	/// <summary>Whether collapsing an advanced section clears the fields inside it.</summary>
	public const string ClearOnCollapse = "clearOnCollapse";

	/// <summary>How serious a banner is.</summary>
	public const string Severity = "severity";

	/// <summary>The id of the input a validation message belongs to.</summary>
	public const string For = "for";

	/// <summary>Which trigger tabs an action list offers.</summary>
	public const string Triggers = "triggers";

	/// <summary>Whether an action list may be run from the editor.</summary>
	public const string CanRun = "canRun";

	/// <summary>The integration a picker is restricted to.</summary>
	public const string IntegrationId = "integrationId";

	/// <summary>The variable types a variable picker offers.</summary>
	public const string VariableTypes = "variableTypes";

	/// <summary>Whether a variable picker offers only writable variables.</summary>
	public const string WritableOnly = "writableOnly";

	/// <summary>The capability an integration picker requires.</summary>
	public const string Capability = "capability";

	/// <summary>Whether an integration picker picks a configuration entry rather than an integration.</summary>
	public const string ConfigurationEntries = "configurationEntries";

	/// <summary>Whether a choice renders as a segmented control instead of a select.</summary>
	public const string Segmented = "segmented";

	/// <summary>Whether a choice renders its options as cards, each showing its label above its own
	/// description, instead of as a select.</summary>
	public const string Cards = "cards";

	/// <summary>The icon an icon-display input frames.</summary>
	public const string Icon = "icon";

	/// <summary>Width divided by height of the widget an icon-display input previews against.</summary>
	public const string AspectRatio = "aspectRatio";

	/// <summary>The background an icon-display input frames its preview against.</summary>
	public const string Background = "background";

	/// <summary>The states a state-mapping editor's rules and fallback may pick.</summary>
	public const string States = "states";

	/// <summary>The caption for a segmented boolean's <c>false</c> option.</summary>
	public const string FalseLabel = "falseLabel";

	/// <summary>The caption for a segmented boolean's <c>true</c> option.</summary>
	public const string TrueLabel = "trueLabel";

	/// <summary>An element's share of a non-wrapping stack row's main-axis space, relative to its siblings'
	/// own shares. Meaningful only for a direct child of a stack whose own <see cref="Wrap" /> is
	/// <c>false</c>. Not called "weight" on the wire either: the widget component profile already uses that
	/// name for a text run's font weight, on the very same node property namespace.</summary>
	public const string RowWeight = "rowWeight";

	/// <summary>Whether a stack keeps its children on one line instead of wrapping them onto more than one.
	/// Absent means the existing wrapping behaviour.</summary>
	public const string Wrap = "wrap";

	/// <summary>Whether an input's caption is left unrendered because its surroundings already name it - a
	/// heading above it, or a sibling it is paired with.</summary>
	public const string HideLabel = "hideLabel";

	/// <summary>Whether a multiple selection renders as a list the user puts in order, making the order of its
	/// value meaningful.</summary>
	public const string Reorderable = "reorderable";

#pragma warning restore CA1720

	/// <summary>The property keys this package ships names for, in declaration order. Not exhaustive - see the
	/// type's remarks.</summary>
	public static readonly IReadOnlyList<string> WellKnown =
	[
		Events, Value, Label, Description, Placeholder, DefaultValue, Required, Disabled, LiteralOnly,
		SupportsReset, Transient, ValidationRegex, MaxLength, VisibleWhen, Invalid, ValidationMessage, Min, Max, Step,
		ShowSlider, Multiline, Options, OptionsSourceId, DynamicOptions, AllowsCustomValue, Loading, Error,
		CacheSeconds, FilterDebounceMs, FileExtensions, Language, AutoPrefixHttps, Title, StepId, State,
		CanSubmit, Direction, Text, Url, DefaultExpanded, ClearOnCollapse, Severity, For, Triggers, CanRun,
		IntegrationId, VariableTypes, WritableOnly, Capability, ConfigurationEntries, Segmented, Cards, Icon,
		AspectRatio, Background, States, FalseLabel, TrueLabel, RowWeight, Wrap, HideLabel,
		Reorderable,
	];
}
