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

	IActionExecutor CreateExecutor();
}
