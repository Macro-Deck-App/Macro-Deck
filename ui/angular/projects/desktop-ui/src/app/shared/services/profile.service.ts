import { Injectable, computed, inject, signal } from '@angular/core';
import {
  AppStrings,
  type IpcProfile,
  Profile,
  type ProfileCreatedEvent,
  type ProfileDeletedEvent,
  type ProfileUpdatedEvent,
  type Result,
} from '@macro-deck/runtime';
import { ApiService } from '../transport';
import { LocalizationService } from '../localization';

@Injectable({
  providedIn: 'root'
})
export class ProfileService {
  private readonly api = inject(ApiService);
  private readonly localization = inject(LocalizationService);

  readonly profiles = signal<Profile[]>([]);
  readonly selectedProfileId = signal<string | null>(null);
  readonly isLoading = signal(false);
  readonly loadError = signal<string | null>(null);

  readonly sortedProfiles = computed(() =>
    [...this.profiles()].sort((a, b) => a.order - b.order)
  );

  readonly selectedProfile = computed(() =>
    this.profiles().find(p => p.id === this.selectedProfileId()) ?? null
  );

  readonly isCurrentProfileLocked = computed(() => {
    const profile = this.selectedProfile();
    return !!profile && (profile.isVirtual || profile.layout.rowsLocked || profile.layout.columnsLocked);
  });

  readonly currentGridConstraint = computed(() => this.selectedProfile()?.layout.constraint ?? null);

  readonly currentLayoutCompatibility = computed(() => {
    const compatibility = this.selectedProfile()?.layout.compatibility ?? null;
    return compatibility && compatibility.status !== 'ok' ? compatibility : null;
  });

  constructor() {
    this.subscribeToEvents();
  }

  private subscribeToEvents(): void {
    this.api.onNotification<ProfileCreatedEvent>('ProfileCreatedEvent').subscribe(event => {
      if (event.profile) {
        const profile = this.mapIpcProfile(event.profile);
        this.profiles.update(profiles =>
          profiles.some(p => p.id === profile.id) ? profiles : [...profiles, profile]
        );
      }
    });

    this.api.onNotification<ProfileUpdatedEvent>('ProfileUpdatedEvent').subscribe(event => {
      if (event.profile) {
        const updated = this.mapIpcProfile(event.profile);
        this.profiles.update(profiles => profiles.map(p => p.id === updated.id ? updated : p));
      }
    });

    this.api.onNotification<ProfileDeletedEvent>('ProfileDeletedEvent').subscribe(event => {
      if (event.profileId) {
        this.removeFromState(event.profileId);
      }
    });
  }

  async loadProfiles(preferredProfileId?: string | null): Promise<void> {
    this.isLoading.set(true);
    this.loadError.set(null);

    try {
      const response = await this.api.getProfiles();
      const profiles = (response.profiles ?? []).map(p => this.mapIpcProfile(p));
      this.profiles.set(profiles);

      const sorted = [...profiles].sort((a, b) => a.order - b.order);
      const currentId = this.selectedProfileId();
      if ((!currentId || !profiles.some(p => p.id === currentId)) && sorted.length > 0) {
        const preferred = preferredProfileId && profiles.some(p => p.id === preferredProfileId)
          ? preferredProfileId
          : sorted[0].id;
        this.selectedProfileId.set(preferred);
      }
    } catch (error) {
      console.error('Failed to load profiles:', error);
      this.loadError.set(error instanceof Error ? error.message : this.localization.translateKey(AppStrings.Errors.Profile.LoadFailed));
    } finally {
      this.isLoading.set(false);
    }
  }

  selectProfile(id: string): void {
    if (this.selectedProfileId() !== id) {
      this.selectedProfileId.set(id);
    }
  }

  async createProfile(
    name: string,
    defaults?: {
      defaultRows?: number;
      defaultColumns?: number;
      defaultBackgroundColor?: string;
      defaultWidgetSpacing?: number;
      defaultWidgetBorderRadius?: number;
    }
  ): Promise<Result<Profile>> {
    try {
      const response = await this.api.createProfile({ name, ...defaults });
      if (!response.success) {
        return { success: false, error: response.error };
      }
      if (!response.profile) {
        return { success: false, error: { code: 'INTERNAL_ERROR', message: this.localization.translateKey(AppStrings.Errors.Profile.NoProfileReturned) } };
      }

      const profile = this.mapIpcProfile(response.profile);
      this.profiles.update(profiles =>
        profiles.some(p => p.id === profile.id) ? profiles : [...profiles, profile]
      );
      this.selectedProfileId.set(profile.id);
      return { success: true, data: profile };
    } catch (error) {
      console.error('Failed to create profile:', error);
      return {
        success: false,
        error: { code: 'INTERNAL_ERROR', message: error instanceof Error ? error.message : this.localization.translateKey(AppStrings.Errors.Profile.CreateFailed) }
      };
    }
  }

  async updateProfile(
    id: string,
    updates: {
      name?: string;
      order?: number;
      defaultRows?: number;
      defaultColumns?: number;
      defaultBackgroundColor?: string;
      defaultWidgetSpacing?: number;
      defaultWidgetBorderRadius?: number;
    }
  ): Promise<Result<Profile>> {
    try {
      const response = await this.api.updateProfile({ id, ...updates });
      if (!response.success) {
        return { success: false, error: response.error };
      }
      if (response.profile) {
        const profile = this.mapIpcProfile(response.profile);
        this.profiles.update(profiles => profiles.map(p => p.id === profile.id ? profile : p));
        return { success: true, data: profile };
      }
      return { success: true };
    } catch (error) {
      console.error('Failed to update profile:', error);
      return {
        success: false,
        error: { code: 'INTERNAL_ERROR', message: error instanceof Error ? error.message : this.localization.translateKey(AppStrings.Errors.Profile.UpdateFailed) }
      };
    }
  }

  async deleteProfile(id: string): Promise<Result> {
    try {
      const response = await this.api.deleteProfile({ id });
      if (!response.success) {
        return { success: false, error: response.error };
      }

      this.removeFromState(id);
      return { success: true };
    } catch (error) {
      console.error('Failed to delete profile:', error);
      return {
        success: false,
        error: { code: 'INTERNAL_ERROR', message: error instanceof Error ? error.message : this.localization.translateKey(AppStrings.Errors.Profile.DeleteFailed) }
      };
    }
  }

  private removeFromState(id: string): void {
    this.profiles.update(profiles => profiles.filter(p => p.id !== id));

    if (this.selectedProfileId() === id) {
      const remaining = this.sortedProfiles();
      this.selectedProfileId.set(remaining.length > 0 ? remaining[0].id : null);
    }
  }

  private mapIpcProfile(ipc: IpcProfile): Profile {
    return {
      id: ipc.id,
      name: ipc.name,
      order: ipc.order,
      layoutType: ipc.layoutType,
      isVirtual: ipc.isVirtual,
      sourceIntegrationId: ipc.sourceIntegrationId,
      layout: {
        rows: ipc.layout.rows,
        columns: ipc.layout.columns,
        rowsLocked: ipc.layout.rowsLocked,
        columnsLocked: ipc.layout.columnsLocked,
        constraint: ipc.layout.constraint,
        compatibility: ipc.layout.compatibility
      },
      defaultRows: ipc.defaultRows,
      defaultColumns: ipc.defaultColumns,
      defaultBackground: ipc.defaultBackgroundColor === 'rgba(18, 18, 18, 0.6)'
        ? null
        : (ipc.defaultBackgroundColor ?? null),
      defaultSpacing: ipc.defaultWidgetSpacing ?? null,
      defaultBorderRadius: ipc.defaultWidgetBorderRadius ?? null
    };
  }
}
