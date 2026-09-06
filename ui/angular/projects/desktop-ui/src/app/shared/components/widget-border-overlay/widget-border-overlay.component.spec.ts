import { provideZonelessChangeDetection, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';

import { BORDER_ANIMATION_PERIOD_MS, WIDGET_BORDER_WIDTH, WidgetBorder } from '@macro-deck/runtime';
import { ServerClockService } from '../../services';
import { WidgetBorderOverlayComponent } from './widget-border-overlay.component';

class FakeServerClock {
  hostTime = 1_700_000_000_000;

  readonly offset = signal(0);

  now(): number {
    return this.hostTime + this.offset();
  }
}

let clock: FakeServerClock;

function createFixture(border: WidgetBorder | undefined): ComponentFixture<WidgetBorderOverlayComponent> {
  clock = new FakeServerClock();
  TestBed.configureTestingModule({
    providers: [provideZonelessChangeDetection(), { provide: ServerClockService, useValue: clock }],
  });

  return createAnother(border);
}

function createAnother(border: WidgetBorder | undefined): ComponentFixture<WidgetBorderOverlayComponent> {
  const fixture = TestBed.createComponent(WidgetBorderOverlayComponent);
  fixture.componentRef.setInput('border', border);
  fixture.detectChanges();
  return fixture;
}

const ring = (fixture: ComponentFixture<WidgetBorderOverlayComponent>): HTMLElement | null =>
  fixture.nativeElement.querySelector('.ring');

const phaseSeconds = (fixture: ComponentFixture<WidgetBorderOverlayComponent>): number =>
  Number.parseFloat(ring(fixture)!.style.getPropertyValue('--wb-phase'));

const expectedPhase = (hostTime: number): number => -((hostTime % BORDER_ANIMATION_PERIOD_MS) / 1000);

describe('WidgetBorderOverlayComponent', () => {
  it('renders no ring without a border config', () => {
    const fixture = createFixture(undefined);

    expect(fixture.componentInstance.resolved()).toBeNull();
    expect(ring(fixture)).toBeNull();
  });

  it('renders no ring for an explicit off style', () => {
    const fixture = createFixture({ style: 'off', color: '#ff0000' });

    expect(fixture.componentInstance.resolved()).toBeNull();
    expect(ring(fixture)).toBeNull();
  });

  it('renders the ring with the style class and configured color', () => {
    const fixture = createFixture({ style: 'heartbeat', color: '#22c55e' });

    const el = ring(fixture)!;
    expect(el.classList).toContain('wb-heartbeat');
    expect(el.style.getPropertyValue('--wb-color')).toBe('#22c55e');
  });

  it('falls back to the default border color', () => {
    const fixture = createFixture({ style: 'static' });

    const el = ring(fixture)!;
    expect(el.classList).toContain('wb-static');
    expect(el.style.getPropertyValue('--wb-color')).toBe('#ffffff');
  });

  it('renders the colorless styles without needing a color', () => {
    const fixture = createFixture({ style: 'rgb' });

    expect(ring(fixture)!.classList).toContain('wb-rgb');
  });

  it('derives the animation phase from host time, reduced to the shared period', () => {
    const fixture = createFixture({ style: 'rgb' });

    expect(phaseSeconds(fixture)).toBe(expectedPhase(clock.now()));
  });

  it('gives every ring the same phase while host time is the same', () => {
    const first = createFixture({ style: 'rgb' });
    const second = createAnother({ style: 'comet', color: '#22c55e' });

    expect(phaseSeconds(second)).toBe(phaseSeconds(first));
  });

  it('offsets a ring created later by the elapsed host time', () => {
    const first = createFixture({ style: 'rgb' });
    const firstPhase = phaseSeconds(first);
    clock.hostTime += 1500;
    const later = createAnother({ style: 'rgb' });

    expect(phaseSeconds(later) - firstPhase).toBeCloseTo(-1.5, 3);
    // Plain time passing must not touch a ring already running - the delay would
    // shift its animation instead of re-anchoring it.
    expect(phaseSeconds(first)).toBe(firstPhase);
  });

  it('re-anchors the phase when the border style changes', () => {
    const fixture = createFixture({ style: 'rgb' });
    clock.hostTime += 2000;

    fixture.componentRef.setInput('border', { style: 'ants', color: '#22c55e' });
    fixture.detectChanges();

    expect(phaseSeconds(fixture)).toBe(expectedPhase(clock.now()));
  });

  it('re-creates the ring so a re-anchored animation actually restarts', async () => {
    const fixture = createFixture({ style: 'rgb' });
    const before = ring(fixture);

    clock.offset.set(-8000);
    await fixture.whenStable();

    expect(ring(fixture)).not.toBe(before);
  });

  it('re-anchors every ring when the host clock sync lands', async () => {
    const fixture = createFixture({ style: 'rgb' });

    clock.offset.set(-8000);
    await fixture.whenStable();

    expect(phaseSeconds(fixture)).toBe(expectedPhase(clock.now()));
  });

  it('keeps every animation duration a divisor of the shared period', () => {
    const durationsMs = [1000, 1400, 1600, 3200, 4000, 8000, 12_000];

    for (const duration of durationsMs) {
      expect(BORDER_ANIMATION_PERIOD_MS % duration).toBe(0);
    }
  });

  it('renders the ring at a fixed width regardless of deck scale', () => {
    const fixture = createFixture({ style: 'static' });

    expect((fixture.nativeElement as HTMLElement).style.getPropertyValue('--wb-width')).toBe(`${WIDGET_BORDER_WIDTH}px`);
  });
});
