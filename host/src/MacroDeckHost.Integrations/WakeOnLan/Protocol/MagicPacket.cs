namespace MacroDeckHost.Integrations.WakeOnLan.Protocol;

internal static class MagicPacket
{
	public const int Length = SyncLength + MacRepetitions * MacLength;

	private const int SyncLength = 6;
	private const int MacLength = 6;
	private const int MacRepetitions = 16;

	public static byte[] Build(ReadOnlySpan<byte> mac)
	{
		if (mac.Length != MacLength)
		{
			throw new ArgumentException("A MAC address has six bytes.", nameof(mac));
		}

		var packet = new byte[Length];
		packet.AsSpan(0, SyncLength).Fill(0xFF);
		for (var repetition = 0; repetition < MacRepetitions; repetition++)
		{
			mac.CopyTo(packet.AsSpan(SyncLength + repetition * MacLength));
		}

		return packet;
	}
}
