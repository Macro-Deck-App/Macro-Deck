import { Pipe, PipeTransform, inject } from '@angular/core';
import { LocalizationService } from './localization.service';
import { LocalizedText, resolveLocalizedText } from '@macro-deck/runtime';

@Pipe({ name: 'localizedText', standalone: true, pure: false })
export class LocalizedTextPipe implements PipeTransform {
  private readonly localization = inject(LocalizationService);

  transform(value: LocalizedText | undefined): string {
    return resolveLocalizedText(value, this.localization);
  }
}
