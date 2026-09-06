import { Component, provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';

import { ShellFileDropService } from '../../services/shell-file-drop.service';
import { ShellFileDropEvent } from '../../util/shell-bridge';
import { IconDropTargetDirective } from './icon-drop-target.directive';

@Component({
  standalone: true,
  imports: [IconDropTargetDirective],
  template: `<div class="tile" sharedIconDropTarget (iconPathDropped)="dropped.push($event)"></div>`,
})
class HostComponent {
  dropped: string[] = [];
}

@Component({
  standalone: true,
  imports: [IconDropTargetDirective],
  template: `<div class="zone" sharedIconDropTarget [multiple]="true" (iconPathsDropped)="dropped = $event"></div>`,
})
class MultipleHostComponent {
  dropped: string[] = [];
}

@Component({
  standalone: true,
  imports: [IconDropTargetDirective],
  template: `<div class="zone" sharedIconDropTarget [multiple]="true" [packArchives]="true"
    (iconPathsDropped)="dropped = $event"></div>`,
})
class PackArchivesHostComponent {
  dropped: string[] = [];
}

@Component({
  standalone: true,
  imports: [IconDropTargetDirective],
  template: `<div class="zone" sharedIconDropTarget [packArchives]="true"
    (iconPathDropped)="dropped.push($event)"></div>`,
})
class SingleModePackArchivesHostComponent {
  dropped: string[] = [];
}

describe('IconDropTargetDirective', () => {
  let fixture: ComponentFixture<HostComponent>;
  let tile: HTMLElement;

  function useShell(bridge: object | null): void {
    if (bridge) {
      (window as { macroDeckShell?: unknown }).macroDeckShell = bridge;
    } else {
      delete (window as { macroDeckShell?: unknown }).macroDeckShell;
    }
  }

  function create(): void {
    TestBed.configureTestingModule({
      imports: [HostComponent],
      providers: [provideZonelessChangeDetection()],
    });
    fixture = TestBed.createComponent(HostComponent);
    fixture.detectChanges();
    tile = fixture.nativeElement.querySelector('.tile') as HTMLElement;
  }

  function drop(overrides: Partial<ShellFileDropEvent>): void {
    TestBed.inject(ShellFileDropService).handleEvent({ kind: 'drop', paths: [], x: 1, y: 1, ...overrides });
  }

  beforeEach(() => {
    useShell({ onFileDrop: () => Promise.resolve(() => undefined) });
  });

  afterEach(() => useShell(null));

  it('emits the path of a dropped image', () => {
    create();
    spyOn(document, 'elementFromPoint').and.returnValue(tile);

    drop({ paths: [{ path: '/Users/me/logo.png', directory: false }] });

    expect(fixture.componentInstance.dropped).toEqual(['/Users/me/logo.png']);
  });

  it('emits the path of a dropped application bundle, which arrives as a directory', () => {
    create();
    spyOn(document, 'elementFromPoint').and.returnValue(tile);

    drop({ paths: [{ path: '/Applications/Spotify.app', directory: true }] });

    expect(fixture.componentInstance.dropped).toEqual(['/Applications/Spotify.app']);
  });

  it('ignores a payload it cannot read an icon from', () => {
    create();
    spyOn(document, 'elementFromPoint').and.returnValue(tile);

    drop({ paths: [{ path: '/Users/me/notes.txt', directory: false }] });

    expect(fixture.componentInstance.dropped).toEqual([]);
  });

  it('takes only the first acceptable path of a multi-file drop', () => {
    create();
    spyOn(document, 'elementFromPoint').and.returnValue(tile);

    drop({
      paths: [
        { path: '/Users/me/notes.txt', directory: false },
        { path: '/Users/me/first.png', directory: false },
        { path: '/Users/me/second.png', directory: false },
      ],
    });

    expect(fixture.componentInstance.dropped).toEqual(['/Users/me/first.png']);
  });

  it('takes every acceptable path plus folders in multiple mode', () => {
    TestBed.configureTestingModule({
      imports: [MultipleHostComponent],
      providers: [provideZonelessChangeDetection()],
    });
    const multiFixture = TestBed.createComponent(MultipleHostComponent);
    multiFixture.detectChanges();
    const zone = multiFixture.nativeElement.querySelector('.zone') as HTMLElement;
    spyOn(document, 'elementFromPoint').and.returnValue(zone);

    drop({
      paths: [
        { path: '/Users/me/notes.txt', directory: false },
        { path: '/Users/me/first.png', directory: false },
        { path: '/Users/me/second.png', directory: false },
        { path: '/Users/me/icons', directory: true },
      ],
    });

    expect(multiFixture.componentInstance.dropped)
      .toEqual(['/Users/me/first.png', '/Users/me/second.png', '/Users/me/icons']);
  });

  it('marks itself active while an acceptable drag hovers and clears it on drop', async () => {
    create();
    spyOn(document, 'elementFromPoint').and.returnValue(tile);

    drop({ kind: 'enter', paths: [{ path: '/Users/me/logo.png', directory: false }] });
    await fixture.whenStable();
    expect(tile.classList).toContain('icon-drop-active');

    drop({ paths: [{ path: '/Users/me/logo.png', directory: false }] });
    await fixture.whenStable();
    expect(tile.classList).not.toContain('icon-drop-active');
  });

  it('stays inactive while a drag carries nothing it accepts', async () => {
    create();
    spyOn(document, 'elementFromPoint').and.returnValue(tile);

    drop({ kind: 'enter', paths: [{ path: '/Users/me/notes.txt', directory: false }] });
    await fixture.whenStable();

    expect(tile.classList).not.toContain('icon-drop-active');
  });

  it('unregisters its drop target on destroy', () => {
    create();
    spyOn(document, 'elementFromPoint').and.returnValue(tile);
    const host = fixture.componentInstance;
    fixture.destroy();

    drop({ paths: [{ path: '/Users/me/logo.png', directory: false }] });

    expect(host.dropped).toEqual([]);
  });

  it('defaults packArchives to false, so an archive path is not accepted', () => {
    TestBed.configureTestingModule({
      imports: [MultipleHostComponent],
      providers: [provideZonelessChangeDetection()],
    });
    const multiFixture = TestBed.createComponent(MultipleHostComponent);
    multiFixture.detectChanges();
    const zone = multiFixture.nativeElement.querySelector('.zone') as HTMLElement;
    spyOn(document, 'elementFromPoint').and.returnValue(zone);

    drop({
      paths: [
        { path: '/d/Neon.macroDeckIconPack', directory: false },
        { path: '/d/logo.png', directory: false },
      ],
    });

    expect(multiFixture.componentInstance.dropped).toEqual(['/d/logo.png']);
  });

  it('accepts archives alongside icons and folders when packArchives is true', () => {
    TestBed.configureTestingModule({
      imports: [PackArchivesHostComponent],
      providers: [provideZonelessChangeDetection()],
    });
    const archiveFixture = TestBed.createComponent(PackArchivesHostComponent);
    archiveFixture.detectChanges();
    const zone = archiveFixture.nativeElement.querySelector('.zone') as HTMLElement;
    spyOn(document, 'elementFromPoint').and.returnValue(zone);

    drop({
      paths: [
        { path: '/d/notes.txt', directory: false },
        { path: '/d/Neon.macroDeckIconPack', directory: false },
        { path: '/d/logo.png', directory: false },
        { path: '/d/icons', directory: true },
      ],
    });

    expect(archiveFixture.componentInstance.dropped)
      .toEqual(['/d/Neon.macroDeckIconPack', '/d/logo.png', '/d/icons']);
  });

  it('is inert in single mode even when packArchives is true', () => {
    TestBed.configureTestingModule({
      imports: [SingleModePackArchivesHostComponent],
      providers: [provideZonelessChangeDetection()],
    });
    const singleFixture = TestBed.createComponent(SingleModePackArchivesHostComponent);
    singleFixture.detectChanges();
    const zone = singleFixture.nativeElement.querySelector('.zone') as HTMLElement;
    spyOn(document, 'elementFromPoint').and.returnValue(zone);

    drop({ kind: 'enter', paths: [{ path: '/d/Neon.macroDeckIconPack', directory: false }] });
    expect(zone.classList).not.toContain('icon-drop-active');

    drop({ paths: [{ path: '/d/Neon.macroDeckIconPack', directory: false }] });
    expect(singleFixture.componentInstance.dropped).toEqual([]);
  });

  it('tracks the active class by archive acceptance and clears it after the drop', async () => {
    TestBed.configureTestingModule({
      imports: [PackArchivesHostComponent],
      providers: [provideZonelessChangeDetection()],
    });
    const archiveFixture = TestBed.createComponent(PackArchivesHostComponent);
    archiveFixture.detectChanges();
    const zone = archiveFixture.nativeElement.querySelector('.zone') as HTMLElement;
    spyOn(document, 'elementFromPoint').and.returnValue(zone);

    drop({ kind: 'enter', paths: [{ path: '/d/Neon.macroDeckIconPack', directory: false }] });
    await archiveFixture.whenStable();
    expect(zone.classList).toContain('icon-drop-active');

    drop({ paths: [{ path: '/d/Neon.macroDeckIconPack', directory: false }] });
    await archiveFixture.whenStable();
    expect(zone.classList).not.toContain('icon-drop-active');
  });

  it('registers nothing when the shell bridge is absent', () => {
    useShell(null);
    create();
    const register = spyOn(TestBed.inject(ShellFileDropService), 'register');

    expect(register).not.toHaveBeenCalled();
    expect(fixture.debugElement.children[0].injector.get(IconDropTargetDirective).supported).toBeFalse();
  });
});
