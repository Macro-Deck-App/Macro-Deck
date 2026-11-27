using System.Globalization;
using System.Text.Json;
using MacroDeckHost.Rendering.Tree;

namespace MacroDeckHost.Rendering.Nodes;

internal sealed record ProgressReference(double PositionMs, double AnchorMs, double? DurationMs, double Rate);

internal static class Progress
{
	public static ProgressReference? Of(UiNode node, string key)
	{
		if (node.Raw(key) is not { ValueKind: JsonValueKind.Object } raw
			|| !raw.TryGetProperty("$progress", out var marker)
			|| marker.ValueKind != JsonValueKind.Object)
		{
			return null;
		}

		if (!marker.TryGetProperty("positionMs", out var position)
			|| position.ValueKind != JsonValueKind.Number
			|| !marker.TryGetProperty("anchor", out var anchor)
			|| anchor.ValueKind != JsonValueKind.String
			|| !DateTimeOffset.TryParse(anchor.GetString(),
				CultureInfo.InvariantCulture,
				DateTimeStyles.AssumeUniversal,
				out var anchorTime))
		{
			return null;
		}

		double? duration = marker.TryGetProperty("durationMs", out var durationValue)
			&& durationValue.ValueKind == JsonValueKind.Number
			&& durationValue.GetDouble() > 0
				? durationValue.GetDouble()
				: null;
		var rate = marker.TryGetProperty("rate", out var rateValue) && rateValue.ValueKind == JsonValueKind.Number
			? rateValue.GetDouble()
			: 1;
		return new ProgressReference(position.GetDouble(), anchorTime.ToUnixTimeMilliseconds(), duration, rate);
	}

	public static double PositionMs(ProgressReference reference, double nowMs)
	{
		var advanced = reference.PositionMs + (nowMs - reference.AnchorMs) * reference.Rate;
		var floored = Math.Max(0, advanced);
		return reference.DurationMs is { } duration ? Math.Min(duration, floored) : floored;
	}

	public static double Fraction(ProgressReference reference, double nowMs)
	{
		return reference.DurationMs is { } duration
			? Math.Min(1, Math.Max(0, PositionMs(reference, nowMs) / duration))
			: 0;
	}

	public static string Duration(double totalMs)
	{
		var totalSeconds = (long)Math.Max(0, Math.Floor(totalMs / 1000));
		var seconds = totalSeconds % 60;
		var minutes = totalSeconds / 60 % 60;
		var hours = totalSeconds / 3600;
		return hours > 0
			? string.Create(CultureInfo.InvariantCulture, $"{hours}:{minutes:00}:{seconds:00}")
			: string.Create(CultureInfo.InvariantCulture, $"{minutes}:{seconds:00}");
	}

	public static string FormatRun(ProgressReference reference, string? format, double nowMs)
	{
		return format switch
		{
			"elapsed" => Duration(PositionMs(reference, nowMs)),
			"remaining" => reference.DurationMs is { } duration
				? Duration(duration - PositionMs(reference, nowMs))
				: "",
			"duration" => reference.DurationMs is { } total ? Duration(total) : "",
			_ => ""
		};
	}
}
