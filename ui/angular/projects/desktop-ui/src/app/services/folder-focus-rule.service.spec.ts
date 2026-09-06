import { provideZonelessChangeDetection, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Observable, Subject } from 'rxjs';

import { FolderFocusRule } from '@macro-deck/runtime';
import { ApiService } from '@shared';
import { FolderFocusRuleService } from './folder-focus-rule.service';

function rule(ruleId: string, folderId: string, overrides: Partial<FolderFocusRule> = {}): FolderFocusRule {
  return {
    folderId,
    folderName: `Folder ${folderId}`,
    profileId: 'profile-1',
    ruleId,
    enabled: true,
    applicationIdentity: 'com.example.app',
    identityKind: 'BundleId',
    deviceId: 'device-1',
    deviceName: 'Living Room',
    deviceExists: true,
    returnOnFocusLoss: false,
    ...overrides,
  };
}

describe('FolderFocusRuleService', () => {
  let service: FolderFocusRuleService;
  let api: jasmine.SpyObj<ApiService>;
  let connectionState: ReturnType<typeof signal<string>>;
  let notifications: Map<string, Subject<unknown>>;

  function push(method: string, payload: unknown): void {
    notifications.get(method)?.next(payload);
  }

  beforeEach(() => {
    connectionState = signal<string>('disconnected');
    notifications = new Map();
    const apiSpy = jasmine.createSpyObj<ApiService>('ApiService', [
      'onNotification', 'getFolderFocusRules', 'setFolderFocusRule', 'deleteFolderFocusRule',
    ]);
    apiSpy.onNotification.and.callFake((method: string) => {
      let subject = notifications.get(method);
      if (!subject) {
        subject = new Subject<unknown>();
        notifications.set(method, subject);
      }
      return subject.asObservable() as Observable<never>;
    });
    apiSpy.getFolderFocusRules.and.resolveTo({ rules: [] });
    Object.defineProperty(apiSpy, 'connectionStateSignal', { value: connectionState });

    TestBed.configureTestingModule({
      providers: [provideZonelessChangeDetection(), { provide: ApiService, useValue: apiSpy }],
    });
    service = TestBed.inject(FolderFocusRuleService);
    api = apiSpy;
  });

  describe('cache repair', () => {
    it('pulls a fresh snapshot when the transport connects', async () => {
      api.getFolderFocusRules.and.resolveTo({ rules: [rule('r1', 'f1')] });

      connectionState.set('connected');
      await service.load();
      TestBed.tick();

      expect(api.getFolderFocusRules).toHaveBeenCalled();
      expect(service.rules().map(r => r.ruleId)).toEqual(['r1']);
    });

    it('pulls again on a reconnect, so rules missed while offline are repaired', async () => {
      connectionState.set('connected');
      TestBed.tick();
      await service.load();
      const afterFirst = api.getFolderFocusRules.calls.count();

      connectionState.set('disconnected');
      TestBed.tick();
      connectionState.set('connected');
      TestBed.tick();
      await service.load();

      expect(api.getFolderFocusRules.calls.count()).toBeGreaterThan(afterFirst);
    });

    it('collapses concurrent loads into one request', async () => {
      const before = api.getFolderFocusRules.calls.count();

      await Promise.all([service.load(), service.load(), service.load()]);

      expect(api.getFolderFocusRules.calls.count()).toBe(before + 1);
    });
  });

  describe('initial load', () => {
    it('populates the cache with the loaded rules', async () => {
      api.getFolderFocusRules.and.resolveTo({ rules: [rule('r1', 'f1'), rule('r2', 'f2')] });

      await service.load();

      expect(service.rules().map(r => r.ruleId)).toEqual(['r1', 'r2']);
      expect(service.rulesForFolder('f1').map(r => r.ruleId)).toEqual(['r1']);
      expect(service.rulesForFolder('f2').map(r => r.ruleId)).toEqual(['r2']);
    });
  });

  describe('push subscriptions', () => {
    it('replaces a folder\'s rules with the full set carried by FolderFocusRuleChangedEvent', () => {
      push('FolderFocusRuleChangedEvent', { folderId: 'f1', rules: [rule('r1', 'f1')] });

      expect(service.rulesForFolder('f1').map(r => r.ruleId)).toEqual(['r1']);

      push('FolderFocusRuleChangedEvent', {
        folderId: 'f1',
        rules: [rule('r1', 'f1', { enabled: false }), rule('r3', 'f1')],
      });

      const folderRules = service.rulesForFolder('f1');
      expect(folderRules.map(r => r.ruleId)).toEqual(['r1', 'r3']);
      expect(folderRules.find(r => r.ruleId === 'r1')?.enabled).toBeFalse();
    });

    it('leaves other folders\' rules untouched on a FolderFocusRuleChangedEvent', () => {
      push('FolderFocusRuleChangedEvent', { folderId: 'f1', rules: [rule('r1', 'f1')] });
      push('FolderFocusRuleChangedEvent', { folderId: 'f2', rules: [rule('r2', 'f2')] });

      expect(service.rules().map(r => r.ruleId).sort()).toEqual(['r1', 'r2']);
    });

    it('removes a rule on FolderFocusRuleRemovedEvent', () => {
      push('FolderFocusRuleChangedEvent', { folderId: 'f1', rules: [rule('r1', 'f1'), rule('r2', 'f1')] });

      push('FolderFocusRuleRemovedEvent', { folderId: 'f1', ruleId: 'r1' });

      expect(service.rulesForFolder('f1').map(r => r.ruleId)).toEqual(['r2']);
    });
  });

  describe('save', () => {
    it('upserts the returned rule on success', async () => {
      api.setFolderFocusRule.and.resolveTo({ success: true, rule: rule('r1', 'f1') });

      const result = await service.save({
        folderId: 'f1',
        enabled: true,
        applicationIdentity: 'com.example.app',
        identityKind: 'BundleId',
        deviceId: 'device-1',
        returnOnFocusLoss: false,
      });

      expect(result.success).toBeTrue();
      expect(service.rulesForFolder('f1').map(r => r.ruleId)).toEqual(['r1']);
    });

    it('surfaces a DuplicateFocusRule error without touching the cache', async () => {
      api.setFolderFocusRule.and.resolveTo({
        success: false,
        error: { code: 'DuplicateFocusRule', message: 'Folder "Home" already watches this application on this device.' },
      });

      const result = await service.save({
        folderId: 'f1',
        enabled: true,
        applicationIdentity: 'com.example.app',
        identityKind: 'BundleId',
        deviceId: 'device-1',
        returnOnFocusLoss: false,
      });

      expect(result.success).toBeFalse();
      expect(result.error?.code).toBe('DuplicateFocusRule');
      expect(service.rules()).toEqual([]);
    });

    it('returns a failure result when the request throws', async () => {
      api.setFolderFocusRule.and.rejectWith(new Error('offline'));

      const result = await service.save({
        folderId: 'f1',
        enabled: true,
        applicationIdentity: 'com.example.app',
        identityKind: 'BundleId',
        deviceId: 'device-1',
        returnOnFocusLoss: false,
      });

      expect(result.success).toBeFalse();
    });
  });

  describe('delete', () => {
    it('removes the rule locally on success', async () => {
      push('FolderFocusRuleChangedEvent', { folderId: 'f1', rules: [rule('r1', 'f1')] });
      api.deleteFolderFocusRule.and.resolveTo({ success: true });

      const result = await service.delete('f1', 'r1');

      expect(result.success).toBeTrue();
      expect(service.rulesForFolder('f1')).toEqual([]);
      expect(api.deleteFolderFocusRule).toHaveBeenCalledWith('f1', 'r1');
    });

    it('leaves the cache untouched on failure', async () => {
      push('FolderFocusRuleChangedEvent', { folderId: 'f1', rules: [rule('r1', 'f1')] });
      api.deleteFolderFocusRule.and.resolveTo({ success: false, error: { code: 'nope', message: 'nope' } });

      const result = await service.delete('f1', 'r1');

      expect(result.success).toBeFalse();
      expect(service.rulesForFolder('f1').map(r => r.ruleId)).toEqual(['r1']);
    });
  });
});
