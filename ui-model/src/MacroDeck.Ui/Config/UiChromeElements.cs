using MacroDeck.Ui.Dsl;

namespace MacroDeck.Ui.Config;

/// <summary>
/// The root of a configuration surface: a multi-step setup flow. Counterpart of the existing config flow
/// dialog, whose Continue and Cancel buttons and whose submit-enabled gate this element carries.
///
/// <para>
/// Only the <b>active</b> step is a child. A completed step is removed from the tree and going back rebuilds
/// it, because a step that is not being shown has no rendering and keeping it would make the tree a history
/// rather than a view.
/// </para>
/// </summary>
public sealed record UiFlow : UiContainer
{
	/// <summary>The dialog's heading.</summary>
	public UiText Title { get; init; }

	/// <summary>The step being shown, which is the id the next submit belongs to.</summary>
	public UiValue<string> StepId { get; init; }

	/// <summary>Where the flow is: showing a step, waiting on an external authorization, finished, failed. An
	/// open vocabulary, like every type string in this profile.</summary>
	public UiValue<string> State { get; init; }

	/// <summary>Whether the submit affordance is enabled. Counterpart of the dialog's own gate, which is what
	/// validation feeds: a flow with an invalid visible field cannot continue.</summary>
	public UiValue<bool> CanSubmit { get; init; }

	/// <inheritdoc />
	public override string Type => UiConfigPrimitives.Flow;

	/// <inheritdoc />
	protected internal override void DeclareProperties(UiPropertyDeclaration properties)
	{
		ArgumentNullException.ThrowIfNull(properties);

		base.DeclareProperties(properties);

		properties.Set(UiConfigProperties.Title, Title.Value);
		properties.Set(UiConfigProperties.StepId, StepId);
		properties.Set(UiConfigProperties.State, State);
		properties.Set(UiConfigProperties.CanSubmit, CanSubmit);
	}
}

/// <summary>One screen of a flow. Counterpart of the existing config flow step, down to the description being
/// the sentence that introduces the instructions rather than the instructions themselves.</summary>
public sealed record UiStep : UiContainer
{
	/// <summary>The id this step submits under.</summary>
	public UiValue<string> StepId { get; init; }

	/// <summary>The step's heading.</summary>
	public UiText Title { get; init; }

	/// <summary>The sentence introducing the step, or the whole guide when there are no instructions.</summary>
	public UiText Description { get; init; }

	/// <inheritdoc />
	public override string Type => UiConfigPrimitives.Step;

	/// <inheritdoc />
	protected internal override void DeclareProperties(UiPropertyDeclaration properties)
	{
		ArgumentNullException.ThrowIfNull(properties);

		base.DeclareProperties(properties);

		properties.Set(UiConfigProperties.StepId, StepId);
		properties.Set(UiConfigProperties.Title, Title.Value);
		properties.Set(UiConfigProperties.Description, Description.Value);
	}
}

/// <summary>A run of children laid out in one direction. The layout primitive the existing dialog and parameter
/// list express implicitly through their own markup; making it an element is what lets a plugin group fields
/// without one.</summary>
// CA1711 reserves the Stack suffix for a LIFO collection. This is the layout element whose type string is
// "stack", derived from what the existing dialog renders; renaming the type would make it disagree with the wire
// name a renderer switches on.
#pragma warning disable CA1711
public sealed record UiConfigStack : UiContainer
#pragma warning restore CA1711
{
	/// <summary>Which way the children run. An open vocabulary; a renderer that does not know a value lays them
	/// out the way it does by default.</summary>
	public UiValue<string> Direction { get; init; }

	/// <summary>Whether the row keeps its children on one line instead of wrapping them onto more than one
	/// when they do not fit. Absent means today's wrapping behaviour, so an existing stack renders unchanged.
	/// Set <c>false</c> together with a <see cref="UiElement.RowWeight" /> on the children that should share
	/// the row's width in proportion rather than size to their own content.</summary>
	public UiValue<bool> Wrap { get; init; }

	/// <inheritdoc />
	public override string Type => UiConfigPrimitives.Stack;

	/// <inheritdoc />
	protected internal override void DeclareProperties(UiPropertyDeclaration properties)
	{
		ArgumentNullException.ThrowIfNull(properties);

		base.DeclareProperties(properties);

		properties.Set(UiConfigProperties.Direction, Direction);
		properties.Set(UiConfigProperties.Wrap, Wrap);
	}
}

/// <summary>
/// A segmented set of panels, each a <see cref="UiTab" />; only the active one's children render.
/// Counterpart of the original Action Button appearance form's Label/Background/Border switch, drawn as a
/// segmented control over the tab labels.
///
/// <para>
/// Which tab is active is deliberately <b>never in the tree</b>, for the same reason
/// <see cref="UiAdvancedSection.DefaultExpanded" />'s remarks give for expansion: it is per-client state,
/// and a shared session would broadcast one user's tab switch to everyone else. A renderer keeps it as its
/// own local state and shows only that tab's children.
/// </para>
/// </summary>
public sealed record UiTabs : UiContainer
{
	/// <inheritdoc />
	public override string Type => UiConfigPrimitives.Tabs;
}

/// <summary>One panel of a <see cref="UiTabs" />, named by its label.</summary>
public sealed record UiTab : UiContainer
{
	/// <summary>The strip's caption for this panel.</summary>
	public UiText Label { get; init; }

	/// <inheritdoc />
	public override string Type => UiConfigPrimitives.Tab;

	/// <inheritdoc />
	protected internal override void DeclareProperties(UiPropertyDeclaration properties)
	{
		ArgumentNullException.ThrowIfNull(properties);

		base.DeclareProperties(properties);

		properties.Set(UiConfigProperties.Label, Label.Value);
	}
}

/// <summary>A section caption. Counterpart of the existing dialog's "Configuration" label, which separates the
/// setup guide from the fields.</summary>
public sealed record UiHeading : UiLeaf
{
	/// <summary>The caption.</summary>
	public UiText Text { get; init; }

	/// <inheritdoc />
	public override string Type => UiConfigPrimitives.Heading;

	/// <inheritdoc />
	protected internal override void DeclareProperties(UiPropertyDeclaration properties)
	{
		ArgumentNullException.ThrowIfNull(properties);

		base.DeclareProperties(properties);

		properties.Set(UiConfigProperties.Text, Text.Value);
	}
}

/// <summary>A paragraph of explanatory text. Counterpart of the existing step description, as a standalone
/// element for prose that belongs somewhere other than at the top of a step.</summary>
public sealed record UiProse : UiLeaf
{
	/// <summary>The paragraph.</summary>
	public UiText Text { get; init; }

	/// <summary>Marks the paragraph as a status line rather than an explanatory one, with a small leading
	/// accent dot coloured the same way <see cref="UiBanner" />'s own <see cref="Severity" /> is - the
	/// Action Button's "Currently &lt;state&gt;" readout under the state row (issue #837) rather than a new
	/// node type for one line of muted, secondary text. An open vocabulary, like <see cref="UiBanner" />'s;
	/// absent renders as a plain paragraph.</summary>
	public UiValue<string> Severity { get; init; }

	/// <inheritdoc />
	public override string Type => UiConfigPrimitives.Prose;

	/// <inheritdoc />
	protected internal override void DeclareProperties(UiPropertyDeclaration properties)
	{
		ArgumentNullException.ThrowIfNull(properties);

		base.DeclareProperties(properties);

		properties.Set(UiConfigProperties.Text, Text.Value);
		properties.Set(UiConfigProperties.Severity, Severity);
	}
}

/// <summary>
/// An ordered list of <see cref="UiInstruction" /> children. Counterpart of the existing step's instruction
/// list, which the dialog renders numbered above the fields.
///
/// <para>
/// A container rather than a property holding a list of strings, because an instruction carries copy blocks of
/// its own - and because a numbered list built out of nodes is one a patch can insert into.
/// </para>
/// </summary>
public sealed record UiInstructions : UiContainer
{
	/// <inheritdoc />
	public override string Type => UiConfigPrimitives.Instructions;
}

/// <summary>
/// One setup instruction. Counterpart of the existing config flow instruction.
///
/// <para>
/// Deliberately carries <b>no number</b>: the existing model is explicit that an entry's position is the number
/// the user sees and that an author must never number the text themselves. A <c>number</c> property would
/// invite exactly that, and a renumbering after an insert would then have to patch every following instruction.
/// </para>
/// </summary>
public sealed record UiInstruction : UiLeaf
{
	/// <summary>The instruction. Ends with a colon when a copy block follows it, rather than repeating the
	/// value inline.</summary>
	public UiText Text { get; init; }

	/// <inheritdoc />
	public override string Type => UiConfigPrimitives.Instruction;

	/// <inheritdoc />
	protected internal override void DeclareProperties(UiPropertyDeclaration properties)
	{
		ArgumentNullException.ThrowIfNull(properties);

		base.DeclareProperties(properties);

		properties.Set(UiConfigProperties.Text, Text.Value);
	}
}

/// <summary>
/// A labeled value the user has to carry over to an external service - a redirect URI to register, a device
/// code to type. Counterpart of the existing config flow copy value, shown verbatim and monospaced with a copy
/// affordance.
///
/// <para>
/// Raises <b>no event</b>. Copying is client-local: it touches the clipboard and nothing on this side, and an
/// event reporting it would put a keystroke's worth of user behaviour on the wire for no one to act on.
/// </para>
/// </summary>
public sealed record UiCopyValue : UiLeaf
{
	/// <summary>A short caption, not a sentence.</summary>
	public UiText Label { get; init; }

	/// <summary>The value, passed exactly as the external service must receive it. Never reformatted.</summary>
	public UiValue<string> Value { get; init; }

	/// <inheritdoc />
	public override string Type => UiConfigPrimitives.CopyValue;

	/// <inheritdoc />
	protected internal override void DeclareProperties(UiPropertyDeclaration properties)
	{
		ArgumentNullException.ThrowIfNull(properties);

		base.DeclareProperties(properties);

		properties.Set(UiConfigProperties.Label, Label.Value);
		properties.Set(UiConfigProperties.Value, Value);
	}
}

/// <summary>
/// A labeled link - setup documentation, a provider's developer portal. Counterpart of the existing config flow
/// link.
///
/// <para>
/// Raises <c>activate</c> rather than being navigated as an href, because the existing dialog opens a link
/// through the shell's external-link service instead of inside the surface. A surface rendered outside a
/// browser has no href to follow at all, so the event is the portable form.
/// </para>
/// </summary>
public sealed record UiLink : UiLeaf
{
	/// <summary>The link text.</summary>
	public UiText Label { get; init; }

	/// <summary>Where the link goes.</summary>
	public UiValue<string> Url { get; init; }

	/// <inheritdoc />
	public override string Type => UiConfigPrimitives.Link;

	/// <inheritdoc />
	protected internal override void DeclareProperties(UiPropertyDeclaration properties)
	{
		ArgumentNullException.ThrowIfNull(properties);

		base.DeclareProperties(properties);

		properties.Set(UiConfigProperties.Label, Label.Value);
		properties.Set(UiConfigProperties.Url, Url);
	}
}

/// <summary>
/// Fields hidden behind an "advanced configuration" affordance. Counterpart of the existing step's advanced
/// fields, which the dialog puts behind a switch and gates Continue independently of.
///
/// <para>
/// Carries <see cref="DefaultExpanded" /> and never a current <c>expanded</c>. Expansion is per-client state:
/// in a shared session one user opening the section would open it for everyone, and the model forbids
/// client-local state from the tree. What the author decided is authored data; what the user did with it is
/// not. The <c>expand</c> and <c>collapse</c> events are the client telling this side what happened, which is a
/// different thing from the tree carrying it.
/// </para>
/// </summary>
public sealed record UiAdvancedSection : UiContainer
{
	/// <summary>The switch's caption.</summary>
	public UiText Label { get; init; }

	/// <summary>Whether the section starts open. The existing dialog opens it on its own when one of the fields
	/// already carries a value, so a re-shown step never hides something the user typed.</summary>
	public UiValue<bool> DefaultExpanded { get; init; }

	/// <summary>Whether collapsing the section clears the fields inside it, which the existing dialog does so a
	/// hidden advanced value cannot silently take effect.</summary>
	public UiValue<bool> ClearOnCollapse { get; init; }

	/// <inheritdoc />
	public override string Type => UiConfigPrimitives.AdvancedSection;

	/// <inheritdoc />
	protected internal override void DeclareProperties(UiPropertyDeclaration properties)
	{
		ArgumentNullException.ThrowIfNull(properties);

		base.DeclareProperties(properties);

		properties.Set(UiConfigProperties.Label, Label.Value);
		properties.Set(UiConfigProperties.DefaultExpanded, DefaultExpanded);
		properties.Set(UiConfigProperties.ClearOnCollapse, ClearOnCollapse);
	}
}

/// <summary>A visual separator. Counterpart of the existing dialog's rule between the setup guide and the
/// fields.</summary>
public sealed record UiDivider : UiLeaf
{
	/// <inheritdoc />
	public override string Type => UiConfigPrimitives.Divider;
}

/// <summary>A message about the flow as a whole rather than about one field. Counterpart of the existing
/// dialog's banner, which carries the error or status message a step returned.</summary>
public sealed record UiBanner : UiLeaf
{
	/// <summary>How serious the message is. An open vocabulary; a renderer that does not know a value shows the
	/// message plainly.</summary>
	public UiValue<string> Severity { get; init; }

	/// <summary>The message.</summary>
	public UiText Text { get; init; }

	/// <inheritdoc />
	public override string Type => UiConfigPrimitives.Banner;

	/// <inheritdoc />
	protected internal override void DeclareProperties(UiPropertyDeclaration properties)
	{
		ArgumentNullException.ThrowIfNull(properties);

		base.DeclareProperties(properties);

		properties.Set(UiConfigProperties.Severity, Severity);
		properties.Set(UiConfigProperties.Text, Text.Value);
	}
}

/// <summary>
/// A message about one field. Counterpart of the existing per-field error, which the editor and the dialog both
/// render under the control it belongs to.
///
/// <para>
/// A node of its own rather than a property on the field, because a message appearing and disappearing is then
/// one insert and one remove naming that message - not a change to the field, which would put a
/// <c>set-properties</c> on a control the user is typing into.
/// </para>
/// </summary>
public sealed record UiValidationMessage : UiLeaf
{
	/// <summary>The message.</summary>
	public UiText Text { get; init; }

	/// <summary>The id of the input this message belongs to. An input's id is the name it submits as, which is
	/// the same key the existing per-field errors are already keyed by.</summary>
	public UiValue<string> For { get; init; }

	/// <inheritdoc />
	public override string Type => UiConfigPrimitives.ValidationMessage;

	/// <inheritdoc />
	protected internal override void DeclareProperties(UiPropertyDeclaration properties)
	{
		ArgumentNullException.ThrowIfNull(properties);

		base.DeclareProperties(properties);

		properties.Set(UiConfigProperties.Text, Text.Value);
		properties.Set(UiConfigProperties.For, For);
	}
}

/// <summary>Work in progress the user has to wait for. Counterpart of the existing dialog's loading and
/// waiting-for-authorization states, both of which are a spinner plus a line of text.</summary>
public sealed record UiBusy : UiLeaf
{
	/// <summary>What is being waited for.</summary>
	public UiText Text { get; init; }

	/// <inheritdoc />
	public override string Type => UiConfigPrimitives.Busy;

	/// <inheritdoc />
	protected internal override void DeclareProperties(UiPropertyDeclaration properties)
	{
		ArgumentNullException.ThrowIfNull(properties);

		base.DeclareProperties(properties);

		properties.Set(UiConfigProperties.Text, Text.Value);
	}
}

/// <summary>
/// A labeled control that does something rather than going somewhere: applying a preset, running a check,
/// revealing a value. It raises <c>activate</c> and contributes no key to the surrounding transaction, which
/// is what separates it from every input - a preset that wrote its own key would leave a stray value nothing
/// reads.
/// </summary>
public sealed record UiConfigButton : UiLeaf
{
	/// <summary>The control's text, and its accessible name whether or not that text is drawn.</summary>
	public UiText Label { get; init; }

	/// <summary>
	/// Draws the control as that icon rather than as its text. The <see cref="Label" /> is still required
	/// and becomes the accessible name, so an icon-only control is never nameless - a renderer with no
	/// such icon falls back to drawing the text and loses nothing but the compactness.
	/// </summary>
	public UiValue<string> Icon { get; init; }

	/// <inheritdoc />
	public override string Type => UiConfigPrimitives.Button;

	/// <inheritdoc />
	protected internal override void DeclareProperties(UiPropertyDeclaration properties)
	{
		ArgumentNullException.ThrowIfNull(properties);

		base.DeclareProperties(properties);

		properties.Set(UiConfigProperties.Label, Label.Value);
		properties.Set(UiConfigProperties.Icon, Icon);
	}
}
