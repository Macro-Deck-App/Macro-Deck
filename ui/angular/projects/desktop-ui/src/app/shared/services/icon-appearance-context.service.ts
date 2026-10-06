import { Injectable, Injector, inject, signal, untracked } from '@angular/core';
import { type IconAppearanceContext, iconAppearanceContext } from '@macro-deck/runtime';
import { ThemeService } from './theme.service';

const REDUCED_MOTION_QUERY = '(prefers-reduced-motion: reduce)';

@Injectable({ providedIn: 'root' })
export class IconAppearanceContextService {
  private readonly injector = inject(Injector);
  private readonly reducedMotion = signal(false);
  private theme: ThemeService | null = null;

  constructor() {
    if (typeof window.matchMedia !== 'function') {
      return;
    }

    const query = window.matchMedia(REDUCED_MOTION_QUERY);
    this.reducedMotion.set(query.matches);
    query.addEventListener?.('change', event => this.reducedMotion.set(event.matches));
  }

  current(): IconAppearanceContext {
    // Resolved on first use and outside the caller's reactive context: the theme service starts
    // effects of its own, which Angular refuses to create while a paint is tracking signals.
    this.theme ??= untracked(() => this.injector.get(ThemeService));
    return iconAppearanceContext(this.theme.resolvedTheme(), this.reducedMotion());
  }
}
