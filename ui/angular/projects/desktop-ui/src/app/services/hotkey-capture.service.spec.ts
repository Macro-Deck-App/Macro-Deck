import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { HotkeyCaptureService } from './hotkey-capture.service';

describe('HotkeyCaptureService', () => {
  let service: HotkeyCaptureService;
  let host: HTMLElement;
  let outside: HTMLElement;
  let recorded: KeyboardEvent[];
  let canceled: number;

  function arm(): void {
    service.start(host, {
      key: event => recorded.push(event),
      cancel: () => canceled++,
    });
  }

  function press(init: KeyboardEventInit): KeyboardEvent {
    const event = new KeyboardEvent('keydown', { ...init, bubbles: true, cancelable: true });
    document.dispatchEvent(event);
    return event;
  }

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideZonelessChangeDetection()] });
    service = TestBed.inject(HotkeyCaptureService);
    host = document.createElement('div');
    outside = document.createElement('div');
    document.body.append(host, outside);
    recorded = [];
    canceled = 0;
  });

  afterEach(() => {
    service.stop();
    host.remove();
    outside.remove();
    delete (window as { macroDeckShell?: unknown }).macroDeckShell;
  });

  it('records a combination pressed with nothing focused', () => {
    arm();
    press({ key: 'k', code: 'KeyK', ctrlKey: true, shiftKey: true });

    expect(recorded.length).toBe(1);
    expect(recorded[0].key).toBe('k');
    expect(recorded[0].ctrlKey).toBeTrue();
    expect(service.active).toBeFalse();
  });

  it('claims the key so nothing else acts on the combination', () => {
    arm();
    const event = press({ key: 'c', code: 'KeyC', metaKey: true });

    expect(event.defaultPrevented).toBeTrue();
  });

  it('waits for a real key while only modifiers are held', () => {
    arm();
    press({ key: 'Control', code: 'ControlLeft', ctrlKey: true });
    press({ key: 'Shift', code: 'ShiftLeft', ctrlKey: true, shiftKey: true });

    expect(recorded).toEqual([]);
    expect(canceled).toBe(0);
    expect(service.active).toBeTrue();
  });

  it('cancels on Escape without letting it reach anything else', () => {
    arm();
    const event = press({ key: 'Escape', code: 'Escape' });

    expect(recorded).toEqual([]);
    expect(canceled).toBe(1);
    expect(event.defaultPrevented).toBeTrue();
    expect(service.active).toBeFalse();
  });

  it('keeps recording while the press is inside the recorder', () => {
    arm();
    host.dispatchEvent(new MouseEvent('mousedown', { bubbles: true }));

    expect(canceled).toBe(0);
    expect(service.active).toBeTrue();
  });

  it('cancels on a press elsewhere and on the window losing focus', () => {
    arm();
    outside.dispatchEvent(new MouseEvent('mousedown', { bubbles: true }));
    expect(canceled).toBe(1);

    arm();
    window.dispatchEvent(new Event('blur'));
    expect(canceled).toBe(2);
  });

  it('leaves no listener behind once the capture ended', () => {
    arm();
    press({ key: 'a', code: 'KeyA' });
    press({ key: 'b', code: 'KeyB' });

    expect(recorded.length).toBe(1);
  });

  it('arming a second capture cancels the first', () => {
    arm();
    arm();
    press({ key: 'a', code: 'KeyA' });

    expect(canceled).toBe(1);
    expect(recorded.length).toBe(1);
  });

  it('asks the shell to release its key equivalents while armed', async () => {
    const setHotkeyCapture = jasmine.createSpy('setHotkeyCapture').and.resolveTo(undefined);
    (window as { macroDeckShell?: unknown }).macroDeckShell = { setHotkeyCapture };

    arm();
    expect(setHotkeyCapture).toHaveBeenCalledOnceWith(true);

    press({ key: 'a', code: 'KeyA' });
    expect(setHotkeyCapture).toHaveBeenCalledWith(false);
  });

  it('records normally when there is no shell at all', () => {
    arm();
    press({ key: 'a', code: 'KeyA' });

    expect(recorded.length).toBe(1);
  });
});
