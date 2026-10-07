import { Injectable, computed, inject, signal } from '@angular/core';
import {
  AppStrings,
  emptyCellStyleFromWire,
  type IpcProfile,
  type IpcProfilePlacement,
  Profile,
  type ProfileCreatedEvent,
  type ProfileDeletedEvent,
  type ProfilesReorderedEvent,
  type ProfileUpdatedEvent,
  type Result,
} from '@macro-deck/runtime';
import { ApiService } from '../transport';
import { LocalizationService } from '../localization';

type ProfileMovePosition = 'before' | 'after';

function compareProfiles(a: Profile, b: Profile): number {
  if (a.isVirtual !== b.isVirtual) {
    return a.isVirtual ? 1 : -1;
  }
  if (a.order !== b.order) {
    return a.order - b.order;
  }
  const left = a.name.toUpperCase();
  const right = b.name.toUpperCase();
  if (left !== right) {
    return left < right ? -1 : 1;
  }
  return a.id < b.id ? -1 : a.id > b.id ? 1 : 0;
}

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
    [...this.profiles()].sort(compareProfiles)
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

    this.api.onNotification<ProfilesReorderedEvent>('ProfilesReorderedEvent').subscribe(event => {
      this.applyPlacements(event.profiles ?? []);
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

      const sorted = [...profiles].sort(compareProfiles);
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
      defaultEmptyCellStyle?: string;
      defaultWidgetShadows?: boolean;
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
      defaultBackgroundColorSource?: string;
      defaultWidgetSpacing?: number;
      defaultWidgetBorderRadius?: number;
      defaultEmptyCellStyle?: string;
      defaultWidgetShadows?: boolean;
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

  async moveProfile(id: string, targetId: string, position: ProfileMovePosition): Promise<Result> {
    const previous = this.profiles();
    const ordered = this.sortedProfiles().filter(p => !p.isVirtual);
    const moving = ordered.find(p => p.id === id);
    if (!moving || id === targetId || !ordered.some(p => p.id === targetId)) {
      return { success: false, error: { code: 'VALIDATION_ERROR', message: this.localization.translateKey(AppStrings.Errors.Profile.MoveFailed) } };
    }

    const reordered = ordered.filter(p => p.id !== id);
    const targetIndex = reordered.findIndex(p => p.id === targetId);
    reordered.splice(position === 'after' ? targetIndex + 1 : targetIndex, 0, moving);
    this.applyPlacements(reordered.map((profile, order) => ({ id: profile.id, order })));

    try {
      const response = await this.api.moveProfile({ id, targetId, position });
      if (!response.success) {
        this.profiles.set(previous);
        return { success: false, error: response.error };
      }
      this.applyPlacements(response.profiles ?? []);
      return { success: true };
    } catch (error) {
      console.error('Failed to move profile:', error);
      this.profiles.set(previous);
      return {
        success: false,
        error: { code: 'INTERNAL_ERROR', message: this.localization.translateKey(AppStrings.Errors.Profile.MoveFailed) }
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

  async duplicateProfile(id: string, name: string): Promise<Result<Profile>> {
    try {
      const response = await this.api.duplicateProfile({ id, name });
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
      console.error('Failed to duplicate profile:', error);
      return {
        success: false,
        error: { code: 'INTERNAL_ERROR', message: this.localization.translateKey(AppStrings.Errors.Profile.DuplicateFailed) }
      };
    }
  }

  private applyPlacements(placements: IpcProfilePlacement[]): void {
    if (placements.length === 0) {
      return;
    }
    const orders = new Map(placements.map(placement => [placement.id, placement.order]));
    this.profiles.update(profiles => profiles.map(profile => {
      const order = orders.get(profile.id);
      return order === undefined || order === profile.order ? profile : { ...profile, order };
    }));
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
      ...(ipc.defaultBackgroundColorSource ? { defaultBackgroundSource: ipc.defaultBackgroundColorSource } : {}),
      defaultSpacing: ipc.defaultWidgetSpacing ?? null,
      defaultBorderRadius: ipc.defaultWidgetBorderRadius ?? null,
      defaultEmptyCellStyle: emptyCellStyleFromWire(ipc.defaultEmptyCellStyle),
      defaultShadows: ipc.defaultWidgetShadows ?? null
    };
  }
}
