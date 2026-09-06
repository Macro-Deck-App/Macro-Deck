import { Injectable, inject } from '@angular/core';
import { ApiService } from '../transport';

@Injectable({
  providedIn: 'root'
})
export class IconImageService {
  private readonly api = inject(ApiService);

  getIconUrl(iconId: string | null | undefined, size?: number): string | null {
    return iconId ? this.api.getIconImageUrl(iconId, size) : null;
  }
}
