import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection, signal } from '@angular/core';
import { Subject } from 'rxjs';

import { ApiService, ThemeService } from '@shared';
import { ActionBuilderComponent } from './action-builder.component';
import { measureToolbarNaturalWidth } from './action-builder-layout';

function fakeApiService(): ApiService {
  const apiSpy = jasmine.createSpyObj<ApiService>('ApiService', ['onNotification']);
  apiSpy.onNotification.and.callFake(() => new Subject());
  Object.defineProperty(apiSpy, 'connectionStateSignal', { value: signal('disconnected') });
  return apiSpy;
}

describe('ActionBuilderComponent toolbar layout', () => {
  let fixture: ComponentFixture<ActionBuilderComponent>;
  let component: ActionBuilderComponent;

  const query = (selector: string): HTMLElement | null => fixture.nativeElement.querySelector(selector);
  const tabRow = (): HTMLElement => query('.tab-row')!;
  const host = (): HTMLElement => fixture.nativeElement as HTMLElement;

  const atWidth = (width: number): void => {
    host().style.width = `${width}px`;
    component.measureToolbar();
    fixture.detectChanges();
  };

  const naturalWidth = (): number => measureToolbarNaturalWidth(tabRow());

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ActionBuilderComponent],
      providers: [provideZonelessChangeDetection(), { provide: ApiService, useValue: fakeApiService() }],
    }).compileComponents();

    fixture = TestBed.createComponent(ActionBuilderComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('auxTabId', 'stateBinding');
    fixture.componentRef.setInput('auxTabLabel', 'Sync State');
    fixture.componentRef.setInput('showToggleTriggers', true);
    host().style.display = 'block';
    fixture.detectChanges();
  });

  afterEach(() => {
    host().style.removeProperty('width');
    host().style.removeProperty('display');
  });

  it('groups the row into exactly two groups rather than a flat list of controls', () => {
    const children = Array.from(tabRow().children);

    expect(children.length).toBe(2);
    expect(children.every(child => child.classList.contains('tab-group'))).toBeTrue();
    expect(children[0].classList.contains('tab-group--triggers')).toBeTrue();
    expect(children[1].classList.contains('tab-group--utility')).toBeTrue();
  });

  it('keeps the divider inside the utility group, as its first child, so it cannot be stranded', () => {
    const divider = query('.aux-tab-divider')!;

    expect(divider.parentElement?.classList.contains('tab-group--utility')).toBeTrue();
    expect(divider.previousElementSibling).toBeNull();
  });

  it('pins the utility group, not the Run button itself, to the far end of the row', () => {
    atWidth(naturalWidth() + 200);

    expect(getComputedStyle(query('.run-flow')!).marginInlineStart).toBe('0px');
    const utility = query('.tab-group--utility')!;
    expect(utility.getBoundingClientRect().right).toBeCloseTo(tabRow().getBoundingClientRect().right, 0);
    expect(utility.lastElementChild).toBe(query('.run-flow'));
  });

  it('stays on one row for as long as the content actually fits', () => {
    atWidth(naturalWidth() + 40);

    const styles = getComputedStyle(tabRow());
    expect(styles.flexWrap).toBe('nowrap');
    expect(styles.flexDirection).toBe('row');
    expect(getComputedStyle(query('.aux-tab-divider')!).display).not.toBe('none');
  });

  it('breaks into a deliberate two-row layout once it genuinely does not fit', () => {
    atWidth(naturalWidth() - 80);

    const styles = getComputedStyle(tabRow());
    expect(styles.flexWrap).toBe('nowrap');
    expect(styles.flexDirection).toBe('column');
    expect(getComputedStyle(query('.aux-tab-divider')!).display).toBe('none');
  });

  it('keeps every control reachable and unclipped across the supported width range', () => {
    const natural = naturalWidth();

    for (const width of [natural + 300, natural + 20, natural - 20, natural - 200, 320]) {
      atWidth(width);

      expect(query('.tab-group--triggers')).withContext(`${width}px`).not.toBeNull();
      expect(query('.aux-tab-divider')).withContext(`${width}px`).not.toBeNull();
      expect(query('.run-flow')).withContext(`${width}px`).not.toBeNull();
      expect(query('.add-trigger-button')).withContext(`${width}px`).not.toBeNull();
      expect(tabRow().scrollWidth).withContext(`${width}px`).toBeLessThanOrEqual(tabRow().clientWidth + 1);
    }
  });

  it('renders the "+" as a child of .tab-group--triggers, and the row still has exactly two groups', () => {
    const triggers = query('.tab-group--triggers')!;
    const addTrigger = query('.add-trigger')!;

    expect(addTrigger.parentElement).toBe(triggers);
    expect(Array.from(tabRow().children).length).toBe(2);
  });

  it('changes the measured natural width once the last trigger is added and the "+" disappears', () => {
    const before = naturalWidth();

    fixture.componentRef.setInput('flows', [
      { triggerId: 'onShortPress', triggerType: 'onShortPress', children: [] },
      { triggerId: 'onLongPress', triggerType: 'onLongPress', children: [] },
      { triggerId: 'onTouchStart', triggerType: 'onTouchStart', children: [] },
      { triggerId: 'onTouchEnd', triggerType: 'onTouchEnd', children: [] },
    ]);
    fixture.detectChanges();

    expect(component.showAddTrigger()).toBeFalse();
    expect(query('.add-trigger')).toBeNull();
    expect(naturalWidth()).not.toBe(before);
  });

  it('still breaks to two rows at its own natural width, reserved "×" allowance included', () => {
    atWidth(naturalWidth() - 80);

    const styles = getComputedStyle(tabRow());
    expect(styles.flexDirection).toBe('column');
  });

  it('measures the toolbar again when the global UI font changes', async () => {
    const measure = spyOn(component, 'measureToolbar').and.callThrough();

    TestBed.inject(ThemeService).uiFontVersion.update(version => version + 1);
    TestBed.tick();
    fixture.detectChanges();
    await fixture.whenStable();

    expect(measure).toHaveBeenCalled();
  });

  it('does not oscillate: the measured need is the same in either layout', () => {
    atWidth(naturalWidth() + 200);
    const whileOneRow = naturalWidth();

    atWidth(whileOneRow - 80);
    expect(getComputedStyle(tabRow()).flexDirection).toBe('column');
    expect(Math.abs(naturalWidth() - whileOneRow)).toBeLessThanOrEqual(4);
  });
});
