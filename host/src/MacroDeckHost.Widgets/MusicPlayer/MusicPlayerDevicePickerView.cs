using System.Globalization;
using MacroDeck.Localization;
using MacroDeck.Sdk.MusicPlayer;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Runtime;
using MacroDeck.Ui.Components;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Widgets.MusicPlayer;

/// <summary>What the device picker holds while it is open: what the player answered, and whether it
/// could.</summary>
internal sealed record MusicPlayerDevicePickerState
{
	public required IReadOnlyList<MusicPlayerDevice> Devices { get; init; }

	/// <summary>The player answered. False for unreachable, timed out, or rejected credentials.</summary>
	public required bool Available { get; init; }

	/// <summary>This instance cannot switch devices at all, so retrying will not help.</summary>
	public required bool Supported { get; init; }

	public static MusicPlayerDevicePickerState Loading { get; } =
		new() { Devices = [], Available = true, Supported = true };

	public static MusicPlayerDevicePickerState Unsupported { get; } =
		new() { Devices = [], Available = true, Supported = false };

	public static MusicPlayerDevicePickerState Unavailable { get; } =
		new() { Devices = [], Available = false, Supported = true };
}

/// <summary>
/// The device picker, as a Macro Deck UI dialog.
///
/// <para>
/// Every row answers with the device's own id and nothing else - see
/// <see cref="UiComponentContainer.Answer" />. The client settles the dialog with it and the host transfers
/// playback to what it named, so the value crossing the wire is one the host issued and can look up.
/// That also takes the transfer's other half - which instance, and whether to start playing - off the
/// wire entirely: the action already knows both, where the picker it replaced had the client echo them
/// back.
/// </para>
///
/// <para>
/// Shorter than the item picker on purpose: a player lists a handful of devices, so there is nothing to
/// search and nothing to window.
/// </para>
/// </summary>
internal static class MusicPlayerDevicePickerView
{
	public static UiElement Build(UiState<MusicPlayerDevicePickerState> state)
	{
		ArgumentNullException.ThrowIfNull(state);

		return new UiStack
		{
			Key = "picker",
			Direction = UiComponentDirections.Vertical,
			Gap = UiSize.FromBasis(0.03),
			Padding = UiSize.FromBasis(0.02),
			Children = [Results(state)],
		};
	}

	private static UiList Results(UiState<MusicPlayerDevicePickerState> state)
		=> new()
		{
			Key = "results",
			Fill = true,
			Gap = UiSize.FromBasis(0.015),
			Children =
			[
				Message("unsupported",
					() => !state.Value.Supported,
					AppStrings.Dialogs.DevicePicker.Unsupported()),
				Message("failed",
					() => state.Value.Supported && !state.Value.Available,
					AppStrings.Dialogs.DevicePicker.LoadFailed()),
				Message("empty",
					() => state.Value.Supported && state.Value.Available && state.Value.Devices.Count == 0,
					AppStrings.Dialogs.DevicePicker.NoDevices()),
				new UiRepeat<MusicPlayerDevice>
				{
					Key = "rows",
					Items = UiValue.From<IReadOnlyList<MusicPlayerDevice>>(() => state.Value.Devices),
					KeySelector = device => device.Id,
					Template = (device, _) => Row(device),
				},
			],
		};

	private static UiWhen Message(string key, Func<bool> when, LocalizedText text)
		=> new()
		{
			Key = $"{key}When",
			Condition = when,
			Content = () => new UiStack
			{
				Key = key,
				Justify = UiComponentJustify.Center,
				Align = UiComponentAlignments.Center,
				Padding = UiSize.FromBasis(0.06),
				Children =
				[
					new UiTextRun
					{
						Key = "text",
						Text = text,
						Size = UiSize.FromBasis(0.04),
						Role = UiComponentTextRoles.Secondary,
						Align = UiComponentAlignments.Center,
						Wrap = true,
					},
				],
			},
		};

	private static UiStack Row(MusicPlayerDevice device)
		// A stack rather than a button, for the reason the item picker's rows are: the affordance follows
		// the declared events, and a button's absent background is the accent colour.
		=> new()
		{
			Key = device.Id,
			Answer = UiValue.Of(device.Id),
			Events = [UiEventHandler.On(UiComponentEvents.Press, Answered)],
			Direction = UiComponentDirections.Horizontal,
			Align = UiComponentAlignments.Center,
			Gap = UiSize.FromBasis(0.025),
			Padding = UiSize.FromBasis(0.02),
			Children =
			[
				new UiStack
				{
					Key = "labels",
					Fill = true,
					Direction = UiComponentDirections.Vertical,
					Justify = UiComponentJustify.Center,
					Gap = UiSize.FromBasis(0.004),
					Children =
					[
						new UiTextRun
						{
							Key = "name",
							Text = device.Name,
							Size = UiSize.FromBasis(0.038),
							Weight = UiComponentTextWeights.Medium,
						},
						new UiWhen
						{
							Key = "typeWhen",
							Condition = () => !string.IsNullOrEmpty(device.Type),
							Content = () => new UiTextRun
							{
								Key = "type",
								Text = device.Type,
								Size = UiSize.FromBasis(0.032),
								Role = UiComponentTextRoles.Secondary,
							},
						},
					],
				},
				new UiWhen
				{
					Key = "activeWhen",
					Condition = () => device.IsActive,
					Content = () => new UiTextRun
					{
						Key = "active",
						Text = AppStrings.Dialogs.DevicePicker.Active(),
						Size = UiSize.FromBasis(0.03),
						Weight = UiComponentTextWeights.Medium,
					},
				},
				new UiWhen
				{
					Key = "volumeWhen",
					// Not a truthiness check: a player reports an absent volume as no value at all, and
					// 0 is a real volume that has to stay visible.
					Condition = () => device.VolumePercent.HasValue,
					Content = () => new UiTextRun
					{
						Key = "volume",
						Text = Volume(device),
						Size = UiSize.FromBasis(0.03),
						Role = UiComponentTextRoles.Secondary,
					},
				},
			],
		};

	private static string Volume(MusicPlayerDevice device)
		=> device.VolumePercent.HasValue
			? device.VolumePercent.Value.ToString(CultureInfo.CurrentCulture) + "%"
			: string.Empty;

	/// <summary>
	/// Declared so the row is pressable, and deliberately empty: the answer settles the dialog on the
	/// client, which never sends the press at all.
	/// </summary>
	private static void Answered()
	{
	}
}
