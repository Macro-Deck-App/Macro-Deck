namespace MacroDeck.Ui.Model.Surfaces;

/// <summary>
/// The <see cref="UiSurface.Attributes" /> keys a <see cref="UiSurfaceKinds.Dialog" /> surface carries: a
/// transient interaction an action opened while it runs, which the action then waits on.
/// </summary>
/// <remarks>
/// <para>
/// A dialog is <see cref="UiSessionModes.Exclusive" /> by construction. It is opened for one client, its
/// tree may carry entered values, and it ends the moment that client completes or cancels it - so unlike a
/// widget it has no meaningful shared reading.
/// </para>
/// <para>
/// Deliberately absent: the dialog's heading. Macro Deck draws that in its own chrome, and the client
/// learns it from the notification that opened the modal, not from the surface - surface attributes travel
/// to the <i>provider</i>, which is the one party that has no use for it.
/// </para>
/// </remarks>
public static class UiDialogSurfaceAttributes
{
	/// <summary>The modal this session backs. The same id the completion the action waits on carries, which
	/// is what lets a provider serving several modals at once tell them apart.</summary>
	public const string ModalId = "modalId";

	/// <summary>Which of the provider's dialogs to open, as the opening action named it. A provider that
	/// does not recognise it declines the session rather than guessing.</summary>
	public const string ViewId = "viewId";

	/// <summary>What the opening action passed in, as the JSON object it was serialized as. Absent reads as
	/// empty.</summary>
	public const string Data = "data";
}
