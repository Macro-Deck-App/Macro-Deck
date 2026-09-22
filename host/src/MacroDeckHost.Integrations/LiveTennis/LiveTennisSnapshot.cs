using System.Globalization;
using System.Text.Json;

namespace MacroDeckHost.Integrations.LiveTennis;

internal sealed record LiveTennisSnapshot(string Scores, int MatchCount, string UpdatedAt)
{
	public static LiveTennisSnapshot Parse(JsonElement root, DateTimeOffset fetchedAt)
	{
		if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
		{
			throw new JsonException("Missing match list");
		}

		var lines = data.EnumerateArray().Take(5).Select(FormatMatch);
		return new LiveTennisSnapshot(string.Join('\n', lines), data.GetArrayLength(),
			fetchedAt.ToString("u", CultureInfo.InvariantCulture));
	}

	private static string FormatMatch(JsonElement match)
	{
		var players = match.GetProperty("players");
		var first = players.GetProperty("p1").GetProperty("name").GetString();
		var second = players.GetProperty("p2").GetProperty("name").GetString();
		var result = $"{first} - {second}: ";
		if (!match.TryGetProperty("score", out var score) || score.ValueKind != JsonValueKind.Object ||
			!score.TryGetProperty("games", out var games) || games.ValueKind != JsonValueKind.Array ||
			games.GetArrayLength() != 2 || games[0].ValueKind != JsonValueKind.Array ||
			games[1].ValueKind != JsonValueKind.Array || games[0].GetArrayLength() == 0 ||
			games[0].GetArrayLength() != games[1].GetArrayLength())
		{
			return result + "?";
		}

		var sets = Enumerable.Range(0, games[0].GetArrayLength()).Select(i =>
		{
			var pair = $"{games[0][i].GetInt32()}-{games[1][i].GetInt32()}";
			return i == games[0].GetArrayLength() - 1 &&
				score.TryGetProperty("is_tiebreak", out var breaker) && breaker.ValueKind == JsonValueKind.True
				? $"[{pair}]"
				: pair;
		});
		result += string.Join(' ', sets);
		if (score.TryGetProperty("points", out var points) && points.ValueKind == JsonValueKind.Array &&
			points.GetArrayLength() == 2 && points[0].ValueKind == JsonValueKind.String &&
			points[1].ValueKind == JsonValueKind.String)
		{
			result += $" ({points[0].GetString()}-{points[1].GetString()})";
		}

		return result;
	}
}
