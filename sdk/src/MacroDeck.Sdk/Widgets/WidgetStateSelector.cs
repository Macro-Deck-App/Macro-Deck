using MacroDeck.Sdk.Deprecation;

namespace MacroDeck.Sdk.Widgets;

/// <summary>
/// Which appearance of a toggle-mode action button an appearance change applies to.
///
/// <para>
/// Superseded by stable state ids (<see cref="WidgetAppearanceRequest.StateIds" />,
/// <see cref="WidgetTargetInfo.States" />): a widget can now report any number of states with ids of its
/// own choosing, not only the two this enum assumes. Use <see cref="WidgetStates.Current" /> in place of
/// <see cref="Current" />, <see cref="WidgetStates.All" /> in place of <see cref="Both" />, and the
/// widget's actual "on"/"off" state ids in place of <see cref="On" />/<see cref="Off" />.
/// </para>
/// </summary>
[Obsolete(
	"Use stable state ids instead - see WidgetAppearanceRequest.StateIds and WidgetTargetInfo.States. Removed in Macro Deck 4.0.0.")]
[MacroDeckDeprecated("3.0.0",
	"4.0.0",
	"Address states by stable id through StateIds instead of this fixed selector.",
	Replacement = "MacroDeck.Sdk.Widgets.WidgetAppearanceRequest.StateIds")]
public enum WidgetStateSelector
{
	/// <summary>The state the button is showing right now. The default.</summary>
	Current,

	/// <summary>The "on" (toggled) appearance, whether or not it is currently shown.</summary>
	On,

	/// <summary>The "off" (untoggled) appearance, whether or not it is currently shown.</summary>
	Off,

	/// <summary>Both appearances, so the change is visible whichever state the button is in.</summary>
	Both
}
