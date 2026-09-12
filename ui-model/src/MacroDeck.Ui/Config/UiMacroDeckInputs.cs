using System.Text.Json;
using MacroDeck.Ui.Config.Options;
using MacroDeck.Ui.Dsl;

namespace MacroDeck.Ui.Config;

/// <summary>
/// The list of action flows something runs, with its triggers, ordering, nesting and drag-and-drop - the
/// Action Button's action editor, reached by naming it.
///
/// <para>
/// This is the reason the profile has high-level controls at all. Expressed as primitives it would be a
/// recursive array of objects whose every renderer would have to reinvent reordering, nesting, condition
/// evaluation and trigger tabs; named, a renderer maps it onto the editor it already ships, and a widget
/// that wants one writes a single node. A renderer that has no such editor declines the type and the node's
/// <see cref="UiElement.Fallback" /> is drawn instead.
/// </para>
///
/// <para>
/// The value is the flow list itself, as the JSON array it is stored as. It is a
/// <see cref="JsonElement" /> rather than a typed list because this package cannot depend on the flow model
/// - the same reason a widget's stored configuration crosses as JSON.
/// </para>
/// </summary>
public sealed record UiActionsListEditor : UiInput<JsonElement>
{
	/// <summary>Which trigger tabs the editor offers. Absent lets the renderer decide from the surface,
	/// which is what a widget wants.</summary>
	public UiValue<IReadOnlyList<string>> Triggers { get; init; }

	/// <summary>Whether the flows may be run from the editor. A widget's own actions can be; a template
	/// being authored against no widget cannot.</summary>
	public UiValue<bool> CanRun { get; init; }

	/// <summary>The states the widget being edited currently declares, as id/label options. An action
	/// inside these flows that picks one of them - Set Button State, or an appearance action's state
	/// selector - offers these alongside whatever the host resolves from stored data, so a state added in
	/// this editor is selectable before the widget has ever been saved. Absent leaves the picker with the
	/// host's answer alone.</summary>
	public UiValue<IReadOnlyList<UiOption>> States { get; init; }

	/// <inheritdoc />
	public override string Type => UiConfigPrimitives.ActionsListEditor;

	/// <inheritdoc />
	protected internal override void DeclareProperties(UiPropertyDeclaration properties)
	{
		ArgumentNullException.ThrowIfNull(properties);

		base.DeclareProperties(properties);

		properties.Set(UiConfigProperties.Triggers, Triggers);
		properties.Set(UiConfigProperties.CanRun, CanRun);
		properties.Set(UiConfigProperties.States, States);
	}
}

/// <summary>One action chosen from the catalog of everything installed, as the qualified action id it is
/// stored as. Distinct from a <see cref="UiChoiceInput" /> over the same ids because the catalog is large,
/// grouped by integration and searched rather than scrolled.</summary>
public sealed record UiActionPickerInput : UiInput<string>
{
	/// <summary>Restricts the catalog to actions owned by one integration. Absent offers all of them.</summary>
	public UiValue<string> IntegrationId { get; init; }

	/// <inheritdoc />
	public override string Type => UiConfigPrimitives.ActionPicker;

	/// <inheritdoc />
	protected internal override void DeclareProperties(UiPropertyDeclaration properties)
	{
		ArgumentNullException.ThrowIfNull(properties);

		base.DeclareProperties(properties);

		properties.Set(UiConfigProperties.IntegrationId, IntegrationId);
	}
}

/// <summary>One variable chosen from the catalog, as the variable name it is stored as.</summary>
/// <remarks>In the widget editor the renderer lists global variables and the edited widget's own
/// widget-scoped variables, never another widget's. Without a widget in scope it lists the whole
/// catalog.</remarks>
public sealed record UiVariablePickerInput : UiInput<string>
{
	/// <summary>Narrows the catalog to variables of these types. Absent offers every type.</summary>
	public UiValue<IReadOnlyList<string>> VariableTypes { get; init; }

	/// <summary>Offers only variables something may write to.</summary>
	public UiValue<bool> WritableOnly { get; init; }

	/// <inheritdoc />
	public override string Type => UiConfigPrimitives.VariablePicker;

	/// <inheritdoc />
	protected internal override void DeclareProperties(UiPropertyDeclaration properties)
	{
		ArgumentNullException.ThrowIfNull(properties);

		base.DeclareProperties(properties);

		properties.Set(UiConfigProperties.VariableTypes, VariableTypes);
		properties.Set(UiConfigProperties.WritableOnly, WritableOnly);
	}
}

/// <summary>One connected device, as the device id it is stored as.</summary>
public sealed record UiDevicePickerInput : UiInput<string>
{
	/// <inheritdoc />
	public override string Type => UiConfigPrimitives.DevicePicker;
}

/// <summary>
/// One integration, or one of its configuration entries when it supports several - as the id it is stored
/// as. A widget that reads from an integration picks the configured instance, not the integration, which is
/// why an integration with one configuration and one with five are the same node here.
/// </summary>
public sealed record UiIntegrationPickerInput : UiInput<string>
{
	/// <summary>Offers only integrations declaring this capability, so a widget that needs a music player
	/// does not list every integration installed.</summary>
	public UiValue<string> Capability { get; init; }

	/// <summary>Picks one of an integration's configuration entries rather than the integration itself.
	/// </summary>
	public UiValue<bool> ConfigurationEntries { get; init; }

	/// <inheritdoc />
	public override string Type => UiConfigPrimitives.IntegrationPicker;

	/// <inheritdoc />
	protected internal override void DeclareProperties(UiPropertyDeclaration properties)
	{
		ArgumentNullException.ThrowIfNull(properties);

		base.DeclareProperties(properties);

		properties.Set(UiConfigProperties.Capability, Capability);
		properties.Set(UiConfigProperties.ConfigurationEntries, ConfigurationEntries);
	}
}

/// <summary>
/// A widget icon's framing - fit, zoom, position and opacity - edited on a live preview of the button
/// face. Names the original Action Button appearance form's icon display block rather than rebuilding
/// drag-to-position and zoom-on-wheel from primitives.
/// </summary>
public sealed record UiIconDisplayInput : UiInput<UiIconDisplay>
{
	/// <summary>The icon the preview draws the framing against.</summary>
	public UiValue<UiIconReference> Icon { get; init; }

	/// <summary>Width divided by height of the widget on the deck, so the preview frames like the real
	/// button.</summary>
	public UiValue<double> AspectRatio { get; init; }

	/// <summary>The background the icon is framed against, so opacity reads the way it will on the deck.
	/// </summary>
	public UiValue<string> Background { get; init; }

	/// <inheritdoc />
	public override string Type => UiConfigPrimitives.IconDisplay;

	/// <inheritdoc />
	protected internal override void DeclareProperties(UiPropertyDeclaration properties)
	{
		ArgumentNullException.ThrowIfNull(properties);

		base.DeclareProperties(properties);

		properties.Set(UiConfigProperties.Icon, Icon);
		properties.Set(UiConfigProperties.AspectRatio, AspectRatio);
		properties.Set(UiConfigProperties.Background, Background);
	}
}

/// <summary>
/// A widget icon's framing, exactly as the appearance form's preview edits it: a fit mode plus zoom,
/// offset and opacity, every field independently optional so a value can override only the ones it means
/// to - the control emits the complete object on every change regardless, never a single field.
///
/// <para>
/// <see cref="Fit" /> is an open string, like every other vocabulary in this profile: a client that does
/// not recognise a value falls back to its own default fit rather than rejecting the whole framing.
/// </para>
/// </summary>
/// <param name="Fit">The fit mode, e.g. <c>contain</c> or <c>cover</c>.</param>
/// <param name="Zoom">Zoom, in percent; 100 is the plain fit.</param>
/// <param name="OffsetX">Horizontal shift, in percent of the widget width.</param>
/// <param name="OffsetY">Vertical shift, in percent of the widget height.</param>
/// <param name="Opacity">Opacity, in percent.</param>
public sealed record UiIconDisplay(
	string? Fit = null,
	double? Zoom = null,
	double? OffsetX = null,
	double? OffsetY = null,
	double? Opacity = null);

/// <summary>
/// A button's state-mapping table: the ordered rules that pick a state from a condition, and the fallback
/// state used when none match. Names the original Action Button editor's state-mapping dialog rather than
/// rebuilding condition rows and a rule list from primitives, exactly like <see cref="UiActionsListEditor" />
/// names the action-flow editor.
///
/// <para>
/// The value is the whole mapping object (rules plus fallback), as the JSON it is stored as - a
/// <see cref="JsonElement" /> for the same reason <see cref="UiActionsListEditor" />'s value is: this
/// package cannot depend on the button's own mapping model.
/// </para>
/// </summary>
public sealed record UiStateMappingEditorInput : UiInput<JsonElement>
{
	/// <summary>The states a rule or the fallback may pick, as id/label options - the node's own states
	/// list, not resolved by the renderer. The variables a condition picks from are not carried here: they
	/// come from a client-side service, the same split <see cref="UiActionsListEditor" /> makes for its own
	/// variable list.</summary>
	public UiValue<IReadOnlyList<UiOption>> States { get; init; }

	/// <inheritdoc />
	public override string Type => UiConfigPrimitives.StateMappingEditor;

	/// <inheritdoc />
	protected internal override void DeclareProperties(UiPropertyDeclaration properties)
	{
		ArgumentNullException.ThrowIfNull(properties);

		base.DeclareProperties(properties);

		properties.Set(UiConfigProperties.States, States);
	}
}
