import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { ShellDroppedPath, ShellFileDropEvent } from '../util/shell-bridge';
import { ShellFileDropService } from './shell-file-drop.service';

describe('ShellFileDropService', () => {
  let service: ShellFileDropService;
  let target: HTMLElement;
  let child: HTMLElement;
  let dropped: ShellDroppedPath[][];
  let listeners: ((event: ShellFileDropEvent) => void)[];
  let unlisten: jasmine.Spy;

  function event(overrides: Partial<ShellFileDropEvent> = {}): ShellFileDropEvent {
    return { kind: 'drop', paths: [], x: 0, y: 0, ...overrides };
  }

  function file(path: string): ShellDroppedPath {
    return { path, directory: false };
  }

  function pointHits(element: Element | null): void {
    spyOn(document, 'elementFromPoint').and.returnValue(element);
  }

  beforeEach(() => {
    listeners = [];
    unlisten = jasmine.createSpy('unlisten');
    (window as { macroDeckShell?: unknown }).macroDeckShell = {
      onFileDrop: (callback: (event: ShellFileDropEvent) => void) => {
        listeners.push(callback);
        return Promise.resolve(unlisten);
      },
    };

    TestBed.configureTestingModule({ providers: [provideZonelessChangeDetection()] });
    service = TestBed.inject(ShellFileDropService);

    target = document.createElement('div');
    child = document.createElement('input');
    target.appendChild(child);
    document.body.appendChild(target);

    dropped = [];
  });

  afterEach(() => {
    target.remove();
    delete (window as { macroDeckShell?: unknown }).macroDeckShell;
  });

  function register(accept: (paths: ShellDroppedPath[]) => ShellDroppedPath[] = paths => paths) {
    return service.register(target, accept, paths => dropped.push(paths));
  }

  it('reports support from the bridge', () => {
    expect(service.supported).toBeTrue();
  });

  it('subscribes to the shell only once targets exist', async () => {
    expect(listeners.length).toBe(0);

    register();
    await Promise.resolve();

    expect(listeners.length).toBe(1);
  });

  it('marks a hovered target active and delivers the drop to it', () => {
    register();
    pointHits(child);

    service.handleEvent(event({ kind: 'enter', paths: [file('/tmp/a.png')], x: 1, y: 1 }));
    expect(service.activeTarget()).toBe(target);

    service.handleEvent(event({ kind: 'drop', paths: [file('/tmp/a.png')], x: 1, y: 1 }));
    expect(dropped).toEqual([[file('/tmp/a.png')]]);
    expect(service.activeTarget()).toBeNull();
  });

  it('keeps the dragged paths across hover events that carry none', () => {
    register();
    pointHits(child);

    service.handleEvent(event({ kind: 'enter', paths: [file('/tmp/a.png')], x: 1, y: 1 }));
    service.handleEvent(event({ kind: 'over', paths: [], x: 1, y: 1 }));

    expect(service.activeTarget()).toBe(target);
  });

  it('stays inactive when the target rejects the payload and drops nothing', () => {
    register(() => []);
    pointHits(child);

    service.handleEvent(event({ kind: 'enter', paths: [file('/tmp/a.txt')], x: 1, y: 1 }));
    expect(service.activeTarget()).toBeNull();

    service.handleEvent(event({ kind: 'drop', paths: [file('/tmp/a.txt')], x: 1, y: 1 }));
    expect(dropped).toEqual([]);
  });

  it('delivers only the accepted subset of a multi-file drop', () => {
    register(paths => paths.filter(path => path.path.endsWith('.png')));
    pointHits(child);

    service.handleEvent(
      event({ kind: 'drop', paths: [file('/tmp/a.txt'), file('/tmp/b.png')], x: 1, y: 1 }),
    );

    expect(dropped).toEqual([[file('/tmp/b.png')]]);
  });

  it('ignores a drop outside every registered target', () => {
    register();
    pointHits(document.body);

    service.handleEvent(event({ kind: 'drop', paths: [file('/tmp/a.png')], x: 1, y: 1 }));

    expect(dropped).toEqual([]);
  });

  it('clears the highlight when the drag leaves the window', () => {
    register();
    pointHits(child);
    service.handleEvent(event({ kind: 'enter', paths: [file('/tmp/a.png')], x: 1, y: 1 }));

    service.handleEvent(event({ kind: 'leave' }));

    expect(service.activeTarget()).toBeNull();
  });

  it('stops delivering to an unregistered target and unsubscribes with the last one', async () => {
    const unregister = register();
    await Promise.resolve();

    unregister();
    await Promise.resolve();
    pointHits(child);
    service.handleEvent(event({ kind: 'drop', paths: [file('/tmp/a.png')], x: 1, y: 1 }));

    expect(dropped).toEqual([]);
    expect(unlisten).toHaveBeenCalled();
  });
});
