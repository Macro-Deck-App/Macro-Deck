import {
  Directive, ElementRef, EventEmitter, Input, OnDestroy, OnInit, Output, computed, inject
} from '@angular/core';

import { acceptsIconDropPath, isIconPackArchivePath } from '../../domain/icon-drop.util';
import { ShellFileDropService } from '../../services/shell-file-drop.service';

// The dropped file's real path is the point: an application, a shortcut or a macOS bundle carries no
// image bytes, so only a path lets the host read the icon out of it. Paths come from the shell
// bridge, the only drop channel that fires inside the desktop shell - Tauri owns WebView
// drag-and-drop and swallows the HTML5 drop event. Outside the shell this stays inert.
@Directive({
  selector: '[sharedIconDropTarget]',
  standalone: true,
  host: {
    '[class.icon-drop-active]': 'dropActive()',
  },
})
export class IconDropTargetDirective implements OnInit, OnDestroy {
  @Input() multiple = false;

  @Input() packArchives = false;

  @Output() iconPathDropped = new EventEmitter<string>();

  @Output() iconPathsDropped = new EventEmitter<string[]>();

  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly fileDrop = inject(ShellFileDropService);

  readonly dropActive = computed(() => this.fileDrop.activeTarget() === this.host.nativeElement);

  private unregisterDrop: (() => void) | null = null;

  get supported(): boolean {
    return this.fileDrop.supported;
  }

  ngOnInit(): void {
    if (!this.fileDrop.supported) {
      return;
    }

    this.unregisterDrop = this.fileDrop.register(
      this.host.nativeElement,
      paths => this.multiple
        ? paths.filter(p => p.directory || acceptsIconDropPath(p) || (this.packArchives && isIconPackArchivePath(p)))
        : paths.filter(acceptsIconDropPath).slice(0, 1),
      paths => this.multiple
        ? this.iconPathsDropped.emit(paths.map(path => path.path))
        : this.iconPathDropped.emit(paths[0].path),
    );
  }

  ngOnDestroy(): void {
    this.unregisterDrop?.();
  }
}
