import { Injectable, computed, effect, signal } from '@angular/core';

export type IntegrationStatusFilter = 'all' | 'enabled' | 'disabled';
export type IntegrationTypeFilter = 'all' | 'internal' | 'external';
export type IntegrationIssuesFilter = 'all' | 'has' | 'none';

const STORAGE_KEY = 'md.integrations.filters';

const STATUS_VALUES: readonly IntegrationStatusFilter[] = ['all', 'enabled', 'disabled'];
const TYPE_VALUES: readonly IntegrationTypeFilter[] = ['all', 'internal', 'external'];
const ISSUES_VALUES: readonly IntegrationIssuesFilter[] = ['all', 'has', 'none'];

@Injectable({ providedIn: 'root' })
export class IntegrationFilterService {
  readonly status = signal<IntegrationStatusFilter>('all');
  readonly type = signal<IntegrationTypeFilter>('all');
  readonly capabilities = signal<readonly string[]>([]);
  readonly issues = signal<IntegrationIssuesFilter>('all');

  readonly hasActiveFilters = computed(() =>
    this.status() !== 'all' ||
    this.type() !== 'all' ||
    this.issues() !== 'all' ||
    this.capabilities().length > 0);

  constructor() {
    this.restore();

    effect(() => {
      const state = {
        status: this.status(),
        type: this.type(),
        capabilities: [...this.capabilities()],
        issues: this.issues(),
      };
      try {
        localStorage.setItem(STORAGE_KEY, JSON.stringify(state));
      } catch {
      }
    });
  }

  toggleCapability(kind: string): void {
    this.capabilities.update(kinds =>
      kinds.includes(kind) ? kinds.filter(k => k !== kind) : [...kinds, kind]);
  }

  clear(): void {
    this.status.set('all');
    this.type.set('all');
    this.capabilities.set([]);
    this.issues.set('all');
  }

  private restore(): void {
    let stored: unknown;
    try {
      const raw = localStorage.getItem(STORAGE_KEY);
      if (!raw) {
        return;
      }
      stored = JSON.parse(raw);
    } catch {
      return;
    }

    if (typeof stored !== 'object' || stored === null) {
      return;
    }

    const state = stored as Record<string, unknown>;
    this.status.set(pick(state['status'], STATUS_VALUES));
    this.type.set(pick(state['type'], TYPE_VALUES));
    this.issues.set(pick(state['issues'], ISSUES_VALUES));

    const capabilities = state['capabilities'];
    this.capabilities.set(Array.isArray(capabilities)
      ? capabilities.filter((kind): kind is string => typeof kind === 'string')
      : []);
  }
}

function pick<T extends string>(value: unknown, allowed: readonly T[]): T {
  return allowed.includes(value as T) ? value as T : allowed[0];
}
