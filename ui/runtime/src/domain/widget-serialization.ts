import {
  WidgetType,
  WidgetData,
  ActionButtonData,
  ButtonState,
  ButtonStateDefinition,
  MusicPlayerData,
  SliderData,
  WeatherData,
  HistoryGraphData,
  ClockData,
} from './widget.interface';
import { DEFAULT_OFF_STATE_ID, DEFAULT_ON_STATE_ID, createStateId, normalizeStates } from './button-state.util';
import { readWidgetIconRef } from './widget-icon-ref.util';

const LEGACY_FONT_KEYS = ['fontFamily', 'fontBold', 'fontItalic', 'labelFont'] as const;

function migrateIconField(target: { icon?: unknown; iconId?: string }): void {
  const resolved = readWidgetIconRef(target.icon, target.iconId);
  delete target.iconId;
  if (resolved) {
    target.icon = resolved;
  } else {
    delete target.icon;
  }
}

function stripLegacyFontKeys<T extends object | undefined>(state: T): T {
  if (!state) {
    return state;
  }
  const clone: Record<string, unknown> = { ...state };
  for (const key of LEGACY_FONT_KEYS) {
    delete clone[key];
  }
  return clone as T;
}

export function parseWidgetData(type: WidgetType, json: string | undefined): WidgetData {
  if (!json) {
    return defaultWidgetData(type);
  }

  try {
    const parsed = JSON.parse(json);
    switch (type) {
      case WidgetType.ActionButton: {
        const buttonData: ActionButtonData = {
          ...parsed,
          label: parsed.label,
          imageUrl: parsed.imageUrl ?? parsed.imageId,
          // Absence already means the reader's accent (the host maps the old `var(--color-accent)`
          // sentinel to absent too) - a new save must not keep re-stamping the literal string.
          backgroundColor: parsed.backgroundColor,
          labelColor: parsed.labelColor,
        };

        // `offState`/`onState` are pure duplicates `serializeWidgetData` used to regenerate on every
        // save; `imageId` is a duplicate of `imageUrl`. The JSON editor (#168) showed all of them
        // alongside the real fields, and editing `offState` there was silently discarded because this
        // parser preferred `states` - the edit appeared to save and then vanished. Seed a temporary
        // off/on pair from the aliases only when `states` is not already the legacy shape (so a bag
        // carrying both never lets a stale alias overwrite the current data), fold `imageId` into
        // `imageUrl`, and strip all three keys so neither the editor nor a future save can
        // resurrect them.
        let legacyPair: { off?: ButtonState; on?: ButtonState } | undefined =
          parsed.states && !Array.isArray(parsed.states) ? parsed.states : undefined;
        if (!legacyPair && (parsed.offState || parsed.onState)) {
          legacyPair = {
            off: parsed.offState ?? {
              backgroundColor: parsed.backgroundColor,
              label: parsed.label
            },
            on: parsed.onState ?? {
              backgroundColor: '#ef4444',
              label: parsed.label
            }
          };
        }
        delete (buttonData as Record<string, unknown>)['offState'];
        delete (buttonData as Record<string, unknown>)['onState'];
        delete (buttonData as Record<string, unknown>)['imageId'];

        // Legacy per-face fields (issue #457): the host now resolves the label font by a single
        // fontFaceId, at root and per state. Tolerated on read (an old client's saved payload) and
        // dropped rather than mapped - the client has no font catalog to resolve them against, so
        // that migration is the host's job (WidgetFontFaceMigration), not this parser's.
        for (const key of LEGACY_FONT_KEYS) {
          delete (buttonData as Record<string, unknown>)[key];
        }
        if (legacyPair) {
          legacyPair = { off: stripLegacyFontKeys(legacyPair.off), on: stripLegacyFontKeys(legacyPair.on) };
        }
        if (Array.isArray(buttonData.states)) {
          buttonData.states = buttonData.states.map(state =>
            state && typeof state === 'object'
              ? { ...state, appearance: stripLegacyFontKeys((state as ButtonStateDefinition).appearance) }
              : state);
        }

        // The ids are literally `off`/`on`: it makes the upgrade an identity mapping and keeps
        // saved `Set appearance` flow parameters (`state: "off"`) resolving.
        if (!Array.isArray(buttonData.states) && legacyPair) {
          buttonData.states = [
            { id: DEFAULT_OFF_STATE_ID, label: 'Off', appearance: legacyPair.off ?? {} },
            { id: DEFAULT_ON_STATE_ID, label: 'On', appearance: legacyPair.on ?? {} },
          ];
        }

        buttonData.stateMode = parsed.stateMode ?? (parsed.mode === 'toggle');

        // A legacy toggle button that customized neither face stored no `states` at all, so the
        // alias fold above produced nothing to seed from. It still has to end up with the default
        // pair, or it reads as state-mode-enabled with no states - which the host does upgrade, and
        // the two must not disagree on the same JSON.
        if (buttonData.stateMode && !Array.isArray(buttonData.states)) {
          buttonData.states = [
            { id: DEFAULT_OFF_STATE_ID, label: 'Off', appearance: {} },
            { id: DEFAULT_ON_STATE_ID, label: 'On', appearance: {} },
          ];
        }

        // Every legacy bare `iconId` (root and per-state) becomes the typed `icon` reference before
        // the state-mode relocation below runs, so that logic only ever has to know about one shape -
        // and an already-migrated payload (an `icon` object with no `iconId` at all) relocates exactly
        // the same way a freshly-migrated one does (issue #425).
        for (const state of buttonData.states ?? []) {
          if (state?.appearance) {
            migrateIconField(state.appearance);
          }
        }
        migrateIconField(buttonData);

        if (buttonData.stateMode) {
          if (buttonData.icon !== undefined) {
            if (!buttonData.states || buttonData.states.length === 0) {
              buttonData.states = [
                { id: DEFAULT_OFF_STATE_ID, label: 'Off', appearance: {} },
                { id: DEFAULT_ON_STATE_ID, label: 'On', appearance: {} },
              ];
            }
            const first = buttonData.states[0];
            if (first.appearance?.icon === undefined) {
              first.appearance = { ...first.appearance, icon: buttonData.icon, iconDisplay: buttonData.iconDisplay };
            }
          }
          delete buttonData.icon;
          delete buttonData.iconDisplay;
        } else {
          // states[0]'s icon -> root; the rest of states[0] (and every other state) is dormant
          // state-mode config the user may come back to, so it is preserved untouched. The icon
          // and its framing move as one unit, and only when the root holds neither: a legacy
          // `imageUrl` button stores framing with no id at all, and moving the two independently
          // could end up applying one icon's framing to a different icon.
          const first = buttonData.states?.[0];
          if (buttonData.icon === undefined && buttonData.iconDisplay === undefined) {
            if (first?.appearance?.icon !== undefined) {
              buttonData.icon = first.appearance.icon;
            }
            if (first?.appearance?.iconDisplay !== undefined) {
              buttonData.iconDisplay = first.appearance.iconDisplay;
            }
          }
          if (first?.appearance) {
            delete first.appearance.icon;
            delete first.appearance.iconDisplay;
          }
        }

        if (parsed.isToggled !== undefined) {
          buttonData.activeStateId = parsed.isToggled ? DEFAULT_ON_STATE_ID : DEFAULT_OFF_STATE_ID;
        }

        if (parsed.stateBinding) {
          // Behaviour-exact translation of the old binding: the same condition, true selects
          // `on`, false falls back to `off` - a deliberate maintainer decision that synced
          // buttons keep working.
          buttonData.stateMapping = {
            rules: [{ id: createStateId(), stateId: DEFAULT_ON_STATE_ID, when: parsed.stateBinding }],
            fallbackStateId: DEFAULT_OFF_STATE_ID,
          };
        }

        delete (buttonData as Record<string, unknown>)['mode'];
        delete (buttonData as Record<string, unknown>)['isToggled'];
        delete (buttonData as Record<string, unknown>)['stateBinding'];

        // A dangling `fallbackStateId`/`rule.stateId`/`stateProvider.blockId` is resolved at
        // render time and shown as invalid in the editor - silently rewriting hand-edited JSON
        // would be worse than showing it as broken.
        if (buttonData.states !== undefined) {
          buttonData.states = normalizeStates(buttonData.states);
        }

        if (parsed.flows) {
          try {
            buttonData.flows = typeof parsed.flows === 'string'
              ? JSON.parse(parsed.flows)
              : parsed.flows;
          } catch (e) {
            console.warn('Failed to parse flows data:', e);
            buttonData.flows = [];
          }
        }

        return buttonData;
      }

      case WidgetType.MusicPlayer: {
        const musicData: MusicPlayerData = {
          ...parsed,
          instanceId: parsed.instanceId,
          coverStyle: parsed.coverStyle ?? 'small',
          showHeader: parsed.showHeader ?? true,
          showTitle: parsed.showTitle ?? true,
          showArtist: parsed.showArtist ?? true,
          showAlbum: parsed.showAlbum ?? true,
          showTimeline: parsed.showTimeline ?? true,
          border: parsed.border
        };

        if (parsed.flows) {
          try {
            musicData.flows = typeof parsed.flows === 'string'
              ? JSON.parse(parsed.flows)
              : parsed.flows;
          } catch (e) {
            console.warn('Failed to parse flows data:', e);
            musicData.flows = [];
          }
        }

        return musicData;
      }

      case WidgetType.Slider: {
        const sliderData: SliderData = {
          ...parsed,
          label: parsed.label,
          orientation: parsed.orientation ?? 'horizontal',
          color: parsed.color,
          labelColor: parsed.labelColor,
          backgroundColor: parsed.backgroundColor,
          showLabel: parsed.showLabel,
          showValue: parsed.showValue,
          border: parsed.border,
          valueVariable: parsed.valueVariable,
          min: parsed.min,
          max: parsed.max,
          step: parsed.step
        };
        migrateIconField(sliderData);
        return sliderData;
      }

      case WidgetType.Weather: {
        const weatherData: WeatherData = {
          ...parsed,
          instanceId: parsed.instanceId,
          showIcon: parsed.showIcon ?? true,
          showTemperature: parsed.showTemperature ?? true,
          showCondition: parsed.showCondition ?? true,
          showForecast: parsed.showForecast ?? true,
          forecastDays: parsed.forecastDays ?? 5,
          border: parsed.border
        };

        if (parsed.flows) {
          try {
            weatherData.flows = typeof parsed.flows === 'string'
              ? JSON.parse(parsed.flows)
              : parsed.flows;
          } catch (e) {
            console.warn('Failed to parse flows data:', e);
            weatherData.flows = [];
          }
        }

        return weatherData;
      }

      case WidgetType.HistoryGraph: {
        const historyGraphData: HistoryGraphData = {
          ...parsed,
          valueVariable: parsed.valueVariable ?? 'system_cpu_usage_percent',
          title: parsed.title,
          subtitleVariable: parsed.subtitleVariable,
          subtitle: parsed.subtitle,
          showSubtitle: parsed.showSubtitle ?? true,
          accentColor: parsed.accentColor,
          maxValue: parsed.maxValue,
          minValue: parsed.minValue,
          historyLength: parsed.historyLength,
          border: parsed.border
        };

        if (parsed.flows) {
          try {
            historyGraphData.flows = typeof parsed.flows === 'string'
              ? JSON.parse(parsed.flows)
              : parsed.flows;
          } catch (e) {
            console.warn('Failed to parse flows data:', e);
            historyGraphData.flows = [];
          }
        }

        return historyGraphData;
      }

      case WidgetType.Clock: {
        const clockData: ClockData = {
          ...parsed,
          style: parsed.style ?? 'digital',
          timeZone: parsed.timeZone,
          showLabel: parsed.showLabel,
          label: parsed.label,
          showSeconds: parsed.showSeconds ?? true,
          showDate: parsed.showDate ?? true,
          border: parsed.border
        };

        if (parsed.flows) {
          try {
            clockData.flows = typeof parsed.flows === 'string'
              ? JSON.parse(parsed.flows)
              : parsed.flows;
          } catch (e) {
            console.warn('Failed to parse flows data:', e);
            clockData.flows = [];
          }
        }

        return clockData;
      }

      default:
        // An unregistered widget type - most often a plugin's - has no known shape to migrate or
        // default here, so the parsed object crosses verbatim rather than as a fabricated Action
        // Button bag; the schema that gave it meaning lives with whatever registered the type.
        return parsed as WidgetData;
    }
  } catch {
    return defaultWidgetData(type);
  }
}

export function defaultWidgetData(type: WidgetType): WidgetData {
  switch (type) {
    case WidgetType.ActionButton:
      // No backgroundColor: absence already means the reader's accent colour.
      return { label: '' } as ActionButtonData;
    case WidgetType.MusicPlayer:
      return {
        coverStyle: 'small',
        showHeader: true,
        showTitle: true,
        showArtist: true,
        showAlbum: true,
        showTimeline: true
      } as MusicPlayerData;
    case WidgetType.Slider:
      // No label until a variable is picked, which then supplies one - a slider labelled "Value"
      // before it controls anything says nothing the user did not already know.
      return { orientation: 'horizontal', label: '' } as SliderData;
    case WidgetType.Weather:
      return {
        showIcon: true,
        showTemperature: true,
        showCondition: true,
        showForecast: true,
        forecastDays: 5
      } as WeatherData;
    case WidgetType.HistoryGraph:
      return {
        valueVariable: 'system_cpu_usage_percent',
        title: 'CPU Load',
        subtitle: '{{ vars.system_cpu_name }}',
        showSubtitle: true,
        maxValue: 100
      } as HistoryGraphData;
    case WidgetType.Clock:
      return {
        style: 'digital',
        showSeconds: true,
        showDate: true
      } as ClockData;
    default:
      // An unregistered type has no built-in defaults to offer - an empty bag, not an Action
      // Button's fields, which its own configuration tree (or JSON editor) fills in from there.
      return {} as WidgetData;
  }
}

export function serializeWidgetData(type: WidgetType, data: WidgetData): string {
  switch (type) {
    case WidgetType.ActionButton: {
      const d = data as ActionButtonData;
      const payload: any = {
        ...d,
        label: d.label,
        imageUrl: d.imageUrl,
        backgroundColor: d.backgroundColor,
        labelColor: d.labelColor,
        flows: JSON.stringify(d.flows ?? [])
      };
      delete payload.offState;
      delete payload.onState;
      delete payload.imageId;
      delete payload.mode;
      delete payload.isToggled;
      delete payload.stateBinding;
      for (const key of LEGACY_FONT_KEYS) {
        delete payload[key];
      }
      if (Array.isArray(payload.states)) {
        payload.states = payload.states.map((state: ButtonStateDefinition) => {
          if (!state || typeof state !== 'object') {
            return state;
          }
          const appearance = stripLegacyFontKeys(state.appearance) as ButtonState | undefined;
          if (appearance) {
            migrateIconField(appearance);
          }
          return { ...state, appearance };
        });
      }
      // Belt and suspenders alongside `parseWidgetData`'s own migration (issue #425): writing always
      // emits `icon` and removes `iconId`, even for data that reached this call without having been
      // parsed first.
      migrateIconField(payload);

      return JSON.stringify(payload);
    }
    case WidgetType.MusicPlayer: {
      const d = data as MusicPlayerData;
      return JSON.stringify({
        ...d,
        instanceId: d.instanceId,
        coverStyle: d.coverStyle,
        showHeader: d.showHeader,
        showTitle: d.showTitle,
        showArtist: d.showArtist,
        showAlbum: d.showAlbum,
        showTimeline: d.showTimeline,
        border: d.border,
        flows: JSON.stringify(d.flows ?? [])
      });
    }
    case WidgetType.Slider: {
      const d = data as SliderData;
      const payload: any = {
        ...d,
        label: d.label,
        orientation: d.orientation,
        color: d.color,
        labelColor: d.labelColor,
        backgroundColor: d.backgroundColor,
        showLabel: d.showLabel,
        showValue: d.showValue,
        border: d.border,
        valueVariable: d.valueVariable,
        min: d.min,
        max: d.max,
        step: d.step
      };
      migrateIconField(payload);
      return JSON.stringify(payload);
    }
    case WidgetType.Weather: {
      const d = data as WeatherData;
      return JSON.stringify({
        ...d,
        instanceId: d.instanceId,
        showIcon: d.showIcon,
        showTemperature: d.showTemperature,
        showCondition: d.showCondition,
        showForecast: d.showForecast,
        forecastDays: d.forecastDays,
        border: d.border,
        flows: JSON.stringify(d.flows ?? [])
      });
    }
    case WidgetType.HistoryGraph: {
      const d = data as HistoryGraphData;
      return JSON.stringify({
        ...d,
        valueVariable: d.valueVariable,
        title: d.title,
        subtitleVariable: d.subtitleVariable,
        subtitle: d.subtitle,
        showSubtitle: d.showSubtitle,
        accentColor: d.accentColor,
        maxValue: d.maxValue,
        minValue: d.minValue,
        historyLength: d.historyLength,
        border: d.border,
        flows: JSON.stringify(d.flows ?? [])
      });
    }
    case WidgetType.Clock: {
      const d = data as ClockData;
      return JSON.stringify({
        ...d,
        style: d.style,
        timeZone: d.timeZone,
        showLabel: d.showLabel,
        label: d.label,
        showSeconds: d.showSeconds,
        showDate: d.showDate,
        border: d.border,
        flows: JSON.stringify(d.flows ?? [])
      });
    }
    default:
      // Nothing here knows an unregistered type's fields, so it writes back exactly what it was
      // given - the only way its configuration tree's edits (composed by ui-config/config-draft)
      // survive a save instead of vanishing into '{}'.
      return JSON.stringify(data);
  }
}

export function toEditorJson(type: WidgetType, data: WidgetData): string {
  const bag: any = JSON.parse(serializeWidgetData(type, data));
  if (typeof bag.flows === 'string') {
    try {
      bag.flows = JSON.parse(bag.flows);
    } catch {
    }
  }

  if (Array.isArray(bag.flows) && bag.flows.length === 0) {
    delete bag.flows;
  }

  if (Array.isArray(bag.states) && bag.states.length === 0) {
    delete bag.states;
  }

  return JSON.stringify(bag, null, 2);
}

export function fromEditorJson(type: WidgetType, text: string): WidgetData {
  const bag: any = JSON.parse(text);
  if (Array.isArray(bag.flows)) {
    bag.flows = JSON.stringify(bag.flows);
  }
  return parseWidgetData(type, JSON.stringify(bag));
}
