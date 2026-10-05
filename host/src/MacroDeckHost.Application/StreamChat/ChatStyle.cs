using System.Buffers;
using System.Security.Cryptography;
using System.Text;

namespace MacroDeckHost.Application.StreamChat;

public static class ChatStyle
{
	private static readonly string[] _defaultColors =
	[
		"#ff0000", "#0000ff", "#008000", "#b22222", "#ff7f50", "#9acd32", "#ff4500", "#2e8b57", "#daa520",
		"#d2691e", "#5f9ea0", "#1e90ff", "#ff69b4", "#8a2be2", "#00ff7f",
	];

	private static readonly SearchValues<char> _hexDigits = SearchValues.Create("0123456789abcdefABCDEF");

	public static string NormalizeColor(string? color, string userId)
		=> color is { Length: 7 } && color[0] == '#' && !color.AsSpan(1).ContainsAnyExcept(_hexDigits)
			? color.ToLowerInvariant()
			: DefaultColor(userId);

	public static string DefaultColor(string userId)
		=> _defaultColors[Hash(userId)[0] % _defaultColors.Length];

	public static string MessageKey(string messageId) => "m" + Convert.ToHexStringLower(Hash(messageId))[..24];

	private static byte[] Hash(string? value) => SHA256.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty));
}
