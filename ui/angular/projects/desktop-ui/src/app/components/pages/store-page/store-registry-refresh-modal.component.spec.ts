import { provideZonelessChangeDetection, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { EMPTY } from 'rxjs';

import { AppStrings, StoreRegistryRefreshRunBody } from '@macro-deck/runtime';
import { ApiService, LocalizationService } from '@shared';
import { StoreRegistryRefreshModalComponent } from './store-registry-refresh-modal.component';

function run(overrides: Partial<StoreRegistryRefreshRunBody> = {}): StoreRegistryRefreshRunBody {
  return {
    hostInstanceId: 'host-a',
    id: 'run-1',
    revision: 1,
    trigger: 'Manual',
    state: 'Running',
    startedAt: '2026-09-15T10:00:00Z',
    filesCompleted: 0,
    filesTotal: 0,
    entries: [{ at: '2026-09-15T10:00:00Z', step: 'Started' }],
    ...overrides,
  };
}

describe('StoreRegistryRefreshModalComponent', () => {
  let fixture: ComponentFixture<StoreRegistryRefreshModalComponent>;

  function translate(key: string, params?: Record<string, unknown>): string {
    return TestBed.inject(LocalizationService).translateKey(key, params);
  }

  function text(): string {
    return (fixture.nativeElement as HTMLElement).textContent ?? '';
  }

  function show(value: StoreRegistryRefreshRunBody | null): void {
    fixture.componentRef.setInput('run', value);
    fixture.detectChanges();
  }

  beforeEach(() => {
    const api = jasmine.createSpyObj<ApiService>('ApiService', ['onNotification']);
    api.onNotification.and.returnValue(EMPTY);
    Object.defineProperty(api, 'connectionStateSignal', { value: signal('connected') });
    TestBed.configureTestingModule({
      imports: [StoreRegistryRefreshModalComponent],
      providers: [provideZonelessChangeDetection(), { provide: ApiService, useValue: api }],
    });
    fixture = TestBed.createComponent(StoreRegistryRefreshModalComponent);
  });

  it('reads a running refresh as a log of what it has done so far and how many files are in', () => {
    show(run({
      filesCompleted: 3,
      filesTotal: 12,
      entries: [
        { at: '2026-09-15T10:00:00Z', step: 'Started' },
        { at: '2026-09-15T10:00:01Z', step: 'FetchingManifest' },
        { at: '2026-09-15T10:00:02Z', step: 'DownloadingFiles', count: 12 },
      ],
    }));

    const progress = (fixture.nativeElement as HTMLElement).querySelector('[role="progressbar"]');
    expect(text()).toContain(translate(AppStrings.Store.RegistryRefresh.State.Running));
    expect(text()).toContain(translate(AppStrings.Store.RegistryRefresh.Step.Started));
    expect(text()).toContain(translate(AppStrings.Store.RegistryRefresh.Step.FetchingManifest));
    expect(text()).toContain(translate(AppStrings.Store.RegistryRefresh.Step.DownloadingFiles, { count: 12 }));
    expect(text()).toContain(translate(AppStrings.Store.RegistryRefresh.Progress, { completed: 3, total: 12 }));
    expect(progress?.getAttribute('aria-valuenow')).toBe('25');
  });

  it('says the registry is being updated and how long until the next try', () => {
    show(run({
      entries: [
        { at: '2026-09-15T10:00:00Z', step: 'Started' },
        { at: '2026-09-15T10:00:02Z', step: 'DownloadingFiles', count: 12 },
        { at: '2026-09-15T10:00:03Z', step: 'WaitingForRegistryUpdate', count: 30 },
      ],
    }));

    expect(text()).toContain(translate(AppStrings.Store.RegistryRefresh.Step.WaitingForRegistryUpdate, { count: 30 }));
    expect(text()).toContain(translate(AppStrings.Store.RegistryRefresh.State.Running));
  });

  it('says in words why a refresh failed and keeps the host detail beside it', () => {
    show(run({
      state: 'Failed',
      entries: [
        { at: '2026-09-15T10:00:00Z', step: 'Started' },
        { at: '2026-09-15T10:00:01Z', step: 'Failed', error: 'network_failure', detail: 'HTTP 503' },
      ],
    }));

    expect(text()).toContain(translate(AppStrings.Store.RegistryRefresh.State.Failed));
    expect(text()).toContain(translate(AppStrings.Store.RegistryRefresh.Step.Failed,
      { reason: translate(AppStrings.Store.RegistryRefresh.Error.NetworkFailure) }));
    expect(text()).toContain('HTTP 503');
    expect((fixture.nativeElement as HTMLElement).querySelector('[role="progressbar"]')).toBeNull();
  });

  it('tells an automatic refresh apart from one a person started', () => {
    show(run({ trigger: 'Scheduled' }));

    expect(text()).toContain(translate(AppStrings.Store.RegistryRefresh.Step.StartedScheduled));
  });

  it('already reads as refreshing before the host has described the run', () => {
    show(null);

    expect(text()).toContain(translate(AppStrings.Store.RegistryRefresh.State.Running));
  });
});
