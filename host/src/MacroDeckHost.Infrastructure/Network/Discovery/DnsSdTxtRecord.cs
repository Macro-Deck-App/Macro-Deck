using System.Text;
using MacroDeckHost.Application.Network.Discovery;

namespace MacroDeckHost.Infrastructure.Network.Discovery;

internal static class DnsSdTxtRecord
{
	private const int MaximumEntryBytes = 255;

	public static byte[] Encode(IReadOnlyList<TxtEntry> entries)
	{
		var buffer = new List<byte>();
		foreach (var entry in entries)
		{
			var bytes = Encoding.UTF8.GetBytes($"{entry.Key}={entry.Value}");
			var length = Math.Min(bytes.Length, MaximumEntryBytes);
			buffer.Add((byte)length);
			buffer.AddRange(bytes.AsSpan(0, length));
		}

		return buffer.ToArray();
	}
}
