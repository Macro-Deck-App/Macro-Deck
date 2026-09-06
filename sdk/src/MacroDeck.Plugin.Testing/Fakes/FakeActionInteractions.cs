using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.MusicPlayer;

namespace MacroDeck.Plugin.Testing.Fakes;

/// <summary>
/// One <see cref="IActionInteractions.RequestItemPicker" /> call recorded by
/// <see cref="FakeActionInteractions" />.
/// </summary>
public sealed record ItemPickerRequest
{
	/// <summary>The client the picker should be shown on, if one was given.</summary>
	public string? OriginClientId { get; init; }

	/// <summary>The music player instance id the pick applies to.</summary>
	public required string InstanceId { get; init; }

	/// <summary>Which kind of catalog item the client should let the user pick.</summary>
	public required MusicPlayerCatalogItemKind Kind { get; init; }

	/// <summary>The prompt shown to the user, if one was given.</summary>
	public string? Prompt { get; init; }
}

/// <summary>
/// One <see cref="IActionInteractions.RequestDevicePicker" /> call recorded by
/// <see cref="FakeActionInteractions" />.
/// </summary>
public sealed record DevicePickerRequest
{
	/// <summary>The client the picker should be shown on, if one was given.</summary>
	public string? OriginClientId { get; init; }

	/// <summary>The music player instance id the pick applies to.</summary>
	public required string InstanceId { get; init; }

	/// <summary>Whether the reply should also start playback on the picked device.</summary>
	public required bool StartPlayback { get; init; }

	/// <summary>The prompt shown to the user, if one was given.</summary>
	public string? Prompt { get; init; }
}

/// <summary>
/// In-memory <see cref="IActionInteractions" />. Both methods are synchronous, record their full
/// argument set, and never throw regardless of input - including a null <c>originClientId</c> or
/// <c>prompt</c>, which the interface already allows, and a null <c>instanceId</c>, which it does not
/// but which this fake tolerates anyway rather than crash a test run over a plugin bug it isn't its job
/// to catch.
/// </summary>
public sealed class FakeActionInteractions : IActionInteractions
{
	private readonly Lock _gate = new();
	private readonly List<ItemPickerRequest> _itemPickerRequests = [];
	private readonly List<DevicePickerRequest> _devicePickerRequests = [];

	/// <summary>Every <see cref="RequestItemPicker" /> call so far, in call order.</summary>
	public IReadOnlyList<ItemPickerRequest> ItemPickerRequests
	{
		get
		{
			lock (_gate)
			{
				return [.. _itemPickerRequests];
			}
		}
	}

	/// <summary>Every <see cref="RequestDevicePicker" /> call so far, in call order.</summary>
	public IReadOnlyList<DevicePickerRequest> DevicePickerRequests
	{
		get
		{
			lock (_gate)
			{
				return [.. _devicePickerRequests];
			}
		}
	}

	/// <inheritdoc />
	public void RequestItemPicker(string? originClientId,
		string instanceId,
		MusicPlayerCatalogItemKind kind,
		string? prompt = null)
	{
		lock (_gate)
		{
			_itemPickerRequests.Add(new ItemPickerRequest
			{
				OriginClientId = originClientId,
				InstanceId = instanceId,
				Kind = kind,
				Prompt = prompt
			});
		}
	}

	/// <inheritdoc />
	public void RequestDevicePicker(string? originClientId,
		string instanceId,
		bool startPlayback,
		string? prompt = null)
	{
		lock (_gate)
		{
			_devicePickerRequests.Add(new DevicePickerRequest
			{
				OriginClientId = originClientId,
				InstanceId = instanceId,
				StartPlayback = startPlayback,
				Prompt = prompt
			});
		}
	}
}
