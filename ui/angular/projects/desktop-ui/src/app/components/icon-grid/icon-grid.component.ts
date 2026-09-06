import {
  AfterViewInit,
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  EventEmitter,
  Input,
  OnChanges,
  OnDestroy,
  Output,
  computed,
  inject,
  signal,
} from '@angular/core';
import { ScrollingModule } from '@angular/cdk/scrolling';
import { IconModel } from '../../services/icon-pack.service';
import { IconTileClick, IconTileComponent } from './icon-tile.component';

const TILE_GAP = 8;
const TILE_PADDING = 4;
const NAME_ROW_HEIGHT = 24;

@Component({
  selector: 'shared-icon-grid',
  standalone: true,
  imports: [ScrollingModule, IconTileComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './icon-grid.component.html',
  styleUrls: ['./icon-grid.component.scss']
})
export class IconGridComponent implements AfterViewInit, OnChanges, OnDestroy {
  private readonly host = inject(ElementRef<HTMLElement>);
  private resizeObserver: ResizeObserver | null = null;

  @Input({ required: true }) icons: IconModel[] = [];
  @Input() tileSize = 96;
  @Input() selectable = false;
  @Input() selectedIconId: string | null = null;
  @Input() selectedIconIds: ReadonlySet<string> | null = null;
  @Input() multiSelect = false;

  @Output() iconClick = new EventEmitter<IconTileClick>();
  @Output() iconContextMenu = new EventEmitter<{ icon: IconModel; x: number; y: number }>();

  protected readonly containerWidth = signal(0);
  protected readonly iconList = signal<IconModel[]>([]);

  protected readonly columns = computed(() => {
    const tileWidth = this.tileSize + TILE_PADDING * 2;
    const available = this.containerWidth() + TILE_GAP;
    return Math.max(1, Math.floor(available / (tileWidth + TILE_GAP)));
  });

  protected readonly rows = computed(() => {
    const icons = this.iconList();
    const columns = this.columns();
    const rows: IconModel[][] = [];
    for (let i = 0; i < icons.length; i += columns) {
      rows.push(icons.slice(i, i + columns));
    }

    return rows;
  });

  get rowHeight(): number {
    return this.tileSize + TILE_PADDING * 2 + NAME_ROW_HEIGHT + TILE_GAP;
  }

  ngOnChanges(): void {
    this.iconList.set(this.icons);
  }

  ngAfterViewInit(): void {
    const element = this.host.nativeElement;
    this.containerWidth.set(element.clientWidth);
    this.resizeObserver = new ResizeObserver(entries => {
      const width = entries[0]?.contentRect.width ?? 0;
      if (width > 0) {
        this.containerWidth.set(width);
      }
    });
    this.resizeObserver.observe(element);
  }

  ngOnDestroy(): void {
    this.resizeObserver?.disconnect();
  }

  protected trackRow(index: number, row: IconModel[]): string {
    return row[0]?.id ?? String(index);
  }

  protected isSelected(icon: IconModel): boolean {
    return this.selectable
      && (icon.id === this.selectedIconId || (this.selectedIconIds?.has(icon.id) ?? false));
  }
}
