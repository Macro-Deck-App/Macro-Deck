import { Injectable, effect, inject, signal, untracked } from '@angular/core';

import { AppStrings, resolveLocalizedText } from '@macro-deck/runtime';
import { ApiService } from '../transport';
import { LocalizationService } from '../localization';
import { ToastService } from './toast.service';

@Injectable({ providedIn: 'root' })
export class WidgetTypeFavoritesService {
  private readonly api = inject(ApiService);
  private readonly toasts = inject(ToastService);
  private readonly localization = inject(LocalizationService);

  private readonly favorites = signal<ReadonlySet<string>>(new Set());
  private generation = 0;

  readonly ids = this.favorites.asReadonly();

  constructor() {
    this.api.onWidgetTypeFavoritesChanged().subscribe(event => this.apply(event.typeIds ?? []));

    effect(() => {
      if (this.api.connectionStateSignal() === 'connected') {
        untracked(() => void this.load());
      }
    });
  }

  isFavorite(typeId: string): boolean {
    return this.favorites().has(typeId);
  }

  async toggle(typeId: string): Promise<void> {
    const favorite = !this.favorites().has(typeId);
    this.setLocally(typeId, favorite);
    const startedAt = this.generation;

    try {
      const response = await this.api.setWidgetTypeFavorite(typeId, favorite);
      const newest = startedAt === this.generation;
      if (response.success) {
        if (newest) this.apply(response.typeIds ?? []);
        return;
      }
      if (newest && response.typeIds) {
        this.apply(response.typeIds);
      } else {
        this.setLocally(typeId, !favorite);
      }
      this.reportFailure(response.error?.message);
    } catch (error) {
      console.error('Failed to save the widget favorite:', error);
      this.setLocally(typeId, !favorite);
      this.reportFailure();
    }
  }

  private async load(): Promise<void> {
    const startedAt = this.generation;
    try {
      const response = await this.api.getWidgetTypeFavorites();
      if (response.success && startedAt === this.generation) {
        this.apply(response.typeIds ?? []);
      }
    } catch (error) {
      console.error('Failed to load widget favorites:', error);
    }
  }

  private apply(typeIds: readonly string[]): void {
    this.generation++;
    this.favorites.set(new Set(typeIds));
  }

  private setLocally(typeId: string, favorite: boolean): void {
    this.generation++;
    this.favorites.update(current => {
      const next = new Set(current);
      if (favorite) {
        next.add(typeId);
      } else {
        next.delete(typeId);
      }
      return next;
    });
  }

  private reportFailure(detail?: string): void {
    this.toasts.show(this.localization.translateKey(AppStrings.Widgets.TypeSelector.FavoriteFailed), {
      variant: 'error',
      detail: resolveLocalizedText(detail, this.localization) || undefined,
    });
  }
}
