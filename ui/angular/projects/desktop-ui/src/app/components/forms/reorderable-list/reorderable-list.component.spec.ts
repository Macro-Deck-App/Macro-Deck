import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';

import { ReorderableListComponent } from './reorderable-list.component';
import { provideLocalizationTesting } from '../../../../testing/localization-test-support';

describe('ReorderableListComponent', () => {
  let fixture: ComponentFixture<ReorderableListComponent>;
  let component: ReorderableListComponent;
  let emitted: string[][];

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [ReorderableListComponent],
      providers: [provideZonelessChangeDetection(), ...provideLocalizationTesting()],
    });
    fixture = TestBed.createComponent(ReorderableListComponent);
    component = fixture.componentInstance;
    component.options = [
      { value: 'cpu', label: 'CPU' },
      { value: 'gpu', label: 'GPU' },
      { value: 'ram', label: 'RAM' },
    ];
    component.value = ['cpu', 'gpu'];
    emitted = [];
    component.valueChange.subscribe(v => emitted.push(v));
  });

  function rowLabels(): string[] {
    fixture.changeDetectorRef.markForCheck();
    fixture.detectChanges();
    return Array.from(fixture.nativeElement.querySelectorAll('.rl-row') as NodeListOf<HTMLElement>)
      .map(row => row.textContent?.trim() ?? '');
  }

  it('shows the selected items in their bound order, then the unselected options', () => {
    component.value = ['gpu', 'cpu'];
    expect(rowLabels()).toEqual(['GPU', 'CPU', 'RAM']);
  });

  it('appends a newly checked option to the end of the order', () => {
    component.toggle('ram');
    expect(emitted).toEqual([['cpu', 'gpu', 'ram']]);
  });

  it('removes an unchecked item and keeps the order of the rest', () => {
    component.value = ['cpu', 'gpu', 'ram'];
    component.toggle('gpu');
    expect(emitted).toEqual([['cpu', 'ram']]);
  });

  it('moves an item down with its button', () => {
    rowLabels();
    (fixture.nativeElement.querySelector('.rl-down button') as HTMLButtonElement).click();
    expect(emitted).toEqual([['gpu', 'cpu']]);
  });

  it('appends an option checked through its checkbox', () => {
    rowLabels();
    const boxes = fixture.nativeElement.querySelectorAll('.rl-row input[type="checkbox"]') as NodeListOf<HTMLInputElement>;
    boxes[2].click();
    expect(emitted).toEqual([['cpu', 'gpu', 'ram']]);
  });

  it('reorders to where a dragged row is dropped', () => {
    component.value = ['cpu', 'gpu', 'ram'];
    component.onDrop({ previousIndex: 2, currentIndex: 0 } as never);
    expect(emitted).toEqual([['ram', 'cpu', 'gpu']]);
  });

  it('keeps a bound value that is no longer offered, labelled by its raw value', () => {
    component.value = ['gone', 'cpu'];
    expect(rowLabels()).toEqual(['gone', 'CPU', 'GPU', 'RAM']);

    component.toggle('gpu');
    expect(emitted).toEqual([['gone', 'cpu', 'gpu']]);
  });

  it('offers no move buttons that work while disabled', () => {
    component.disabled = true;
    rowLabels();
    const buttons = Array.from(fixture.nativeElement.querySelectorAll('.rl-up button, .rl-down button')) as HTMLButtonElement[];
    expect(buttons.length).toBeGreaterThan(0);
    expect(buttons.every(b => b.disabled)).toBeTrue();
  });
});
