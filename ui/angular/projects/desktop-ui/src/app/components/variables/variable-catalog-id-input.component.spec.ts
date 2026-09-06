import { provideZonelessChangeDetection, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Subject } from 'rxjs';
import { ApiService } from '@shared';
import { VariableCatalogIdInputComponent } from './variable-catalog-id-input.component';

describe('VariableCatalogIdInputComponent', () => {
  let fixture: ComponentFixture<VariableCatalogIdInputComponent>;
  let component: VariableCatalogIdInputComponent;
  let resolveSpy: jasmine.Spy;
  let bindSpy: jasmine.Spy;

  beforeEach(async () => {
    const apiSpy = jasmine.createSpyObj<ApiService>('ApiService', [
      'resolveCatalogVariable',
      'bindCatalogVariable',
      'getVariables',
      'onNotification',
    ]);
    apiSpy.getVariables.and.resolveTo({ variables: [] });
    apiSpy.onNotification.and.callFake(() => new Subject());
    Object.defineProperty(apiSpy, 'connectionStateSignal', { value: signal('disconnected') });
    resolveSpy = apiSpy.resolveCatalogVariable;
    bindSpy = apiSpy.bindCatalogVariable;

    TestBed.configureTestingModule({
      imports: [VariableCatalogIdInputComponent],
      providers: [provideZonelessChangeDetection(), { provide: ApiService, useValue: apiSpy }],
    });

    fixture = TestBed.createComponent(VariableCatalogIdInputComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('integrationId', 'foobar2000');
    fixture.detectChanges();
    await fixture.whenStable();
  });

  it('shows the resolved name/type and a Bind action once the provider recognizes the id', async () => {
    resolveSpy.and.resolveTo({
      node: { id: 'tag:artist', name: 'tag:artist', displayName: 'Artist tag', hasChildren: false, type: 'text' },
    });

    component.onIdInput('tag:artist');
    await component.resolve();
    await fixture.whenStable();

    expect(component.resolved()).not.toBeNull();
    expect(component.resolvedName()).toBe('Artist tag');
    expect(component.failed()).toBeFalse();
  });

  it('shows a clear failure message and never binds when the provider does not recognize the id', async () => {
    resolveSpy.and.resolveTo({ error: { code: 'not_found', message: 'unknown resource' } });

    component.onIdInput('does-not-exist');
    await component.resolve();
    await fixture.whenStable();

    expect(component.failed()).toBeTrue();
    expect(component.resolved()).toBeNull();
    expect(bindSpy).not.toHaveBeenCalled();

    fixture.detectChanges();
    const html = fixture.nativeElement as HTMLElement;
    expect(html.textContent).toContain('doesn\'t recognize that ID');
    expect(html.querySelector('shared-variable-bind-dialog')).toBeNull();
  });

  it('refuses to offer Bind for a resolved resource the caller cannot use', async () => {
    // Same gate as the browse tree's: a typed id must clear the caller's restrictions before it is
    // materialized, or the manual entry point becomes the way around them.
    resolveSpy.and.resolveTo({
      node: { id: 'tag:artist', name: 'tag:artist', displayName: 'Artist tag', hasChildren: false, type: 'text' },
    });
    fixture.componentRef.setInput('acceptedTypes', ['numeric']);

    component.onIdInput('tag:artist');
    await component.resolve();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(component.resolved()).not.toBeNull();
    expect(component.canBind()).toBeFalse();
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('cannot be used here');
    expect(bindSpy).not.toHaveBeenCalled();
  });

  it('refuses to offer Bind for a read-only resource when the caller needs to write', async () => {
    resolveSpy.and.resolveTo({
      node: { id: 'tag:rating', name: 'tag:rating', displayName: 'Rating', hasChildren: false, type: 'numeric' },
    });
    fixture.componentRef.setInput('acceptedTypes', ['numeric']);
    fixture.componentRef.setInput('writableOnly', true);

    component.onIdInput('tag:rating');
    await component.resolve();
    await fixture.whenStable();

    expect(component.canBind()).toBeFalse();

    resolveSpy.and.resolveTo({
      node: {
        id: 'tag:volume', name: 'tag:volume', displayName: 'Volume', hasChildren: false,
        type: 'numeric', canWrite: true,
      },
    });
    component.onIdInput('tag:volume');
    await component.resolve();
    await fixture.whenStable();

    expect(component.canBind()).toBeTrue();
  });

  it('clears a stale result as soon as the id is edited again', async () => {
    resolveSpy.and.resolveTo({
      node: { id: 'tag:artist', name: 'tag:artist', displayName: 'Artist tag', hasChildren: false, type: 'text' },
    });
    component.onIdInput('tag:artist');
    await component.resolve();
    await fixture.whenStable();
    expect(component.resolved()).not.toBeNull();

    component.onIdInput('tag:artist2');

    expect(component.resolved()).toBeNull();
    expect(component.failed()).toBeFalse();
  });
});
