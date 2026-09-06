import { TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';

import { IntegrationFilterService } from './integration-filter.service';

const STORAGE_KEY = 'md.integrations.filters';

describe('IntegrationFilterService', () => {
  function create(): IntegrationFilterService {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({ providers: [provideZonelessChangeDetection()] });
    return TestBed.inject(IntegrationFilterService);
  }

  beforeEach(() => localStorage.removeItem(STORAGE_KEY));
  afterEach(() => localStorage.removeItem(STORAGE_KEY));

  it('starts unfiltered when nothing was ever persisted', () => {
    const service = create();

    expect(service.status()).toBe('all');
    expect(service.type()).toBe('all');
    expect(service.issues()).toBe('all');
    expect(service.capabilities()).toEqual([]);
    expect(service.hasActiveFilters()).toBeFalse();
  });

  it('restores the selection a previous run left behind', () => {
    const previous = create();
    previous.status.set('enabled');
    previous.type.set('external');
    previous.issues.set('has');
    previous.toggleCapability('weather');
    TestBed.tick();

    const restored = create();

    expect(restored.status()).toBe('enabled');
    expect(restored.type()).toBe('external');
    expect(restored.issues()).toBe('has');
    expect(restored.capabilities()).toEqual(['weather']);
  });

  it('falls back to the unfiltered defaults for a payload it cannot read', () => {
    localStorage.setItem(STORAGE_KEY, '{ not json');

    const service = create();

    expect(service.status()).toBe('all');
    expect(service.capabilities()).toEqual([]);
  });

  it('drops values that are not selectable instead of filtering by them', () => {
    localStorage.setItem(STORAGE_KEY, JSON.stringify({
      status: 'archived',
      type: 'external',
      issues: 7,
      capabilities: ['weather', 42],
    }));

    const service = create();

    expect(service.status()).toBe('all');
    expect(service.issues()).toBe('all');
    expect(service.type()).toBe('external');
    expect(service.capabilities()).toEqual(['weather']);
  });

  it('clears every facet at once', () => {
    const service = create();
    service.status.set('disabled');
    service.toggleCapability('actions');

    service.clear();

    expect(service.status()).toBe('all');
    expect(service.capabilities()).toEqual([]);
    expect(service.hasActiveFilters()).toBeFalse();
  });

  it('toggles a capability off again on a second selection', () => {
    const service = create();

    service.toggleCapability('events');
    service.toggleCapability('actions');
    service.toggleCapability('events');

    expect(service.capabilities()).toEqual(['actions']);
  });
});
