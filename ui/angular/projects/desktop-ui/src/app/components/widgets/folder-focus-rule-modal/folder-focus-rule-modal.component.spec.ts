import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';

import { Device, FolderFocusRule } from '@macro-deck/runtime';
import { ApiService } from '@shared';
import { DeviceService } from '../../../services/device.service';
import { FolderFocusRuleService } from '../../../services/folder-focus-rule.service';
import { FolderService } from '../../../services';
import { FolderFocusRuleModalComponent } from './folder-focus-rule-modal.component';
import { EMPTY } from 'rxjs';

function device(id: string, overrides: Partial<Device> = {}): Device {
  return {
    id,
    name: `Device ${id}`,
    nameIsCustom: false,
    clientType: 'native',
    formFactor: 'desktop',
    online: true,
    connectionCount: 1,
    hasActiveSession: true,
    lastSeenAt: '2026-08-03T00:00:00Z',
    createdAt: '2026-08-01T00:00:00Z',
    ...overrides,
  };
}

function rule(ruleId: string, overrides: Partial<FolderFocusRule> = {}): FolderFocusRule {
  return {
    folderId: 'f1',
    folderName: 'Home',
    profileId: 'p1',
    ruleId,
    enabled: true,
    applicationIdentity: 'com.example.app',
    identityKind: 'BundleId',
    deviceId: 'd1',
    deviceName: 'Living Room',
    deviceExists: true,
    returnOnFocusLoss: false,
    ...overrides,
  };
}

describe('FolderFocusRuleModalComponent', () => {
  let fixture: ComponentFixture<FolderFocusRuleModalComponent>;
  let apiSpy: jasmine.SpyObj<ApiService>;
  let focusRuleService: {
    rulesForFolder: jasmine.Spy;
    load: jasmine.Spy;
    save: jasmine.Spy;
    delete: jasmine.Spy;
  };

  function configure(rules: FolderFocusRule[], devices: Device[] = [device('d1', { name: 'Living Room' })]): void {
    apiSpy = jasmine.createSpyObj<ApiService>('ApiService', ['getApplicationFocusSupport', 'getRunningApplications', 'onNotification']);
    apiSpy.getApplicationFocusSupport.and.resolveTo({ supported: true, preferredIdentityKind: 'ExecutablePath' });
    apiSpy.getRunningApplications.and.resolveTo({ applications: [] });
    apiSpy.onNotification.and.returnValue(EMPTY);

    focusRuleService = {
      rulesForFolder: jasmine.createSpy('rulesForFolder').and.returnValue(rules),
      load: jasmine.createSpy('load').and.resolveTo(),
      save: jasmine.createSpy('save').and.resolveTo({ success: true, data: rule('new') }),
      delete: jasmine.createSpy('delete').and.resolveTo({ success: true }),
    };

    const deviceService = {
      devices: jasmine.createSpy('devices').and.returnValue(devices),
      load: jasmine.createSpy('load').and.resolveTo(),
    };

    TestBed.configureTestingModule({
      imports: [FolderFocusRuleModalComponent],
      providers: [
        provideZonelessChangeDetection(),
        { provide: ApiService, useValue: apiSpy },
        { provide: FolderFocusRuleService, useValue: focusRuleService },
        { provide: DeviceService, useValue: deviceService },
        { provide: FolderService, useValue: { getFolderById: () => ({ id: 'f1', name: 'Home' }) } },
      ],
    });
  }

  async function create(): Promise<ComponentFixture<FolderFocusRuleModalComponent>> {
    const f = TestBed.createComponent(FolderFocusRuleModalComponent);
    f.componentRef.setInput('folderId', 'f1');
    f.detectChanges();
    await f.whenStable();
    f.detectChanges();
    return f;
  }

  it('renders one row per rule of the folder', async () => {
    configure([rule('r1'), rule('r2', { applicationIdentity: 'com.other.app' })]);
    fixture = await create();

    const rows = fixture.nativeElement.querySelectorAll('.ffr-row-app') as NodeListOf<HTMLElement>;
    expect(Array.from(rows).map(el => el.textContent?.trim())).toEqual(['com.example.app', 'com.other.app']);
  });

  it('shows the unsupported-platform banner and disables editing', async () => {
    configure([rule('r1')]);
    apiSpy.getApplicationFocusSupport.and.resolveTo({
      supported: false,
      unsupportedReason: 'Focus watching needs Accessibility permission.',
      preferredIdentityKind: 'ExecutablePath',
    });
    fixture = await create();

    const banner = fixture.nativeElement.querySelector('shared-error-banner');
    expect(banner?.textContent).toContain('Focus watching needs Accessibility permission.');
    expect(fixture.componentInstance['editingDisabled']()).toBeTrue();
  });

  it('warns when a rule targets a device that no longer exists', async () => {
    configure([rule('r1', { deviceExists: false })]);
    fixture = await create();

    expect(fixture.nativeElement.querySelector('.ffr-warning')?.textContent).toContain('Unknown device');
  });

  it('saves the drafted rule with the expected payload', async () => {
    configure([]);
    fixture = await create();
    const component = fixture.componentInstance;

    component['startAdd']();
    component['onAppIdentityChange']('com.new.app');
    component['setDeviceId']('d1');
    component['setReturnOnFocusLoss'](true);
    await component['save']();

    expect(focusRuleService.save).toHaveBeenCalledWith({
      folderId: 'f1',
      ruleId: undefined,
      enabled: true,
      applicationIdentity: 'com.new.app',
      identityKind: 'ExecutablePath',
      deviceId: 'd1',
      returnOnFocusLoss: true,
    });
  });

  it('surfaces a DuplicateFocusRule error inline and stays in the form', async () => {
    configure([]);
    focusRuleService.save.and.resolveTo({
      success: false,
      error: {
        code: 'DuplicateFocusRule',
        message: 'Folder "Home" already watches this application on this device.',
      },
    });
    fixture = await create();
    const component = fixture.componentInstance;

    component['startAdd']();
    component['onAppIdentityChange']('com.new.app');
    component['setDeviceId']('d1');
    await component['save']();
    fixture.detectChanges();

    expect(component['saveError']()).toBe('Folder "Home" already watches this application on this device.');
    expect(component['editing']()).not.toBeNull();

    const banner = fixture.nativeElement.querySelector('.ffr-form shared-error-banner');
    expect(banner?.textContent).toContain('Folder "Home" already watches this application on this device.');
  });

  it('surfaces a DuplicateFocusRule error inline when re-enabling a rule fails', async () => {
    const existing = rule('r1', { enabled: false });
    configure([existing]);
    focusRuleService.save.and.resolveTo({
      success: false,
      error: {
        code: 'DuplicateFocusRule',
        message: 'Folder "Home" already watches this application on this device.',
      },
    });
    fixture = await create();
    const component = fixture.componentInstance;

    await component['toggleEnabled'](existing);
    fixture.detectChanges();

    expect(component['saveError']()).toBe('Folder "Home" already watches this application on this device.');

    const banner = fixture.nativeElement.querySelector('shared-error-banner');
    expect(banner?.textContent).toContain('Folder "Home" already watches this application on this device.');
  });
});
