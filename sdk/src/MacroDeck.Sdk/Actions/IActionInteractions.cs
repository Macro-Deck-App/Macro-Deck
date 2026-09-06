using MacroDeck.Sdk.MusicPlayer;

namespace MacroDeck.Sdk.Actions;

/// <summary>
/// Runtime interaction surface an action can use to ask the originating client a question during
/// execution. Populated by the host on <see cref="ActionExecutionContext"/> per execution. Calls
/// are fire-and-forget (the host pushes a request to the client, which replies via a separate
/// transport call); they never block the action flow.
/// </summary>
public interface IActionInteractions
{
	/// <summary>
	/// Asks the originating client to pick a track or playlist from the provider's catalog. The
	/// client opens a picker dialog and replies through the host's submit-pick transport endpoint,
	/// which performs the actual playback. This call returns immediately.
	/// </summary>
	/// <param name="originClientId">The client that triggered the action (the picker is shown there).</param>
	/// <param name="instanceId">The globally-unique music player instance id the pick applies to.</param>
	void RequestItemPicker(string? originClientId,
		string instanceId,
		MusicPlayerCatalogItemKind kind,
		string? prompt = null);

	/// <summary>
	/// Asks the originating client to pick a playback device from the provider's device list. The
	/// client opens a picker dialog and replies through the host's submit-pick transport endpoint,
	/// which performs the actual transfer. This call returns immediately.
	/// </summary>
	/// <param name="originClientId">The client that triggered the action (the picker is shown there).</param>
	/// <param name="instanceId">The globally-unique music player instance id the pick applies to.</param>
	/// <param name="startPlayback">
	/// Whether the reply should also start playback on the picked device. Carried on the request
	/// because the picker round trip carries none of the action's other parameters - the action has
	/// already returned by the time the user answers, so this is the only way the reply learns whether
	/// it is a "Play on Device" or a "Transfer Playback" pick.
	/// </param>
	void RequestDevicePicker(string? originClientId,
		string instanceId,
		bool startPlayback,
		string? prompt = null);
}
