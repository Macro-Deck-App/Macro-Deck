using System.Text.Json;
using System.Text.RegularExpressions;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Components;
using MacroDeck.Ui.Model.Events;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Patches;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Runtime;

namespace MacroDeckHost.Integrations.Steam.Widgets;

internal static partial class SteamSurfaces
{
	public const int DefaultCornerRadius = 22;

	private const int BreathingRoom = 12;

	private const string Transparent = "transparent";

	private static readonly double _cornerClearance = 1 - 1 / Math.Sqrt(2);

	private static readonly JsonElement _emptyObject = JsonDocument.Parse("{}").RootElement;

	public static bool IsConfigFor(UiSurface surface, string widgetType)
	{
		return ReadAttribute(surface, UiConfigSurfaceAttributes.EntryPoint) == UiConfigEntryPoints.WidgetConfig
			&& ReadAttribute(surface, UiConfigSurfaceAttributes.WidgetType) == widgetType;
	}

	public static JsonElement ConfigData(UiSurface surface)
	{
		return surface.Attributes.TryGetValue(UiConfigSurfaceAttributes.WidgetData, out var data)
			&& data.ValueKind == JsonValueKind.Object
				? data
				: _emptyObject;
	}

	public static JsonElement WidgetData(UiSurface surface)
	{
		return surface.Attributes.TryGetValue(UiWidgetSurfaceAttributes.Data, out var data)
			&& data.ValueKind == JsonValueKind.Object
				? data
				: _emptyObject;
	}

	public static string? WidgetType(UiSurface surface)
	{
		return ReadAttribute(surface, UiWidgetSurfaceAttributes.WidgetType);
	}

	public static bool IsSampleRequested(UiSurface surface)
	{
		return surface.Kind == UiSurfaceKinds.Preview
			&& surface.Attributes.TryGetValue(UiWidgetSurfaceAttributes.Sample, out var sample)
			&& sample.ValueKind == JsonValueKind.True;
	}

	public static int CornerRadius(UiSurface surface)
	{
		return surface.Attributes.TryGetValue(UiWidgetSurfaceAttributes.CornerRadius, out var value)
			&& value.ValueKind == JsonValueKind.Number
			&& value.TryGetInt32(out var radius)
				? radius
				: DefaultCornerRadius;
	}

	public static UiSize SafeArea(int cornerRadius)
	{
		var inset = Math.Max(BreathingRoom, _cornerClearance * Math.Max(0, cornerRadius));

		return UiSize.Of(UiLength.Capped(inset / UiLength.Cell, inset));
	}

	public static string? ReadString(JsonElement data, string name)
	{
		return data.ValueKind == JsonValueKind.Object
			&& data.TryGetProperty(name, out var value)
			&& value.ValueKind == JsonValueKind.String
				? value.GetString()
				: null;
	}

	public static IReadOnlyList<string> ReadStrings(JsonElement data, string name)
	{
		return data.ValueKind == JsonValueKind.Object
			&& data.TryGetProperty(name, out var value)
			&& value.ValueKind == JsonValueKind.Array
				?
				[
					.. value.EnumerateArray()
						.Where(item => item.ValueKind == JsonValueKind.String)
						.Select(item => item.GetString()!)
				]
				: [];
	}

	public static bool? ReadBool(JsonElement data, string name)
	{
		return data.ValueKind == JsonValueKind.Object
			&& data.TryGetProperty(name, out var value)
			&& value.ValueKind is JsonValueKind.True or JsonValueKind.False
				? value.GetBoolean()
				: null;
	}

	public static double? ReadDouble(JsonElement data, string name)
	{
		return data.ValueKind == JsonValueKind.Object
			&& data.TryGetProperty(name, out var value)
			&& value.ValueKind == JsonValueKind.Number
			&& value.TryGetDouble(out var number)
			&& double.IsFinite(number)
				? number
				: null;
	}

	public static string? NormalizeBackground(string? raw)
	{
		var value = raw?.Trim();
		if (string.Equals(value, Transparent, StringComparison.OrdinalIgnoreCase))
		{
			return Transparent;
		}

		return value is not null && HexColor().IsMatch(value) ? value.ToLowerInvariant() : null;
	}

	private static string? ReadAttribute(UiSurface surface, string name)
	{
		return surface.Attributes.TryGetValue(name, out var element) && element.ValueKind == JsonValueKind.String
			? element.GetString()
			: null;
	}

	[GeneratedRegex("^#[0-9a-fA-F]{6}$", RegexOptions.CultureInvariant)]
	private static partial Regex HexColor();
}

internal sealed class SteamViewSession : IUiSession
{
	private readonly UiView _view;

	public SteamViewSession(UiView view)
	{
		ArgumentNullException.ThrowIfNull(view);

		_view = view;
		_view.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
		_view.HandlerFaulted += (_, fault)
			=> Faulted?.Invoke(this, new UiSessionFaultedEventArgs(fault.Exception.Message, fault.Exception));
	}

	public event EventHandler? Changed;

	public event EventHandler<UiSessionFaultedEventArgs>? Faulted;

	public UiTree BuildTree()
	{
		return _view.Tree;
	}

	public IReadOnlyList<UiPatch> DrainPatches()
	{
		return _view.DrainPatches();
	}

	public void Dispatch(UiEvent uiEvent)
	{
		_view.Dispatch(uiEvent);
	}

	public ValueTask DisposeAsync()
	{
		return ValueTask.CompletedTask;
	}
}
