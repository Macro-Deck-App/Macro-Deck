using MacroDeck.Localization;

namespace MacroDeck.Sdk.Actions;

public interface IActionDefinition
{
	string Id { get; }

	LocalizedText Name { get; }

	LocalizedText Description { get; }

	IReadOnlyList<ActionParameter> Parameters { get; }

	/// <summary>
	/// Where this action is offered. <see cref="MacroDeckPlatform.All" /> by default.
	///
	/// <para>
	/// Narrow it only when the underlying capability <b>does not exist</b> on the other platforms and
	/// nothing can stand in for it - hibernation, for instance, which macOS does not expose as a
	/// user-facing operation. Such an action is then neither listed nor executable there.
	/// </para>
	/// </summary>
	MacroDeckPlatform Platforms => MacroDeckPlatform.All;

	/// <summary>
	/// A well-known job this action performs for Macro Deck itself, one of the <see cref="ActionRoles" />
	/// values, or <c>null</c> (the default) for an ordinary action. A role is a contract on the action's
	/// parameters: see each role for the parameters it requires. Macro Deck decides which declaring
	/// actions it uses for a role; declaring one does not by itself make Macro Deck call the action.
	/// </summary>
	string? Role => null;

	IActionExecutor CreateExecutor();
}
