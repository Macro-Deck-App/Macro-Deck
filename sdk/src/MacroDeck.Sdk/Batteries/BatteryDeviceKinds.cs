namespace MacroDeck.Sdk.Batteries;

/// <summary>
/// The kinds of battery device Macro Deck knows, used for <see cref="BatteryDevice.Kind" />. A reader
/// shows a kind it does not know as <see cref="Other" />, so a provider may publish a newer kind
/// without breaking an older Macro Deck.
/// </summary>
public static class BatteryDeviceKinds
{
	public const string Phone = "phone";

	public const string Tablet = "tablet";

	public const string Laptop = "laptop";

	public const string Headphones = "headphones";

	public const string Watch = "watch";

	public const string Controller = "controller";

	public const string Mouse = "mouse";

	public const string Keyboard = "keyboard";

	public const string Speaker = "speaker";

	public const string Other = "other";

	public static readonly IReadOnlyList<string> All =
		[Phone, Tablet, Laptop, Headphones, Watch, Controller, Mouse, Keyboard, Speaker, Other];

	private static readonly HashSet<string> _known = new(All, StringComparer.Ordinal);

	public static bool IsKnown(string? kind)
	{
		return kind is not null && _known.Contains(kind);
	}

	/// <summary>Returns <paramref name="kind" /> when it is one of <see cref="All" />, otherwise <see cref="Other" />.</summary>
	public static string Normalize(string? kind)
	{
		return IsKnown(kind) ? kind! : Other;
	}
}
