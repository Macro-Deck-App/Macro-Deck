import { Injectable, computed, inject, signal } from '@angular/core';
import { SystemFontFace } from '@macro-deck/runtime';
import { ApiService } from '@shared';

export interface FontFamily {
  family: string;
  faces: SystemFontFace[];
}

const SLANT_ORDER: Record<SystemFontFace['slant'], number> = { upright: 0, italic: 1, oblique: 2 };

function compareFaces(a: SystemFontFace, b: SystemFontFace): number {
  return a.weight - b.weight || SLANT_ORDER[a.slant] - SLANT_ORDER[b.slant];
}

@Injectable({
  providedIn: 'root'
})
export class FontService {
  private readonly api = inject(ApiService);

  private readonly faces = signal<SystemFontFace[]>([]);
  readonly isLoading = signal(false);

  readonly families = computed<FontFamily[]>(() => {
    const byFamily = new Map<string, SystemFontFace[]>();
    for (const face of this.faces()) {
      const list = byFamily.get(face.family);
      if (list) {
        list.push(face);
      } else {
        byFamily.set(face.family, [face]);
      }
    }
    return [...byFamily.entries()]
      .map(([family, faces]) => ({ family, faces: faces.slice().sort(compareFaces) }))
      .sort((a, b) => a.family.localeCompare(b.family));
  });

  async loadSystemFonts(): Promise<void> {
    this.isLoading.set(true);
    try {
      const response = await this.api.getSystemFonts();
      this.faces.set((response.faces ?? []).filter(face => face.remoteRenderable));
    } catch (error) {
      console.error('Failed to load system fonts:', error);
    } finally {
      this.isLoading.set(false);
    }
  }

  faceById(faceId: string | undefined): SystemFontFace | undefined {
    return faceId ? this.faces().find(face => face.faceId === faceId) : undefined;
  }

  facesForFamily(family: string | undefined): SystemFontFace[] {
    if (!family) {
      return [];
    }
    return this.families().find(f => f.family === family)?.faces ?? [];
  }
}
