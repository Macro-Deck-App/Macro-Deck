import { Directive, EventEmitter, HostBinding, HostListener, Output } from '@angular/core';

@Directive({
  selector: '[sharedFileDrop]',
  standalone: true
})
export class FileDropDirective {
  @Output() filesDropped = new EventEmitter<File[]>();

  @HostBinding('class.file-drag-over') isDragOver = false;

  private dragDepth = 0;

  @HostListener('dragenter', ['$event'])
  onDragEnter(event: DragEvent): void {
    if (!this.hasFiles(event)) {
      return;
    }

    event.preventDefault();
    this.dragDepth++;
    this.isDragOver = true;
  }

  @HostListener('dragover', ['$event'])
  onDragOver(event: DragEvent): void {
    if (!this.hasFiles(event)) {
      return;
    }

    event.preventDefault();
    if (event.dataTransfer) {
      event.dataTransfer.dropEffect = 'copy';
    }
  }

  @HostListener('dragleave', ['$event'])
  onDragLeave(event: DragEvent): void {
    if (!this.hasFiles(event)) {
      return;
    }

    this.dragDepth = Math.max(0, this.dragDepth - 1);
    if (this.dragDepth === 0) {
      this.isDragOver = false;
    }
  }

  @HostListener('drop', ['$event'])
  async onDrop(event: DragEvent): Promise<void> {
    if (!this.hasFiles(event)) {
      return;
    }

    event.preventDefault();
    this.dragDepth = 0;
    this.isDragOver = false;

    const transfer = event.dataTransfer;
    if (!transfer) {
      return;
    }

    const files = await this.collectFiles(transfer);
    if (files.length > 0) {
      this.filesDropped.emit(files);
    }
  }

  private hasFiles(event: DragEvent): boolean {
    return event.dataTransfer?.types.includes('Files') ?? false;
  }

  private async collectFiles(transfer: DataTransfer): Promise<File[]> {
    const entries: FileSystemEntry[] = [];
    for (const item of Array.from(transfer.items)) {
      const entry = item.webkitGetAsEntry();
      if (entry) {
        entries.push(entry);
      }
    }

    if (entries.length === 0) {
      return Array.from(transfer.files);
    }

    const files: File[] = [];
    for (const entry of entries) {
      await this.traverse(entry, files);
    }

    return files;
  }

  private async traverse(entry: FileSystemEntry, files: File[]): Promise<void> {
    if (entry.isFile) {
      const file = await new Promise<File | null>(resolve => {
        (entry as FileSystemFileEntry).file(resolve, () => resolve(null));
      });
      if (file) {
        const relativePath = entry.fullPath.replace(/^\//, '');
        if (relativePath !== file.name) {
          Object.defineProperty(file, 'webkitRelativePath', { value: relativePath });
        }

        files.push(file);
      }

      return;
    }

    if (entry.isDirectory) {
      const reader = (entry as FileSystemDirectoryEntry).createReader();
      let batch: FileSystemEntry[];
      do {
        batch = await new Promise<FileSystemEntry[]>(resolve => {
          reader.readEntries(resolve, () => resolve([]));
        });
        for (const child of batch) {
          await this.traverse(child, files);
        }
      } while (batch.length > 0);
    }
  }
}
