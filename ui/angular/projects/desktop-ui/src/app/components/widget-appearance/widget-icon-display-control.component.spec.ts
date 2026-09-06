import { provideZonelessChangeDetection, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Subject } from 'rxjs';

import { WidgetIconDisplay } from '@macro-deck/runtime';
import { ApiService } from '@shared';
import { WidgetIconDisplayControlComponent } from './widget-icon-display-control.component';

interface Handle {
  fixture: ComponentFixture<WidgetIconDisplayControlComponent>;
  changes: WidgetIconDisplay[];
  resets: number;
}

function createFixture(inputs: Partial<{
  display: WidgetIconDisplay;
  actionMode: boolean;
  aspectRatio: number;
  iconUrl: string | null;
  resetActive: boolean;
}> = {}): Handle {
  const apiSpy = jasmine.createSpyObj<ApiService>('ApiService', ['onNotification']);
  apiSpy.onNotification.and.callFake(() => new Subject());
  Object.defineProperty(apiSpy, 'connectionStateSignal', { value: signal('disconnected') });

  TestBed.configureTestingModule({
    providers: [provideZonelessChangeDetection(), { provide: ApiService, useValue: apiSpy }],
  });

  const fixture = TestBed.createComponent(WidgetIconDisplayControlComponent);
  fixture.componentRef.setInput('iconUrl', inputs.iconUrl === undefined ? 'https://host.invalid/icon' : inputs.iconUrl);
  if (inputs.display !== undefined) fixture.componentRef.setInput('display', inputs.display);
  if (inputs.actionMode !== undefined) fixture.componentRef.setInput('actionMode', inputs.actionMode);
  if (inputs.aspectRatio !== undefined) fixture.componentRef.setInput('aspectRatio', inputs.aspectRatio);
  if (inputs.resetActive !== undefined) fixture.componentRef.setInput('resetActive', inputs.resetActive);

  const changes: WidgetIconDisplay[] = [];
  let resets = 0;
  fixture.componentInstance.displayChange.subscribe(change => changes.push(change));
  fixture.componentInstance.resetRequested.subscribe(() => resets++);
  fixture.detectChanges();
  return { fixture, changes, get resets() { return resets; } };
}

function frame(fixture: ComponentFixture<WidgetIconDisplayControlComponent>): HTMLElement {
  return fixture.nativeElement.querySelector('.idc-frame') as HTMLElement;
}

function drag(fixture: ComponentFixture<WidgetIconDisplayControlComponent>, dx: number, dy: number): void {
  const element = frame(fixture);
  spyOn(element, 'getBoundingClientRect').and.returnValue({ width: 200, height: 100 } as DOMRect);
  element.dispatchEvent(new PointerEvent('pointerdown', { button: 0, pointerId: 1, clientX: 0, clientY: 0, bubbles: true }));
  element.dispatchEvent(new PointerEvent('pointermove', { pointerId: 1, clientX: dx, clientY: dy, bubbles: true }));
  element.dispatchEvent(new PointerEvent('pointerup', { pointerId: 1, bubbles: true }));
}

describe('WidgetIconDisplayControlComponent editor mode', () => {
  it('shows the defaults for a button with no stored framing', () => {
    const { fixture } = createFixture();
    const image = fixture.nativeElement.querySelector('.idc-image') as HTMLImageElement;

    expect(image.style.objectFit).toBe('contain');
    expect(image.style.opacity).toBe('1');
  });

  it('offers only the two real fit modes', () => {
    const { fixture } = createFixture();
    const labels = Array.from(fixture.nativeElement.querySelectorAll('.seg-option') as NodeListOf<HTMLElement>)
      .map(option => option.textContent?.trim());

    expect(labels).toEqual(['Contain', 'Cover']);
  });

  it('emits the picked fit mode alongside the framing it did not touch', () => {
    const { fixture, changes } = createFixture({ display: { zoom: 150 } });
    (fixture.nativeElement.querySelectorAll('.seg-option')[1] as HTMLElement).click();

    expect(changes).toEqual([{ zoom: 150, fit: 'cover' }]);
  });

  it('clamps a number typed beyond the allowed range', () => {
    const { fixture, changes } = createFixture();
    fixture.componentInstance['onNumberChange']('zoom', 9000);
    fixture.componentInstance['onNumberChange']('opacity', -30);

    expect(changes).toEqual([{ zoom: 400 }, { zoom: 400, opacity: 0 }]);
  });

  it('falls back to the default when a number field is cleared', () => {
    const { fixture, changes } = createFixture({ display: { opacity: 20 } });
    fixture.componentInstance['onNumberChange']('opacity', '');

    expect(changes).toEqual([{ opacity: 100 }]);
  });

  it('translates a drag into both offsets at once, as a share of the frame', () => {
    const { fixture, changes } = createFixture({ display: { offsetX: 0, offsetY: 0 } });
    drag(fixture, 40, -10);

    expect(changes).toEqual([{ offsetX: 20, offsetY: -10 }]);
  });

  it('adds a drag onto the offsets already stored', () => {
    const { fixture, changes } = createFixture({ display: { offsetX: 30, offsetY: 0 } });
    drag(fixture, 40, 0);

    expect(changes).toEqual([{ offsetX: 50, offsetY: 0 }]);
  });

  it('renders no preview at all without an image to frame', () => {
    const { fixture } = createFixture({ iconUrl: null });

    expect(fixture.nativeElement.querySelector('.idc-frame')).toBeNull();
    expect(fixture.nativeElement.querySelector('.idc-controls')).not.toBeNull();
  });

  it('accumulates edits that land before the stored framing catches up', () => {
    const { fixture, changes } = createFixture();
    fixture.componentInstance['onFitChange']('cover');
    fixture.componentInstance['onNumberChange']('zoom', 250);
    fixture.componentInstance['onNumberChange']('opacity', 35);

    expect(changes[changes.length - 1]).toEqual({ fit: 'cover', zoom: 250, opacity: 35 });
  });

  it('drops its working copy once the stored framing arrives', () => {
    const { fixture, changes } = createFixture();
    fixture.componentInstance['onNumberChange']('zoom', 250);
    fixture.componentRef.setInput('display', { opacity: 10 });
    fixture.detectChanges();
    fixture.componentInstance['onNumberChange']('offsetX', 5);

    expect(changes[changes.length - 1]).toEqual({ opacity: 10, offsetX: 5 });
  });

  it('zooms on the wheel, one step per notch', () => {
    const { fixture, changes } = createFixture({ display: { zoom: 100 } });
    frame(fixture).dispatchEvent(new WheelEvent('wheel', { deltaY: -1, bubbles: true, cancelable: true }));

    expect(changes).toEqual([{ zoom: 110 }]);
  });

  it('stops zooming out at the lower bound', () => {
    const { fixture, changes } = createFixture({ display: { zoom: 10 } });
    frame(fixture).dispatchEvent(new WheelEvent('wheel', { deltaY: 1, bubbles: true, cancelable: true }));

    expect(changes).toEqual([]);
  });

  it('asks for a reset rather than emitting a framing of its own', () => {
    const handle = createFixture({ display: { fit: 'cover', zoom: 300, opacity: 10 } });
    (handle.fixture.nativeElement.querySelector('.idc-reset button') as HTMLElement).click();

    expect(handle.resets).toBe(1);
    expect(handle.changes).toEqual([]);
  });

  it('frames the preview at the widget aspect ratio', () => {
    const { fixture } = createFixture({ aspectRatio: 2.1 });

    expect(frame(fixture).style.aspectRatio).toBe('2.1 / 1');
  });
});

describe('WidgetIconDisplayControlComponent action mode', () => {
  it('offers an Unchanged fit and selects it while nothing is set', () => {
    const { fixture } = createFixture({ actionMode: true });
    const options = Array.from(fixture.nativeElement.querySelectorAll('.seg-option') as NodeListOf<HTMLElement>);

    expect(options.map(option => option.textContent?.trim())).toEqual(['Unchanged', 'Contain', 'Cover']);
    expect(options[0].classList).toContain('active');
  });

  it('leaves a cleared number unchanged instead of writing the default', () => {
    const { fixture, changes } = createFixture({ actionMode: true, display: { zoom: 200 } });
    fixture.componentInstance['onNumberChange']('zoom', '');

    expect(changes).toEqual([{ zoom: undefined }]);
  });

  it('shows no preview, since the target widget\'s icon is unknowable while authoring', () => {
    const { fixture } = createFixture({ actionMode: true, iconUrl: null });

    expect(fixture.nativeElement.querySelector('.idc-frame')).toBeNull();
  });

  it('marks the reset as the state the block is in, which no field value could show', () => {
    const { fixture } = createFixture({ actionMode: true, resetActive: true });

    expect(fixture.nativeElement.querySelector('.idc-reset')?.classList).toContain('active');
  });

  it('asks for a reset, which for an action means clearing the stored framing', () => {
    const handle = createFixture({ actionMode: true, display: { zoom: 200 } });
    (handle.fixture.nativeElement.querySelector('.idc-reset button') as HTMLElement).click();

    expect(handle.resets).toBe(1);
    expect(handle.changes).toEqual([]);
  });
});
