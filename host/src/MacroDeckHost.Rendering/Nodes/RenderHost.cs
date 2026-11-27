using System.Text.Json;
using MacroDeckHost.Rendering.Dom;
using MacroDeckHost.Rendering.Tree;

namespace MacroDeckHost.Rendering.Nodes;

internal sealed record TextFitEntry(Element Element, double? Declared, double? Minimum, int MaxLines);

internal sealed class RenderHost
{
	private readonly TileRenderInput _input;

	public RenderHost(TileRenderInput input, IReadOnlyDictionary<string, int>? versionCaps = null)
	{
		_input = input;
		VersionCaps = versionCaps;
	}

	public DateTimeOffset Now => _input.Now;

	public double NowMs => _input.Now.ToUnixTimeMilliseconds();

	public string Culture => _input.Culture;

	public TimeZoneInfo TimeZone => _input.TimeZone;

	public bool OwnsRootBorder => _input.OwnsRootBorder;

	public bool AnimatedImages => _input.AnimatedImages;

	public List<TextFitEntry> Fits { get; } = [];

	public List<Element> FirstFits { get; } = [];

	public List<Element> AnchoredLists { get; } = [];

	public List<Element> Lists { get; } = [];

	public List<Element> ContentSizedButtons { get; } = [];

	public HashSet<string> MissingResources { get; } = new(StringComparer.Ordinal);

	public List<long> TickPeriodsMs { get; } = [];

	public bool HasRing { get; set; }

	public IReadOnlyDictionary<string, int>? VersionCaps { get; }

	public void Forget(Element subtree)
	{
		var removed = new HashSet<Element>(subtree.Descendants()) { subtree };
		Fits.RemoveAll(fit => removed.Contains(fit.Element));
		FirstFits.RemoveAll(removed.Contains);
		AnchoredLists.RemoveAll(removed.Contains);
		Lists.RemoveAll(removed.Contains);
		ContentSizedButtons.RemoveAll(removed.Contains);
	}

	public string Translate(string scope, string key, IReadOnlyDictionary<string, string>? arguments)
	{
		return _input.Localization?.Translate(scope, key, arguments) ?? $"[[{scope}:{key}]]";
	}

	public string? Text(UiNode node, string key)
	{
		return Text(node.Raw(key));
	}

	public string? Text(JsonElement? raw)
	{
		if (raw is not { } value)
		{
			return null;
		}

		if (value.ValueKind == JsonValueKind.String)
		{
			return value.GetString();
		}

		if (value.ValueKind != JsonValueKind.Object
			|| !value.TryGetProperty("$localized", out var reference)
			|| reference.ValueKind != JsonValueKind.Object)
		{
			return null;
		}

		if (!reference.TryGetProperty("scope", out var scope)
			|| scope.ValueKind != JsonValueKind.String
			|| !reference.TryGetProperty("key", out var key)
			|| key.ValueKind != JsonValueKind.String)
		{
			return null;
		}

		Dictionary<string, string>? arguments = null;
		if (reference.TryGetProperty("arguments", out var values) && values.ValueKind == JsonValueKind.Object)
		{
			arguments = [];
			foreach (var argument in values.EnumerateObject())
			{
				arguments[argument.Name] = argument.Value.ValueKind switch
				{
					JsonValueKind.String => argument.Value.GetString() ?? "",
					JsonValueKind.True => "true",
					JsonValueKind.False => "false",
					JsonValueKind.Number => FormatNumber(argument.Value.GetDouble()),
					_ => Text(argument.Value) ?? argument.Value.GetRawText()
				};
			}
		}

		return Translate(scope.GetString() ?? "", key.GetString() ?? "", arguments);
	}

	public RenderResource? Resource(JsonElement? raw, double? displayPx)
	{
		if (raw is not { ValueKind: JsonValueKind.Object } value
			|| !value.TryGetProperty("resourceId", out var id)
			|| id.ValueKind != JsonValueKind.String
			|| string.IsNullOrEmpty(id.GetString()))
		{
			return null;
		}

		var resourceId = id.GetString()!;
		var size = displayPx is { } px ? (int)Math.Ceiling(px * _input.Scale) : (int?)null;
		if (_input.Resources?.TryGet(resourceId, size) is { } found)
		{
			return found;
		}

		MissingResources.Add(resourceId);
		return null;
	}

	// JavaScript's String(number), which is how the runtime writes a numeric argument.
	public static string FormatNumber(double value)
	{
		if (Math.Abs(value % 1) < double.Epsilon && Math.Abs(value) < 1e21)
		{
			return ((long)value).ToString(System.Globalization.CultureInfo.InvariantCulture);
		}

		return value.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
	}
}
