import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { EMPTY } from 'rxjs';

import { ApiService } from '@shared';
import { ShellFileDropService } from '../../../services/shell-file-drop.service';
import { ShellOpenDialogOptions } from '../../../util/shell-bridge';
import { FilePathInputComponent } from './file-path-input.component';

describe('FilePathInputComponent', () => {
  let fixture: ComponentFixture<FilePathInputComponent>;
  let component: FilePathInputComponent;
  let showOpenDialog: jasmine.Spy<(options?: ShellOpenDialogOptions) => Promise<string | null>>;

  function useShell(bridge: object | null): void {
    if (bridge) {
      (window as { macroDeckShell?: unknown }).macroDeckShell = bridge;
    } else {
      delete (window as { macroDeckShell?: unknown }).macroDeckShell;
    }
  }

  function create(): void {
    TestBed.configureTestingModule({
      imports: [FilePathInputComponent],
      providers: [
        provideZonelessChangeDetection(),
        {
          provide: ApiService,
          useValue: {
            getFilesystemEntries: () => Promise.resolve({ path: '', entries: [] }),
            onNotification: () => EMPTY,
          },
        },
      ],
    });
    fixture = TestBed.createComponent(FilePathInputComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  beforeEach(() => {
    showOpenDialog = jasmine.createSpy('showOpenDialog').and.resolveTo('/tmp/picked.png');
    useShell({ showOpenDialog });
  });

  afterEach(() => useShell(null));

  it('takes the path from the native picker', async () => {
    create();
    const emitted: string[] = [];
    component.valueChange.subscribe(value => emitted.push(value));

    await component.browse();

    expect(showOpenDialog).toHaveBeenCalledWith({ directory: false, extensions: [] });
    expect(emitted).toEqual(['/tmp/picked.png']);
    expect(component.showBrowser()).toBeFalse();
  });

  it('asks the native picker for a directory in folder mode', async () => {
    create();
    component.kind = 'folder';

    await component.browse();

    expect(showOpenDialog).toHaveBeenCalledWith({ directory: true, extensions: [] });
  });

  it('passes the extension filter through', async () => {
    create();
    component.kind = 'image';
    component.extensions = ['png', 'webp'];

    await component.browse();

    expect(showOpenDialog).toHaveBeenCalledWith({ directory: false, extensions: ['png', 'webp'] });
  });

  it('keeps the current value when the picker is cancelled', async () => {
    showOpenDialog.and.resolveTo(null);
    create();
    component.value = '/tmp/keep.png';
    const emitted: string[] = [];
    component.valueChange.subscribe(value => emitted.push(value));

    await component.browse();

    expect(emitted).toEqual([]);
    expect(component.value).toBe('/tmp/keep.png');
    expect(component.showBrowser()).toBeFalse();
  });

  // Without this the Browse button is simply dead when the bridge call fails,
  // which is exactly what issue #122 reported on Windows.
  it('falls back to the in-app browser when the native picker errors', async () => {
    showOpenDialog.and.rejectWith(new Error('invalid args'));
    create();

    await component.browse();

    expect(component.showBrowser()).toBeTrue();
  });

  it('falls back to the in-app browser outside the desktop shell', async () => {
    useShell(null);
    create();

    await component.browse();

    expect(component.showBrowser()).toBeTrue();
  });

  it('registers itself as a drop target and takes the dropped path', () => {
    create();
    const drop = TestBed.inject(ShellFileDropService);
    const emitted: string[] = [];
    component.valueChange.subscribe(value => emitted.push(value));
    spyOn(document, 'elementFromPoint').and.returnValue(fixture.nativeElement as HTMLElement);

    drop.handleEvent({ kind: 'drop', paths: [{ path: '/tmp/dropped.png', directory: false }], x: 1, y: 1 });

    expect(emitted).toEqual(['/tmp/dropped.png']);
  });

  it('repaints the input with the dropped path', async () => {
    create();
    const drop = TestBed.inject(ShellFileDropService);
    spyOn(document, 'elementFromPoint').and.returnValue(fixture.nativeElement as HTMLElement);

    drop.handleEvent({ kind: 'drop', paths: [{ path: '/tmp/dropped.png', directory: false }], x: 1, y: 1 });
    await fixture.whenStable();

    const input = fixture.nativeElement.querySelector('input') as HTMLInputElement;
    expect(input.value).toBe('/tmp/dropped.png');
  });

  it('rejects a dropped directory in file mode and a dropped file in folder mode', () => {
    create();
    const drop = TestBed.inject(ShellFileDropService);
    const emitted: string[] = [];
    component.valueChange.subscribe(value => emitted.push(value));
    spyOn(document, 'elementFromPoint').and.returnValue(fixture.nativeElement as HTMLElement);

    drop.handleEvent({ kind: 'drop', paths: [{ path: '/tmp/dir', directory: true }], x: 1, y: 1 });
    component.extensions = ['exe', 'app'];
    drop.handleEvent({ kind: 'drop', paths: [{ path: '/tmp/dir', directory: true }], x: 1, y: 1 });
    component.kind = 'folder';
    drop.handleEvent({ kind: 'drop', paths: [{ path: '/tmp/a.png', directory: false }], x: 1, y: 1 });

    expect(emitted).toEqual([]);
  });

  // A macOS .app bundle is a directory, so a blanket directory rejection would make the Launch
  // Application field refuse the very thing it asks for (issue #395).
  it('takes a dropped application bundle when the field declares its extension', () => {
    create();
    component.extensions = ['exe', 'lnk', 'app'];
    const drop = TestBed.inject(ShellFileDropService);
    const emitted: string[] = [];
    component.valueChange.subscribe(value => emitted.push(value));
    spyOn(document, 'elementFromPoint').and.returnValue(fixture.nativeElement as HTMLElement);

    drop.handleEvent({
      kind: 'drop',
      paths: [{ path: '/System/Applications/Calculator.app', directory: true }],
      x: 1,
      y: 1,
    });

    expect(emitted).toEqual(['/System/Applications/Calculator.app']);
  });

  it('rejects a dropped file whose extension is not allowed', () => {
    create();
    component.kind = 'image';
    component.extensions = ['png'];
    const drop = TestBed.inject(ShellFileDropService);
    const emitted: string[] = [];
    component.valueChange.subscribe(value => emitted.push(value));
    spyOn(document, 'elementFromPoint').and.returnValue(fixture.nativeElement as HTMLElement);

    drop.handleEvent({ kind: 'drop', paths: [{ path: 'C:\\tmp\\a.txt', directory: false }], x: 1, y: 1 });
    expect(emitted).toEqual([]);

    drop.handleEvent({ kind: 'drop', paths: [{ path: 'C:\\tmp\\a.PNG', directory: false }], x: 1, y: 1 });
    expect(emitted).toEqual(['C:\\tmp\\a.PNG']);
  });

  it('unregisters its drop target on destroy', () => {
    create();
    const drop = TestBed.inject(ShellFileDropService);
    const emitted: string[] = [];
    component.valueChange.subscribe(value => emitted.push(value));
    spyOn(document, 'elementFromPoint').and.returnValue(fixture.nativeElement as HTMLElement);

    fixture.destroy();
    drop.handleEvent({ kind: 'drop', paths: [{ path: '/tmp/dropped.png', directory: false }], x: 1, y: 1 });

    expect(emitted).toEqual([]);
  });
});
