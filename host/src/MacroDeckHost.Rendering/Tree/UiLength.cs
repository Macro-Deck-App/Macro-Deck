using System.Text.Json;
using System.Text.RegularExpressions;

namespace MacroDeckHost.Rendering.Tree;

internal readonly record struct LengthScope(double Basis, double? CrossExtent, double? Container)
{
	public LengthScope WithoutCross()
	{
		return this with { CrossExtent = null };
	}
}

internal sealed partial record UiLength(double Basis, double? OfParent, double? MaxOfCross, double? MaxOfCell)
{
	public const double Cell = 120;

	public static UiLength? From(JsonElement? raw)
	{
		if (raw is not { ValueKind: JsonValueKind.Object } value)
		{
			return null;
		}

		if (!value.TryGetProperty("basis", out var basis) || basis.ValueKind != JsonValueKind.Number)
		{
			return null;
		}

		var ofParent = NumberOf(value, "ofParent");
		return new UiLength(basis.GetDouble(),
			ofParent is >= 0 ? ofParent : null,
			NumberOf(value, "maxOfCross"),
			NumberOf(value, "maxOfCell"));
	}

	public static UiLength? Of(UiNode? node, string key)
	{
		return node is null ? null : From(node.Raw(key));
	}

	public static double? Resolve(UiLength? length, LengthScope scope)
	{
		if (length is null)
		{
			return null;
		}

		var parent = ParentExtent(length, scope);
		var resolved = parent is { } extent ? length.OfParent!.Value * extent : length.Basis * scope.Basis;
		if (length.MaxOfCross is { } cross && scope.CrossExtent is { } crossExtent && double.IsFinite(crossExtent))
		{
			resolved = Math.Min(resolved, cross * crossExtent);
		}

		if (length.MaxOfCell is { } cell)
		{
			resolved = Math.Min(resolved, cell * Cell);
		}

		return resolved;
	}

	public static double Reference(UiLength? length, LengthScope scope)
	{
		return (length is null ? null : ParentExtent(length, scope)) ?? scope.Basis;
	}

	public static double? BoxExtent(double? width, double? height)
	{
		return width is null || height is null ? null : Math.Min(width.Value, height.Value);
	}

	public static string? HexColor(JsonElement? raw)
	{
		return raw is { ValueKind: JsonValueKind.String } value && HexPattern().IsMatch(value.GetString() ?? "")
			? value.GetString()
			: null;
	}

	public static string? HexColor(UiNode? node, string key)
	{
		return node is null ? null : HexColor(node.Raw(key));
	}

	public static string? BackgroundColor(UiNode? node, string key)
	{
		var raw = node?.Raw(key);
		return raw is { ValueKind: JsonValueKind.String } value && value.GetString() == "transparent"
			? "transparent"
			: HexColor(raw);
	}

	private static double? ParentExtent(UiLength length, LengthScope scope)
	{
		return length.OfParent is not null && scope.Container is { } container && double.IsFinite(container)
			? container
			: null;
	}

	private static double? NumberOf(JsonElement value, string name)
	{
		return value.TryGetProperty(name, out var member) && member.ValueKind == JsonValueKind.Number
			? member.GetDouble()
			: null;
	}

	[GeneratedRegex("^#(?:[0-9a-fA-F]{6}|[0-9a-fA-F]{8})$")]
	private static partial Regex HexPattern();
}
