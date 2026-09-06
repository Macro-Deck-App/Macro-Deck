import {
  ChangeDetectionStrategy,
  Component,
  EventEmitter,
  Input,
  Output,
  ViewChild,
  ViewEncapsulation,
  computed,
  inject,
  signal,
} from '@angular/core';
import { FormsModule } from '@angular/forms';

import { AppStrings, isListInsideLoop } from '@macro-deck/runtime';
import { ApiService, InputComponent, LocalizationService, ModalComponent, dismissModal } from '@shared';
import type { ActionBlockDefinition } from '@macro-deck/runtime';
import { ActionFlowStore } from '../services/action-flow.store';
import { IntegrationService } from '../../../services/integration.service';
import { ActionCapabilitiesComponent } from './action-capabilities.component';

interface PickerCategory {
  name: string;
  iconUrl: string | null;
  fallbackIcon: string;
  count: number;
}

@Component({
  selector: 'shared-action-picker',
  standalone: true,
  imports: [FormsModule, ModalComponent, InputComponent, ActionCapabilitiesComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  encapsulation: ViewEncapsulation.None,
  templateUrl: './action-picker.component.html',
  styleUrls: ['./action-picker.component.scss', '../action-card/action-colors.scss'],
})
export class ActionPickerComponent {
  private static readonly CategoryGlyphs: Readonly<Record<string, string>> = {
    'app.macro-deck.deck': 'icon-grid',
    'app.macro-deck.scripts': 'icon-list-play',
    'app.macro-deck.variables': 'icon-braces-x',
    'app.macro-deck.widget': 'icon-action-button-type',
    Logic: 'icon-code',
  };

  @Input({ required: true })
  set definitions(value: ActionBlockDefinition[]) {
    this.definitionsInput.set(value ?? []);
  }

  @Output() readonly picked = new EventEmitter<ActionBlockDefinition>();

  @Output() readonly closed = new EventEmitter<void>();

  protected readonly store = inject(ActionFlowStore, { optional: true });
  private readonly api = inject(ApiService);
  private readonly integrationService = inject(IntegrationService);
  private readonly localization = inject(LocalizationService);

  protected readonly heading = computed(() =>
    this.localization.translateKey(AppStrings.ActionBuilder.Picker.Heading));
  protected readonly allSourcesLabel = computed(() =>
    this.localization.translateKey(AppStrings.ActionBuilder.Picker.AllSources));
  protected readonly searchPlaceholder = computed(() =>
    this.localization.translateKey(AppStrings.ActionBuilder.Picker.SearchPlaceholder));
  protected readonly noResultsMessage = computed(() =>
    this.localization.translateKey(AppStrings.ActionBuilder.Picker.NoResults));

  @ViewChild(ModalComponent) private modal?: ModalComponent;

  private readonly definitionsInput = signal<ActionBlockDefinition[]>([]);
  readonly search = signal('');
  readonly category = signal<string | null>(null);
  readonly highlightIndex = signal(0);

  readonly categories = computed<PickerCategory[]>(() => {
    const map = new Map<string, PickerCategory>();
    for (const def of this.definitionsInput()) {
      const existing = map.get(def.category);
      if (existing) {
        existing.count++;
      } else {
        map.set(def.category, {
          name: def.category,
          iconUrl: this.iconUrlFor(def.integrationId),
          fallbackIcon: def.icon
            ?? ActionPickerComponent.CategoryGlyphs[def.categoryId ?? '']
            ?? 'icon-puzzle',
          count: 1,
        });
      }
    }
    return [...map.values()].sort((a, b) => a.name.localeCompare(b.name));
  });

  readonly filtered = computed(() => {
    const query = this.search().toLowerCase().trim();
    const category = this.category();
    const targetListId = this.store?.pickerOpenForList() ?? null;
    const insideLoop = targetListId
      ? isListInsideLoop(this.store!.flows(), targetListId)
      : false;
    return this.definitionsInput().filter(def => {
      if (category && def.category !== category) return false;
      if (query && !def.label.toLowerCase().includes(query)) return false;
      if (def.loopOnly && !insideLoop) return false;
      return true;
    });
  });

  glyphFor(def: ActionBlockDefinition): string {
    return def.icon ?? ActionPickerComponent.CategoryGlyphs[def.categoryId ?? ''] ?? 'icon-puzzle';
  }

  iconUrlFor(integrationId?: string): string | null {
    if (!integrationId) return null;
    const integration = this.integrationService.integrations().find(i => i.id === integrationId);
    return integration?.hasIcon ? this.api.getIntegrationIconUrl(integrationId, integration.iconVersion) : null;
  }

  colorClass(def: ActionBlockDefinition): string {
    return `action-color-${def.type}`;
  }

  pick(def: ActionBlockDefinition): void {
    dismissModal(this.modal, () => {
      this.picked.emit(def);
      this.store?.pickAction(def);
    });
  }

  protected onDismiss(): void {
    this.store?.closePicker();
    this.closed.emit();
  }

  onSearchChange(value: string): void {
    this.search.set(value);
    this.highlightIndex.set(0);
  }

  selectCategory(category: string | null): void {
    this.category.set(category);
    this.highlightIndex.set(0);
  }

  onSearchKeydown(event: KeyboardEvent): void {
    const items = this.filtered();
    if (items.length === 0) return;
    switch (event.key) {
      case 'ArrowDown':
        event.preventDefault();
        this.highlightIndex.update(i => Math.min(i + 1, items.length - 1));
        break;
      case 'ArrowUp':
        event.preventDefault();
        this.highlightIndex.update(i => Math.max(i - 1, 0));
        break;
      case 'Enter': {
        event.preventDefault();
        const def = items[this.highlightIndex()];
        if (def) this.pick(def);
        break;
      }
    }
  }
}
