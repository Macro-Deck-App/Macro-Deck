namespace MacroDeckHost.Integrations.Voicemeeter;

internal enum VoicemeeterEdition
{
	None = 0,
	Standard = 1,
	Banana = 2,
	Potato = 3
}

internal sealed record VoicemeeterLayout(int Strips, int PhysicalStrips, int Buses, int PhysicalBuses)
{
	public static readonly VoicemeeterLayout None = new(0, 0, 0, 0);

	private static readonly VoicemeeterLayout _standard = new(3, 2, 2, 1);
	private static readonly VoicemeeterLayout _banana = new(5, 3, 5, 3);
	private static readonly VoicemeeterLayout _potato = new(8, 5, 8, 5);

	public static VoicemeeterLayout Widest => _potato;

	public int VirtualBuses => Buses - PhysicalBuses;

	public bool HasRecorder => Buses > _standard.Buses;

	public static VoicemeeterLayout For(VoicemeeterEdition edition) => edition switch
	{
		VoicemeeterEdition.Standard => _standard,
		VoicemeeterEdition.Banana => _banana,
		VoicemeeterEdition.Potato => _potato,
		_ => None
	};

	public IReadOnlyList<string> BusAssignmentNames()
	{
		var names = new List<string>(Buses);
		for (var i = 0; i < PhysicalBuses; i++)
		{
			names.Add($"A{i + 1}");
		}

		for (var i = 0; i < VirtualBuses; i++)
		{
			names.Add($"B{i + 1}");
		}

		return names;
	}

	public string? BusAssignmentName(int busIndex)
	{
		if (busIndex < 0 || busIndex >= Buses)
		{
			return null;
		}

		return busIndex < PhysicalBuses ? $"A{busIndex + 1}" : $"B{busIndex - PhysicalBuses + 1}";
	}
}

internal static class VoicemeeterEditions
{
	public static VoicemeeterEdition FromRawType(int rawType) => rawType switch
	{
		1 => VoicemeeterEdition.Standard,
		2 => VoicemeeterEdition.Banana,
		3 => VoicemeeterEdition.Potato,
		_ => VoicemeeterEdition.None
	};

	public static string DisplayName(VoicemeeterEdition edition) => edition switch
	{
		VoicemeeterEdition.Standard => "Voicemeeter",
		VoicemeeterEdition.Banana => "Voicemeeter Banana",
		VoicemeeterEdition.Potato => "Voicemeeter Potato",
		_ => "Unknown"
	};

	public static int ToRunType(VoicemeeterEdition edition) => edition switch
	{
		VoicemeeterEdition.Standard => 1,
		VoicemeeterEdition.Banana => 2,
		VoicemeeterEdition.Potato => Environment.Is64BitProcess ? 6 : 3,
		_ => 0
	};
}
