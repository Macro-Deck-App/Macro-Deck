import { UiNode } from '../ui-framework/ui-node.interface';
import { UiMacroDeckComponents } from './macrodeck-component-types';
import { nodeNumber, nodeString } from '../ui-framework/node-properties.util';
import { nodeLength, resolveLength } from '../ui-framework/length';
import { UiComponentProperties } from '../ui-components/component-properties';
import { showsSeconds, textAlign, textFillColor, textFontWeight } from '../ui-components/style';
import { textIntrinsicMainPx } from '../ui-components/text-paint';
import {
  formatDate,
  formatDateLong,
  formatDateOrdered,
  formatTimeRun,
  formatZoneName,
  formatZoneOffset,
  nodeTimeRef,
  UiDateOrder,
  UiHourStyle,
  UiTimeFormats,
} from './time';
import type { UiComponentContext, UiComponentDefinition } from '../ui-framework/component-registry';
import { textFit } from '../render/text-fit';
import { px } from '../ui-components/px.util';
import { MINUTE_MS, SECOND_MS } from '../ui-components/render-constants';

interface TimeRun {
  before: string;
  seconds: string;
  after: string;
}

const HOUR_STYLES: Readonly<Record<string, UiHourStyle>> = {
  [UiTimeFormats.Time12Hour]: { cycle: 'h12', padded: false },
  [UiTimeFormats.Time12HourPadded]: { cycle: 'h12', padded: true },
  [UiTimeFormats.Time24Hour]: { cycle: 'h23', padded: true },
  [UiTimeFormats.Time24HourUnpadded]: { cycle: 'h23', padded: false },
};

const DATE_ORDERS: Readonly<Record<string, UiDateOrder>> = {
  [UiTimeFormats.DateDayFirst]: 'day-first',
  [UiTimeFormats.DateMonthFirst]: 'month-first',
  [UiTimeFormats.DateIso]: 'iso',
};

function timeRun(node: UiNode, ctx: UiComponentContext<UiDynamicTextState>): TimeRun {
  const reference = nodeTimeRef(node, UiComponentProperties.Value);
  if (!reference) return { before: '', seconds: '', after: '' };

  const locale = ctx.host.culture();
  const instant = new Date(ctx.host.now());
  const format = nodeString(node, UiComponentProperties.Format) ?? '';
  const plain = (before: string): TimeRun => ({ before, seconds: '', after: '' });

  if (format === UiTimeFormats.Time) {
    return formatTimeRun(instant, reference.zone, showsSeconds(node), locale, undefined, ctx.host.hourCycle?.());
  }
  if (format in HOUR_STYLES) {
    return formatTimeRun(instant, reference.zone, showsSeconds(node), locale, HOUR_STYLES[format]);
  }
  if (format in DATE_ORDERS) {
    return plain(formatDateOrdered(instant, reference.zone, locale, DATE_ORDERS[format]));
  }
  switch (format) {
    case UiTimeFormats.Date:
      return plain(formatDate(instant, reference.zone, locale));
    case UiTimeFormats.DateLong:
      return plain(formatDateLong(instant, reference.zone, locale));
    case UiTimeFormats.ZoneName:
      return plain(formatZoneName(reference.zone));
    case UiTimeFormats.ZoneOffset:
      return plain(formatZoneOffset(instant, reference.zone));
    default:
      return { before: '', seconds: '', after: '' };
  }
}

export interface UiDynamicTextState {
  secondsBefore: Text | null;
  secondsAfter: Text | null;
}

export const macrodeckDynamicTextComponent: UiComponentDefinition<UiDynamicTextState> = {
  type: UiMacroDeckComponents.DynamicText,

  // 2 is the producer-pinned formats - the 12/24-hour faces, the ordered dates and the UTC offset.
  // They have to be negotiated rather than simply ignored: an unknown *format* is not caught anywhere,
  // so a reader that only knows version 1 would draw an empty run where a clock belongs. A producer
  // using one asks for 2 and supplies a version 1 fallback, and this range is what answers that ask.
  version: { minimum: 1, maximum: 2 },

  create(doc: Document) {
    return doc.createElement('span');
  },

  createState(): UiDynamicTextState {
    return { secondsBefore: null, secondsAfter: null };
  },

  paint(node, ctx) {
    const element = ctx.element as HTMLElement;
    const state = ctx.state;

    ctx.setClassName(element, 'widget-text widget-dynamic-text');
    ctx.setStyle(element, 'width', px(ctx.box.width));
    ctx.setStyle(element, 'font-weight', String(textFontWeight(node)));
    ctx.setStyle(element, 'color', textFillColor(node));
    ctx.setStyle(element, 'text-align', textAlign(node));
    ctx.setStyle(element, 'white-space', 'nowrap');
    ctx.setStyle(element, 'text-overflow', 'ellipsis');

    if (state.secondsBefore === null) {
      const doc = element.ownerDocument;
      state.secondsBefore = doc.createTextNode('');
      state.secondsAfter = doc.createTextNode('');
      // `element` has no other content at this point, so the seconds span - the only part this node
      // ever registers - lands as its sole child; the text nodes are then threaded in on either side of
      // it, which is the one order this node keeps them in from here on.
      const span = ctx.part('seconds', 'span');
      ctx.setClassName(span, 'widget-dynamic-text-seconds');
      element.insertBefore(state.secondsBefore, span);
      element.appendChild(state.secondsAfter);
    }

    const run = timeRun(node, ctx);
    const seconds = ctx.part('seconds', 'span') as HTMLElement;
    if (state.secondsBefore!.nodeValue !== run.before) state.secondsBefore!.nodeValue = run.before;
    if (state.secondsAfter!.nodeValue !== run.after) state.secondsAfter!.nodeValue = run.after;
    if (seconds.textContent !== run.seconds) seconds.textContent = run.seconds;
    ctx.setStyle(seconds, 'color', 'var(--color-text-muted)');

    const declared = resolveLength(nodeLength(node, UiComponentProperties.Size), ctx.basis, ctx.crossExtent);
    const minSize = resolveLength(nodeLength(node, UiComponentProperties.MinSize), ctx.basis, ctx.crossExtent);
    ctx.keepFit(element, textFit(element, declared, minSize, size => {
      ctx.setStyle(seconds, 'font-size', size === undefined ? null : px(size * 0.55));
    }), `${declared}|${minSize}|${run.before}${run.seconds}${run.after}|${ctx.host.uiFontKey?.() ?? ''}`);
  },

  intrinsicMainPx: textIntrinsicMainPx,

  tickPeriodMs(node) {
    if (!nodeTimeRef(node, UiComponentProperties.Value)) return null;

    const format = nodeString(node, UiComponentProperties.Format) ?? '';
    if (format === UiTimeFormats.Time || format in HOUR_STYLES) {
      return showsSeconds(node) ? SECOND_MS : MINUTE_MS;
    }
    switch (format) {
      case UiTimeFormats.Date:
      case UiTimeFormats.DateLong:
      case UiTimeFormats.DateDayFirst:
      case UiTimeFormats.DateMonthFirst:
      case UiTimeFormats.DateIso:
      // A zone's offset is fixed until it isn't: a daylight-saving transition moves it, and the run has
      // to follow. Checking once a minute is what a date already costs.
      case UiTimeFormats.ZoneOffset:
        return MINUTE_MS;
      default:
        return null;
    }
  },
};
