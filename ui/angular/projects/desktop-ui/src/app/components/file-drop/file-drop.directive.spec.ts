import { Component, provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { FileDropDirective } from './file-drop.directive';

@Component({
  standalone: true,
  imports: [FileDropDirective],
  template: `<div class="zone" sharedFileDrop (filesDropped)="files = $event"></div>`,
})
class HostComponent {
  files: File[] = [];
}

describe('FileDropDirective', () => {
  let fixture: ComponentFixture<HostComponent>;
  let zone: HTMLElement;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [HostComponent],
      providers: [provideZonelessChangeDetection()],
    });
    fixture = TestBed.createComponent(HostComponent);
    fixture.detectChanges();
    zone = fixture.nativeElement.querySelector('.zone');
  });

  function dragEvent(type: string, dataTransfer: Partial<DataTransfer>): DragEvent {
    const event = new Event(type, { bubbles: true, cancelable: true }) as DragEvent;
    Object.defineProperty(event, 'dataTransfer', { value: dataTransfer });
    return event;
  }

  it('marks the host while a file drag hovers and clears it on leave', () => {
    const transfer = { types: ['Files'] } as unknown as DataTransfer;

    zone.dispatchEvent(dragEvent('dragenter', transfer));
    fixture.detectChanges();
    expect(zone.classList).toContain('file-drag-over');

    zone.dispatchEvent(dragEvent('dragleave', transfer));
    fixture.detectChanges();
    expect(zone.classList).not.toContain('file-drag-over');
  });

  it('ignores drags without files', () => {
    const transfer = { types: ['text/plain'] } as unknown as DataTransfer;

    zone.dispatchEvent(dragEvent('dragenter', transfer));
    fixture.detectChanges();

    expect(zone.classList).not.toContain('file-drag-over');
  });

  it('emits plain dropped files', async () => {
    const file = new File(['x'], 'a.png');
    const transfer = {
      types: ['Files'],
      files: [file],
      items: [],
    } as unknown as DataTransfer;

    zone.dispatchEvent(dragEvent('drop', transfer));
    await new Promise(resolve => setTimeout(resolve));

    expect(fixture.componentInstance.files.map(f => f.name)).toEqual(['a.png']);
  });

  it('traverses dropped directories recursively and preserves relative paths', async () => {
    const nestedFile = new File(['x'], 'b.png');

    const fileEntry = {
      isFile: true,
      isDirectory: false,
      fullPath: '/folder/nested/b.png',
      file: (resolve: (f: File) => void) => resolve(nestedFile),
    };
    let read = false;
    const directoryEntry = {
      isFile: false,
      isDirectory: true,
      fullPath: '/folder',
      createReader: () => ({
        readEntries: (resolve: (entries: unknown[]) => void) => {
          resolve(read ? [] : [fileEntry]);
          read = true;
        },
      }),
    };

    const transfer = {
      types: ['Files'],
      files: [],
      items: [{ webkitGetAsEntry: () => directoryEntry }],
    } as unknown as DataTransfer;

    zone.dispatchEvent(dragEvent('drop', transfer));
    await new Promise(resolve => setTimeout(resolve));

    const files = fixture.componentInstance.files;
    expect(files.length).toBe(1);
    expect((files[0] as File & { webkitRelativePath?: string }).webkitRelativePath)
      .toBe('folder/nested/b.png');
  });
});
