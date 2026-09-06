import { Injectable, signal } from '@angular/core';

export const DEFAULT_BASIS = 120;

@Injectable({ providedIn: 'root' })
export class UiWidgetTreeContext {
  private readonly basisSignal = signal(DEFAULT_BASIS);
  readonly basis = this.basisSignal.asReadonly();

  setBasis(basis: number): void {
    if (Number.isFinite(basis) && basis > 0) this.basisSignal.set(basis);
  }

  private tileBorder = false;

  /** See {@link UiRenderHost.ownsRootWidgetBorder}: set by a surface that draws the root button's
   * ring itself, beside the transform-scaled content, so the button must not paint a second one. */
  readonly tileDrawsRootBorder = (): boolean => this.tileBorder;

  setTileDrawsRootBorder(owns: boolean): void {
    this.tileBorder = owns;
  }
}
