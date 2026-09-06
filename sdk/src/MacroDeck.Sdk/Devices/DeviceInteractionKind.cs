namespace MacroDeck.Sdk.Devices;

/// <summary>
/// The kind of hardware interaction a device reports through <see cref="IDeviceSession.SendInteractionAsync" />.
/// </summary>
/// <remarks>
/// Only <see cref="Press" />, <see cref="Release" />, <see cref="ShortPress" /> and <see cref="LongPress" />
/// are executed today; the remaining kinds are accepted by the contract and reported unsupported until a
/// widget model exists for them. The enum exists so adding kinds later is additive rather than a breaking
/// change to a smaller one.
///
/// <para>
/// The host synthesizes <c>onShortPress</c>/<c>onLongPress</c> from a <see cref="Press" />/
/// <see cref="Release" /> pair using a 600 ms threshold. A provider that already distinguishes a short
/// from a long press in its own firmware or driver should send <see cref="ShortPress" /> or
/// <see cref="LongPress" /> explicitly instead of the raw press/release pair, so the host does not have
/// to re-derive what the device already knows.
/// </para>
/// </remarks>
public enum DeviceInteractionKind
{
	Unknown = 0,

	Press = 1,

	Release = 2,

	ShortPress = 3,

	LongPress = 4,

	EncoderTurn = 5,

	EncoderPress = 6,

	EncoderRelease = 7,

	TouchStart = 8,

	TouchMove = 9,

	TouchEnd = 10,

	Analog = 11
}
