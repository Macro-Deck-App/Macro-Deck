import { Pipe, PipeTransform, inject } from '@angular/core';
import { AppStringsKey, StringsKey } from '@macro-deck/runtime';
import { LocalizationService } from './localization.service';

export type LocalizationKey = StringsKey | AppStringsKey;

@Pipe({ name: 'translate', standalone: true, pure: false })
export class TranslatePipe implements PipeTransform {
  private readonly localization = inject(LocalizationService);

  transform(key: LocalizationKey, args?: Record<string, unknown>): string {
    return this.localization.translateKey(key, args);
  }
}
