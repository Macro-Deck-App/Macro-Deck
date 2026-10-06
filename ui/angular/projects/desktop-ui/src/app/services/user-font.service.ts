import { Injectable, inject, signal } from '@angular/core';
import { UserFont, UserFontImportResult } from '@macro-deck/runtime';
import { ApiService } from '@shared';
import { FontLoaderService } from '../shared/services/font-loader.service';
import { FontService } from './font.service';

export interface UserFontImportOutcome {
  success: boolean;
  results: UserFontImportResult[];
}

@Injectable({ providedIn: 'root' })
export class UserFontService {
  private readonly api = inject(ApiService);
  private readonly systemFonts = inject(FontService);
  private readonly fontLoader = inject(FontLoaderService);

  readonly fonts = signal<UserFont[]>([]);
  readonly isLoading = signal(false);

  async load(): Promise<void> {
    this.isLoading.set(true);
    try {
      const response = await this.api.getUserFonts();
      this.fonts.set(response.fonts ?? []);
    } catch (error) {
      console.error('Failed to load imported fonts:', error);
    } finally {
      this.isLoading.set(false);
    }
  }

  async import(files: File[]): Promise<UserFontImportOutcome> {
    let response;
    try {
      response = await this.api.importUserFonts(files);
    } catch (error) {
      console.error('Failed to import fonts:', error);
      return { success: false, results: [] };
    }

    const results = response.results ?? [];
    if (!response.success) {
      return { success: false, results };
    }

    const imported = results.filter(result => result.status === 'Imported' && result.font);
    if (imported.length > 0) {
      this.fontLoader.evict(imported.map(result => result.font!.faceId));
      await Promise.all([this.load(), this.systemFonts.loadSystemFonts()]);
    }
    return { success: true, results };
  }

  async remove(fontId: string): Promise<boolean> {
    const font = this.fonts().find(candidate => candidate.fontId === fontId);
    try {
      const response = await this.api.deleteUserFont(fontId);
      if (!response.success) {
        return false;
      }
    } catch (error) {
      console.error('Failed to remove font:', error);
      return false;
    }

    this.fonts.update(fonts => fonts.filter(candidate => candidate.fontId !== fontId));
    if (font) {
      this.fontLoader.evict([font.faceId]);
    }
    await this.systemFonts.loadSystemFonts();
    return true;
  }
}
