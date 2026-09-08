import { Component, provideZonelessChangeDetection, ChangeDetectionStrategy } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';

import { ShellDropKind } from '../../domain/shell-drop.util';
import { ShellFileDropService } from '../../services/shell-file-drop.service';
import { ShellFileDropEvent } from '../../util/shell-bridge';
import { ShellDrop, ShellDropTargetDirective } from './shell-drop-target.directive';

@Component({
  standalone: true,
  imports: [ShellDropTargetDirective],
  changeDetection: ChangeDetectionStrategy.Eager,
  template: `<div
    class="zone"
    sharedShellDropTarget
    [dropKinds]="kinds"
    [dropAcceptAt]="acceptAt"
    [highlightHost]="highlight"
    (shellDropHover)="hovered?.push($event)"
    (shellDropped)="dropped.push($event)"
  ></div>`,
})
class HostComponent {
  kinds: ShellDropKind[] = ['profile'];
  acceptAt?: (kind: string, at: { x: number; y: number }) => boolean;
  highlight = true;
  dropped: ShellDrop[] = [];
  hovered?: (ShellDrop | null)[];
}

describe('ShellDropTargetDirective', () => {
  let fixture: ComponentFixture<HostComponent>;
  let zone: HTMLElement;

  function useShell(bridge: object | null): void {
    if (bridge) {
      (window as { macroDeckShell?: unknown }).macroDeckShell = bridge;
    } else {
      delete (window as { macroDeckShell?: unknown }).macroDeckShell;
    }
  }

  function create(acceptAt?: HostComponent['acceptAt'], kinds?: ShellDropKind[], highlight = true): void {
    TestBed.configureTestingModule({
      imports: [HostComponent],
      providers: [provideZonelessChangeDetection()],
    });
    fixture = TestBed.createComponent(HostComponent);
    fixture.componentInstance.acceptAt = acceptAt;
    if (kinds) {
      fixture.componentInstance.kinds = kinds;
    }
    fixture.componentInstance.highlight = highlight;
    fixture.detectChanges();
    zone = fixture.nativeElement.querySelector('.zone') as HTMLElement;
    spyOn(document, 'elementFromPoint').and.returnValue(zone);
  }

  function send(overrides: Partial<ShellFileDropEvent>): void {
    TestBed.inject(ShellFileDropService).handleEvent({ kind: 'drop', paths: [], x: 10, y: 20, ...overrides });
  }

  beforeEach(() => useShell({ onFileDrop: () => Promise.resolve(() => undefined) }));

  afterEach(() => useShell(null));

  it('emits the kind, path and drop position', () => {
    create();

    send({ paths: [{ path: '/tmp/Streaming.macroDeckProfile', directory: false }] });

    expect(fixture.componentInstance.dropped).toEqual([
      { kind: 'profile', path: '/tmp/Streaming.macroDeckProfile', x: 10, y: 20 },
    ]);
  });

  it('ignores an archive of a kind it does not take', () => {
    create();

    send({ paths: [{ path: '/tmp/Lights.macroDeckFolder', directory: false }] });

    expect(fixture.componentInstance.dropped).toEqual([]);
  });

  it('takes only the first archive of a multi-file drop', () => {
    create();

    send({
      paths: [
        { path: '/tmp/one.macroDeckProfile', directory: false },
        { path: '/tmp/two.macroDeckProfile', directory: false },
      ],
    });

    expect(fixture.componentInstance.dropped.length).toBe(1);
    expect(fixture.componentInstance.dropped[0].path).toBe('/tmp/one.macroDeckProfile');
  });

  it('honours a position predicate on the drop as well as the hover', () => {
    create((_kind, at) => at.x > 100);

    send({ kind: 'enter', paths: [{ path: '/tmp/a.macroDeckProfile', directory: false }], x: 10, y: 20 });
    expect(TestBed.inject(ShellFileDropService).activeTarget()).toBeNull();

    send({ paths: [{ path: '/tmp/a.macroDeckProfile', directory: false }], x: 10, y: 20 });
    expect(fixture.componentInstance.dropped).toEqual([]);

    send({ paths: [{ path: '/tmp/a.macroDeckProfile', directory: false }], x: 200, y: 20 });
    expect(fixture.componentInstance.dropped.length).toBe(1);
  });

  it('highlights the target while an acceptable archive hovers it', () => {
    create();

    send({ kind: 'enter', paths: [{ path: '/tmp/a.macroDeckProfile', directory: false }] });

    expect(TestBed.inject(ShellFileDropService).activeTarget()).toBe(zone);
  });

  // A macOS bundle is a directory, and the deck takes it as an application rather than as a folder
  // archive - the one payload where the directory flag does not mean "not a file" (issue #395).
  it('emits an application bundle for a target that takes applications', () => {
    create(undefined, ['profile', 'application']);

    send({ paths: [{ path: '/Applications/Calculator.app', directory: true }] });

    expect(fixture.componentInstance.dropped).toEqual([
      { kind: 'application', path: '/Applications/Calculator.app', x: 10, y: 20 },
    ]);
  });

  it('ignores an application on a target that only takes archives', () => {
    create();

    send({ paths: [{ path: '/Applications/Calculator.app', directory: true }] });

    expect(fixture.componentInstance.dropped).toEqual([]);
  });

  it('reports where an acceptable drag is hovering, and when it leaves', async () => {
    create();
    const hovers: (ShellDrop | null)[] = [];
    fixture.componentInstance.hovered = hovers;

    send({ kind: 'enter', paths: [{ path: '/tmp/a.macroDeckProfile', directory: false }], x: 10, y: 20 });
    send({ kind: 'over', paths: [], x: 40, y: 20 });
    send({ kind: 'leave', paths: [] });
    await fixture.whenStable();

    expect(hovers).toEqual([
      { kind: 'profile', path: '/tmp/a.macroDeckProfile', x: 10, y: 20 },
      { kind: 'profile', path: '/tmp/a.macroDeckProfile', x: 40, y: 20 },
      null,
    ]);
  });

  it('does not outline the host when highlighting is turned off', () => {
    create(undefined, undefined, false);

    send({ kind: 'enter', paths: [{ path: '/tmp/a.macroDeckProfile', directory: false }] });
    fixture.detectChanges();

    expect(TestBed.inject(ShellFileDropService).activeTarget()).toBe(zone);
    expect(zone.classList.contains('shell-drop-active')).toBeFalse();
  });

  // Outside the desktop shell there is no bridge, so the directive must not claim to be a drop
  // target at all - the caller uses that to hide its drop hint.
  it('stays inert without the shell bridge', () => {
    useShell(null);
    create();

    send({ paths: [{ path: '/tmp/a.macroDeckProfile', directory: false }] });

    expect(fixture.componentInstance.dropped).toEqual([]);
  });
});
