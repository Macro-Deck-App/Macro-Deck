namespace MacroDeck.Sdk.ConfigFlow;

/// <summary>
/// An <see cref="IConfigFlowProvider" /> whose flows can also render themselves as a Macro Deck UI
/// tree.
/// </summary>
/// <remarks>
/// The declared field list stays mandatory: a client that cannot render a tree is served the ordinary
/// <see cref="ConfigFlowStep" /> path unchanged, and the step's fields remain how Macro Deck learns
/// which submitted values are secret. Deriving from <see cref="IConfigFlowProvider" /> is what makes
/// that a compile-time guarantee rather than a convention.
/// </remarks>
public interface IUiConfigFlowProvider : IConfigFlowProvider
{
	/// <summary>
	/// Whether flows created by <see cref="IConfigFlowProvider.CreateConfigFlow" /> serve a UI tree.
	/// </summary>
	/// <remarks>
	/// Read when the provider is discovered, before anything has been initialized, so it must be
	/// side-effect free and must not depend on a live connection or a completed configuration. A
	/// provider that reports <c>false</c> is treated exactly like a plain
	/// <see cref="IConfigFlowProvider" />.
	/// </remarks>
	bool ServesConfigUiTree => true;
}
