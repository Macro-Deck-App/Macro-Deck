using System.Collections.Concurrent;
using System.Text.Json;
using MacroDeck.Plugin.Hosting.Transport;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Sdk.Widgets;

namespace MacroDeck.Plugin.Hosting.Integrations.HostApis;

/// <summary>
/// Serves the synchronous members of <c>IDeckNavigator</c>, <c>IScriptApi</c> and <c>IWidgetApi</c>
/// from the host's last <c>host.state</c> push for each <see cref="HostApis"/> member, rather than a
/// blocking round trip: <c>GetFolders()</c>, <c>GetScripts()</c> and friends are synchronous SDK
/// contracts, and a <c>host.invoke</c> per call would either block a thread on an async round trip or
/// require making the SDK contract asynchronous, which is not this step's to do.
///
/// <para>
/// Before the first push for a given API, every reader gets an empty list (or <c>false</c> for
/// <c>Exists</c>) rather than throwing - the mirror of the host-side snapshot rule
/// <c>RemotePluginCapabilitySnapshot.Empty</c> applies for the opposite direction: a plugin that has
/// not yet heard from the host behaves as if the host has nothing to offer yet, not as if it is broken.
/// </para>
/// </summary>
internal sealed class HostStateCache(PluginConnectionState connectionState)
{
	private readonly ConcurrentDictionary<string, JsonElement?> _byApi = new(StringComparer.Ordinal);

	/// <summary>
	/// Raised after a <c>host.state</c> push for <see cref="HostApis.Config" /> is applied -
	/// <see cref="Integrations.IntegrationLifecycleHostedService" /> is the subscriber, re-running every
	/// integration's <c>InitializeAsync</c> the same way it does for a non-resumed reconnect. This is the
	/// SDK-side half of closing issue #413's config-change gap: <c>config</c> carries no data of its own
	/// (see <see cref="HostStatePayload.Data" />'s "absent when the API has nothing to push"), so this
	/// push is purely the invalidation signal - the fresh values still come from the ordinary
	/// <c>host.invoke</c> round trip <c>InitializeAsync</c> already makes.
	/// </summary>
	public event Action? ConfigChanged;

	/// <summary>Applies one <c>host.state</c> push, replacing whatever was cached for its API.</summary>
	public void Apply(ProtocolEnvelope envelope)
	{
		var payload = envelope.Payload?.Deserialize<HostStatePayload>(PluginProtocolJson.Options);
		if (payload?.Api is not { Length: > 0 } api)
		{
			return;
		}

		_byApi[api] = payload.Data;

		if (string.Equals(api, Protocol.Callbacks.HostApis.Config, StringComparison.Ordinal))
		{
			ConfigChanged?.Invoke();
		}
	}

	/// <summary>The last pushed list for <paramref name="api"/>, or empty when nothing has been pushed
	/// (or the host pushed nothing) yet.</summary>
	public IReadOnlyList<T> GetList<T>(string api) => Get<List<T>>(api) ?? [];

	/// <summary>
	/// The last pushed widgets list, reverse-mapped from whichever wire shape the negotiated protocol
	/// version pushes - <see cref="WidgetTargetInfoDtoV1" /> or <see cref="WidgetTargetInfoDtoV2" />. A
	/// v1 payload carries no state list, so it yields a <see cref="WidgetTargetInfo" /> with empty
	/// <see cref="WidgetTargetInfo.States" /> and only the deprecated
	/// <see cref="WidgetTargetInfo.HasOnOffStates" /> set - the mirror of the host's own
	/// <c>WidgetStateWireCompatibility</c>.
	/// </summary>
	public IReadOnlyList<WidgetTargetInfo> GetWidgets()
	{
		if (connectionState.NegotiatedVersion is >= 2)
		{
			return [.. GetList<WidgetTargetInfoDtoV2>(Protocol.Callbacks.HostApis.Widgets).Select(ToWidgetTargetInfo)];
		}

		return [.. GetList<WidgetTargetInfoDtoV1>(Protocol.Callbacks.HostApis.Widgets).Select(ToWidgetTargetInfo)];
	}

	private static WidgetTargetInfo ToWidgetTargetInfo(WidgetTargetInfoDtoV1 dto)
		=> new()
		{
			Id = dto.Id,
			Label = dto.Label,
			Location = dto.Location,
			Type = dto.Type,
#pragma warning disable CS0618 // v1 carries only the collapsed flag - the reverse of the same compatibility shim the host applies on the way out.
			HasOnOffStates = dto.HasOnOffStates,
#pragma warning restore CS0618
			States = [],
			AppearanceProperties = [.. dto.AppearanceProperties.Select(property => (WidgetAppearanceProperty)property)]
		};

	private static WidgetTargetInfo ToWidgetTargetInfo(WidgetTargetInfoDtoV2 dto)
		=> new()
		{
			Id = dto.Id,
			Label = dto.Label,
			Location = dto.Location,
			Type = dto.Type,
			States = [.. dto.States.Select(state => new WidgetStateInfo(state.Id, state.Label))],
			CurrentStateId = dto.CurrentStateId,
			AppearanceProperties = [.. dto.AppearanceProperties.Select(property => (WidgetAppearanceProperty)property)]
		};

	/// <summary>The last pushed state for <paramref name="api"/>, deserialized as <typeparamref name="T"/>,
	/// or the type's default when nothing has been pushed yet - used by an API whose pushed shape is not
	/// a bare list, e.g. <c>deck</c>, which pushes both folders and profiles together.</summary>
	public T? Get<T>(string api)
	{
		if (!_byApi.TryGetValue(api, out var data) || data is not { } element)
		{
			return default;
		}

		try
		{
			return element.Deserialize<T>(PluginProtocolJson.Options);
		}
		catch (JsonException)
		{
			return default;
		}
	}
}
