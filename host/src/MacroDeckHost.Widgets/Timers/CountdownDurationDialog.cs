using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Components;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Model.Events;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Patches;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Runtime;
using MacroDeckHost.Application.Timers;
using MacroDeckHost.Application.Ui.Modals;
using MacroDeckHost.Localization;
using MacroDeckHost.Widgets.Configuration;

namespace MacroDeckHost.Widgets.Timers;

// The entered duration stays on the host, keyed by the dialog: a button's answer only names which button
// was pressed, so a client cannot hand the countdown a value it did not show.
public sealed class CountdownDurationDrafts
{
	private readonly ConcurrentDictionary<Guid, int> _seconds = new();

	internal void Begin(Guid draftId, int seconds) => _seconds[draftId] = seconds;

	internal void Set(Guid draftId, int seconds)
	{
		if (_seconds.ContainsKey(draftId))
		{
			_seconds[draftId] = seconds;
		}
	}

	internal int? Get(Guid draftId) => _seconds.TryGetValue(draftId, out var seconds) ? seconds : null;

	internal void End(Guid draftId) => _seconds.TryRemove(draftId, out _);
}

public sealed class CountdownDurationPrompt : ICountdownDurationPrompt
{
	internal const string StartAnswer = "start";
	internal const string PresetAnswerPrefix = "preset:";
	internal const string WidgetIdKey = "widgetId";
	internal const string DraftIdKey = "draftId";

	internal static readonly IReadOnlyList<int> PresetSeconds = [60, 300, 600, 900, 1800, 3600];

	private readonly IUiInteractionsFactory _interactions;
	private readonly CountdownDurationDrafts _drafts;

	public CountdownDurationPrompt(IUiInteractionsFactory interactions, CountdownDurationDrafts drafts)
	{
		_interactions = interactions;
		_drafts = drafts;
	}

	public async Task<int?> AskAsync(Guid widgetId,
		string originClientId,
		int? initialSeconds,
		CancellationToken cancellationToken)
	{
		var draftId = Guid.NewGuid();
		_drafts.Begin(draftId, initialSeconds ?? TimerWidgetConfig.DefaultDurationSeconds);

		try
		{
			var modal = new ModalDefinition
			{
				ViewId = CountdownWidgetUiProvider.DurationDialogViewId,
				Title = AppStrings.Widgets.Countdown.Dialog.Title(),
				Data = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
				{
					[WidgetIdKey] = JsonSerializer.SerializeToElement(widgetId.ToString()),
					[DraftIdKey] = JsonSerializer.SerializeToElement(draftId.ToString()),
				},
			};

			var result = await _interactions.ForIntegration(CountdownWidgetUiProvider.OwnerId)
				.ShowModalAsync<string>(originClientId, modal, cancellationToken)
				.ConfigureAwait(false);

			return result.Cancelled ? null : Resolve(result.Value, draftId);
		}
		finally
		{
			_drafts.End(draftId);
		}
	}

	private int? Resolve(string? answer, Guid draftId)
	{
		if (answer == StartAnswer)
		{
			return _drafts.Get(draftId) is > 0 and var seconds ? seconds : null;
		}

		if (answer?.StartsWith(PresetAnswerPrefix, StringComparison.Ordinal) == true &&
			int.TryParse(answer.AsSpan(PresetAnswerPrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture,
				out var preset) &&
			PresetSeconds.Contains(preset))
		{
			return preset;
		}

		return null;
	}
}

internal sealed class CountdownDurationDialogSession : IUiSession
{
	private readonly Guid _widgetId;
	private readonly Guid _draftId;
	private readonly TimerWidgetStore _store;
	private readonly CountdownDurationDrafts _drafts;
	private readonly UiState<int> _seconds;
	private readonly UiState<bool> _alreadyRunning;
	private readonly UiView _view;
	private readonly Lock _sync = new();
	private bool _disposed;

	private CountdownDurationDialogSession(UiSurface surface,
		Guid widgetId,
		Guid draftId,
		TimerWidgetStore store,
		CountdownDurationDrafts drafts)
	{
		_widgetId = widgetId;
		_draftId = draftId;
		_store = store;
		_drafts = drafts;
		_seconds = new UiState<int>(drafts.Get(draftId) ?? TimerWidgetConfig.DefaultDurationSeconds);
		_alreadyRunning = new UiState<bool>(IsBusy());
		_view = new UiView(surface, CountdownDurationDialogView.Build(_seconds, _alreadyRunning, Step));
		_view.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
		_view.HandlerFaulted += (_, fault)
			=> Faulted?.Invoke(this, new UiSessionFaultedEventArgs(fault.Exception.Message, fault.Exception));
		_store.Changed += OnTimerChanged;
	}

	public event EventHandler? Changed;

	public event EventHandler<UiSessionFaultedEventArgs>? Faulted;

	public static IUiSession? Open(UiSurface surface, TimerWidgetStore store, CountdownDurationDrafts drafts)
	{
		var data = surface.Attributes.TryGetValue(UiDialogSurfaceAttributes.Data, out var element) ? element : default;

		return Guid.TryParse(WidgetConfigJson.ReadString(data, CountdownDurationPrompt.WidgetIdKey), out var widgetId) &&
			Guid.TryParse(WidgetConfigJson.ReadString(data, CountdownDurationPrompt.DraftIdKey), out var draftId)
				? new CountdownDurationDialogSession(surface, widgetId, draftId, store, drafts)
				: null;
	}

	public UiTree BuildTree() => _view.Tree;

	public IReadOnlyList<UiPatch> DrainPatches() => _view.DrainPatches();

	public void Dispatch(UiEvent uiEvent)
	{
		lock (_sync)
		{
			_view.Dispatch(uiEvent);
		}
	}

	public ValueTask DisposeAsync()
	{
		_store.Changed -= OnTimerChanged;

		lock (_sync)
		{
			_disposed = true;
		}

		_view.Dispose();

		return ValueTask.CompletedTask;
	}

	private void Step(CountdownDurationUnit unit, int direction)
	{
		var span = TimeSpan.FromSeconds(_seconds.Value);
		var (hours, minutes, seconds) = ((int)span.TotalHours, span.Minutes, span.Seconds);

		switch (unit)
		{
			case CountdownDurationUnit.Hours:
				hours = Wrap(hours + direction, 24);
				break;
			case CountdownDurationUnit.Minutes:
				minutes = Wrap(minutes + direction, 60);
				break;
			default:
				seconds = Wrap(seconds + direction * 5, 60);
				break;
		}

		var total = hours * 3600 + minutes * 60 + seconds;
		_seconds.Set(total);
		_drafts.Set(_draftId, total);
	}

	private static int Wrap(int value, int modulo) => ((value % modulo) + modulo) % modulo;

	private bool IsBusy() => _store.Get(_widgetId) is { Phase: not TimerWidgetPhase.Idle };

	private void OnTimerChanged(object? sender, TimerWidgetChangedEventArgs args)
	{
		if (args.WidgetId != _widgetId)
		{
			return;
		}

		lock (_sync)
		{
			// Once started elsewhere this dialog's wait has been cancelled, so it must not offer its buttons again.
			if (_disposed || _alreadyRunning.Value || !IsBusy())
			{
				return;
			}

			using (_view.Batch())
			{
				_alreadyRunning.Set(true);
			}
		}
	}
}

internal enum CountdownDurationUnit
{
	Hours,
	Minutes,
	Seconds,
}

internal static class CountdownDurationDialogView
{
	private static readonly UiSize _gap = UiSize.FromBasis(0.03);
	private static readonly UiSize _textSize = UiSize.FromBasis(0.045);
	private static readonly UiSize _buttonHeight = UiSize.FromBasis(0.11);
	private const string _outlineColor = "#8a8a8a";

	public static UiElement Build(UiState<int> seconds,
		UiState<bool> alreadyRunning,
		Action<CountdownDurationUnit, int> step)
		=> new UiStack
		{
			Key = "countdownDuration",
			Direction = UiComponentDirections.Vertical,
			Padding = UiSize.FromBasis(0.03),
			Gap = _gap,
			Children =
			[
				new UiWhen
				{
					Key = "runningGate",
					Condition = () => alreadyRunning.Value,
					Content = () => new UiTextRun
					{
						Key = "alreadyRunning",
						Text = AppStrings.Widgets.Countdown.Dialog.AlreadyRunning(),
						Size = _textSize,
						Role = UiComponentTextRoles.Secondary,
						Wrap = true,
						MaxLines = 3,
					},
				},
				new UiWhen
				{
					Key = "pickerGate",
					Condition = () => !alreadyRunning.Value,
					Content = () => Picker(seconds, step),
				},
			],
		};

	private static UiStack Picker(UiState<int> seconds, Action<CountdownDurationUnit, int> step)
		=> new()
		{
			Key = "picker",
			Direction = UiComponentDirections.Vertical,
			Gap = _gap,
			Children =
			[
				new UiStack
				{
					Key = "readoutRow",
					MainSize = UiSize.FromBasis(0.16),
					Justify = UiComponentJustify.Center,
					Align = UiComponentAlignments.Stretch,
					Children =
					[
						new UiTextRun
						{
							Key = "readout",
							Text = UiText.From(() => TimerFace.FormatSeconds(seconds.Value)),
							Size = UiSize.FromBasis(0.11),
							MinSize = UiSize.FromBasis(0.07),
							Weight = UiComponentTextWeights.Bold,
							Align = UiComponentAlignments.Center,
						},
					],
				},
				new UiStack
				{
					Key = "steppers",
					Direction = UiComponentDirections.Horizontal,
					MainSize = UiSize.FromBasis(0.31),
					Gap = _gap,
					Children =
					[
						Stepper(CountdownDurationUnit.Hours, AppStrings.Widgets.Countdown.Dialog.Hours(), step),
						Stepper(CountdownDurationUnit.Minutes, AppStrings.Widgets.Countdown.Dialog.Minutes(), step),
						Stepper(CountdownDurationUnit.Seconds, AppStrings.Widgets.Clock.Seconds(), step),
					],
				},
				PresetRow("presetsShort", CountdownDurationPrompt.PresetSeconds.Take(3)),
				PresetRow("presetsLong", CountdownDurationPrompt.PresetSeconds.Skip(3)),
				new UiModifier
				{
					Key = "startState",
					Disabled = UiValue.From(() => seconds.Value <= 0),
					Child = new UiButton
					{
						Key = "start",
						MainSize = _buttonHeight,
						Justify = UiComponentJustify.Center,
						Align = UiComponentAlignments.Center,
						Events = [UiEventHandler.On(UiComponentEvents.Press, () => { })],
						Answer = UiValue.Optional(() => seconds.Value > 0
							? UiValue.Of(CountdownDurationPrompt.StartAnswer)
							: UiValue.None<string>()),
						Children = [Label("startLabel", AppStrings.Widgets.Countdown.Dialog.Start())],
					},
				},
			],
		};

	private static UiStack Stepper(CountdownDurationUnit unit, UiText label, Action<CountdownDurationUnit, int> step)
	{
		var key = unit.ToString().ToLowerInvariant();

		return new UiStack
		{
			Key = key,
			Fill = true,
			Direction = UiComponentDirections.Vertical,
			Align = UiComponentAlignments.Stretch,
			Gap = UiSize.FromBasis(0.015),
			Children =
			[
				StepButton(key + "Up", UiIcons.Plus, () => step(unit, 1)),
				new UiStack
				{
					Key = key + "LabelRow",
					Fill = true,
					Justify = UiComponentJustify.Center,
					Align = UiComponentAlignments.Stretch,
					Children = [Label(key + "Label", label)],
				},
				StepButton(key + "Down", UiIcons.Minus, () => step(unit, -1)),
			],
		};
	}

	private static UiStack PresetRow(string key, IEnumerable<int> seconds)
		=> new()
		{
			Key = key,
			Direction = UiComponentDirections.Horizontal,
			MainSize = _buttonHeight,
			Gap = _gap,
			Children = [.. seconds.Select(Preset)],
		};

	private static UiModifier StepButton(string key, string icon, Action press)
		=> Outlined(key, new UiStack
		{
			Key = key + "Face",
			MainSize = _buttonHeight,
			Justify = UiComponentJustify.Center,
			Align = UiComponentAlignments.Stretch,
			Events = [UiEventHandler.On(UiComponentEvents.Press, press)],
			Children = [new UiIcon { Key = "icon", Fill = true, Icon = icon, Size = UiSize.FromBasis(0.045) }],
		});

	private static UiModifier Preset(int seconds)
	{
		var key = "preset" + seconds.ToString(CultureInfo.InvariantCulture);

		return Outlined(key, new UiStack
		{
			Key = key + "Face",
			Fill = true,
			Justify = UiComponentJustify.Center,
			Align = UiComponentAlignments.Stretch,
			Answer = CountdownDurationPrompt.PresetAnswerPrefix + seconds.ToString(CultureInfo.InvariantCulture),
			Events = [UiEventHandler.On(UiComponentEvents.Press, () => { })],
			Children =
			[
				Label("label",
					seconds >= 3600
						? AppStrings.Widgets.Countdown.Dialog.PresetHours(seconds / 3600)
						: AppStrings.Widgets.Countdown.Dialog.PresetMinutes(seconds / 60)),
			],
		});
	}

	private static UiModifier Outlined(string key, UiElement face)
		=> new()
		{
			Key = key,
			Radius = UiSize.FromBasis(0.02),
			BorderWidth = UiSize.FromBasis(0.004),
			BorderColor = _outlineColor,
			Child = face,
		};

	private static UiTextRun Label(string key, UiText text)
		=> new()
		{
			Key = key,
			Text = text,
			Size = _textSize,
			Weight = UiComponentTextWeights.Medium,
			Align = UiComponentAlignments.Center,
			MaxLines = 1,
		};
}
