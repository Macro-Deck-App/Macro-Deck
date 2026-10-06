using System.Security.Cryptography;
using System.Text;

namespace MacroDeckHost.Application.Icons.Included;

public static class IncludedIconPack
{
	public const string SourceId = "macrodeck:included";

	public const string Cpu = "cpu";
	public const string MemoryStick = "memory-stick";
	public const string Gpu = "gpu";
	public const string Battery = "battery";

	public static readonly Guid PackId = new("0199b6f2-7c1e-7a3d-9e51-5f0c2a8d4b61");

	public static IReadOnlyList<string> Names { get; } =
	[
		Cpu, MemoryStick, Gpu, "hard-drive", "server", "network", "wifi", Battery, "battery-charging",
		"thermometer", "fan", "gauge", "activity", "droplet", "zap", "power", "sun", "moon", "cloud", "volume-2",
		"mic", "headphones", "music", "monitor",
	];

	// Name-based, so host code and stored widget data can name an included icon without a lookup and the id
	// survives every re-sync of the pack.
	public static Guid IconId(string name)
	{
		ArgumentException.ThrowIfNullOrEmpty(name);

		var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{PackId:N}/{name}"));
		var bytes = hash.AsSpan(0, 16).ToArray();
		bytes[6] = (byte)((bytes[6] & 0x0F) | 0x50);
		bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);

		return new Guid(bytes, bigEndian: true);
	}
}
