using System.Text.Json;
using System.Text.Json.Nodes;
using MacroDeck.Localization;
using MacroDeck.Ui.Config;
using MacroDeck.Ui.Config.Options;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Runtime;
using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.Icons;
using MacroDeckHost.Domain.Widgets;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Widgets.Configuration;

internal static class WidgetIconField
{
	public const string Automatic = "automatic";

	// The whole icon object is bound at once so the client's draft carries the pinned appearance with it:
	// an icon input alone composes only type and reference and would drop the pin on every save.
	public static UiObjectInput Build(string key,
		LocalizedText label,
		Func<WidgetIconReference?> read,
		Action<WidgetIconReference?> write,
		IIconPackCache icons,
		UiValue<bool> disabled = default)
		=> new()
		{
			Key = key,
			Binding = Bind.Custom(() => ToElement(read()), value => write(FromElement(value, read()))),
			Disabled = disabled,
			Children =
			[
				new UiIconReferenceInput
				{
					Key = "reference",
					Label = label,
					Binding = Bind.Custom(() => ToUi(read())!, value => write(Pick(value, read()))),
					Disabled = disabled,
				},
				new UiWhen
				{
					Key = "appearance-when",
					Condition = () => Options(icons, read()).Count > 0,
					Content = () => new UiChoiceInput
					{
						Key = "appearance",
						Label = AppStrings.Widgets.Appearance.Icon.Appearance(),
						Binding = Bind.Custom(() => read()?.Appearance ?? Automatic,
							value => write(read() is { } current
								? current with { Appearance = value == Automatic || string.IsNullOrEmpty(value) ? null : value }
								: null)),
						Options = UiValue.From<IReadOnlyList<UiOption>>(() => Options(icons, read())),
						Disabled = disabled,
					},
				},
			],
		};

	public static WidgetIconReference? Pick(UiIconReference? value, WidgetIconReference? current)
	{
		if (value is null || string.IsNullOrEmpty(value.Reference))
		{
			return null;
		}

		var next = new WidgetIconReference(value.Type, value.Reference);
		return current is { } existing && existing.SameIcon(next) ? existing : next;
	}

	private static UiIconReference? ToUi(WidgetIconReference? reference)
		=> reference is { } value ? new UiIconReference(value.Type, value.Reference) : null;

	private static JsonElement ToElement(WidgetIconReference? reference)
		=> reference is { } value
			? JsonSerializer.SerializeToElement(value.ToJson())
			: JsonSerializer.SerializeToElement<JsonNode?>(null);

	private static WidgetIconReference? FromElement(JsonElement value, WidgetIconReference? current)
	{
		if (value.ValueKind != JsonValueKind.Object ||
			WidgetIconReference.Read(JsonNode.Parse(value.GetRawText()), null) is not { } next)
		{
			return null;
		}

		return next.Appearance is null && current is { } existing && existing.SameIcon(next) ? existing : next;
	}

	private static List<UiOption> Options(IIconPackCache icons, WidgetIconReference? reference)
	{
		if (reference is not { Type: WidgetIconReference.IconPackType } value ||
			!Guid.TryParse(value.Reference, out var iconId) ||
			icons.GetIconById(iconId) is not { AppearanceOfId: null } icon)
		{
			return [];
		}

		var appearances = icons.GetAppearances(icon.Id);
		if (appearances.Count == 0)
		{
			return [];
		}

		var options = new List<UiOption>
		{
			UiOption.Of(Automatic, AppStrings.Widgets.Appearance.Icon.AppearanceAutomatic()),
			UiOption.Of(WidgetIconReference.DefaultAppearance, AppStrings.IconPacks.Appearances.Default()),
		};

		foreach (var appearance in appearances)
		{
			var appearanceKey = IconAppearanceTraits.ToKey(appearance.AppearanceTraits ?? new Dictionary<string, string>());
			options.Add(UiOption.Of(appearanceKey, KindLabel(appearanceKey)));
		}

		return options;
	}

	private static LocalizedText KindLabel(string key)
		=> key switch
		{
			"colorScheme=light" => AppStrings.IconPacks.Appearances.Kind.Light(),
			"colorScheme=dark" => AppStrings.IconPacks.Appearances.Kind.Dark(),
			"motion=static" => AppStrings.IconPacks.Appearances.Kind.Static(),
			"motion=animated" => AppStrings.IconPacks.Appearances.Kind.Animated(),
			"colorScheme=light;motion=static" => AppStrings.IconPacks.Appearances.Kind.LightStatic(),
			"colorScheme=light;motion=animated" => AppStrings.IconPacks.Appearances.Kind.LightAnimated(),
			"colorScheme=dark;motion=static" => AppStrings.IconPacks.Appearances.Kind.DarkStatic(),
			"colorScheme=dark;motion=animated" => AppStrings.IconPacks.Appearances.Kind.DarkAnimated(),
			_ => AppStrings.IconPacks.Appearances.Kind.Custom(key)
		};
}
