import { Component, provideZonelessChangeDetection, ChangeDetectionStrategy } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { RailItemComponent } from './rail-item.component';
import { RailPageComponent } from './rail-page.component';

@Component({
  standalone: true,
  imports: [RailPageComponent, RailItemComponent],
  changeDetection: ChangeDetectionStrategy.Eager,
  template: `
    <shared-rail-page railTitle="Test Title">
      <ng-container ngProjectAs="[page-banner]">
        <div class="test-banner">banner</div>
      </ng-container>
      <ng-container ngProjectAs="[rail-actions]">
        <button type="button" class="test-action">New</button>
      </ng-container>
      <ul ngProjectAs="[rail]" class="test-list">
        <li>
          <shared-rail-item label="First" [count]="3" [active]="true" (click)="clicks = clicks + 1">
            <span class="test-badge">badge</span>
          </shared-rail-item>
        </li>
        <li>
          <shared-rail-item label="Second" [active]="false" />
        </li>
      </ul>
      <div class="test-detail">detail</div>
    </shared-rail-page>
  `,
})
class HostComponent {
  clicks = 0;
}

describe('RailPageComponent', () => {
  let fixture: ComponentFixture<HostComponent>;
  let element: HTMLElement;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [HostComponent],
      providers: [provideZonelessChangeDetection()],
    });
    fixture = TestBed.createComponent(HostComponent);
    element = fixture.nativeElement;
    fixture.detectChanges();
  });

  it('renders the rail title as the page heading', () => {
    expect(element.querySelector('.rail-header h1')?.textContent).toBe('Test Title');
  });

  it('projects banner, rail actions, rail body and detail content into their slots', () => {
    expect(element.querySelector('shared-rail-page > .test-banner')).toBeTruthy();
    expect(element.querySelector('.rail-header .test-action')).toBeTruthy();
    expect(element.querySelector('.rail > .test-list')).toBeTruthy();
    expect(element.querySelector('.detail > .test-detail')).toBeTruthy();
  });

  it('renders rail items with label, count, active state and projected badges', () => {
    const items = Array.from(element.querySelectorAll<HTMLButtonElement>('.rail-item'));
    expect(items.length).toBe(2);

    expect(items[0].querySelector('.rail-item-name')?.textContent).toBe('First');
    expect(items[0].querySelector('.rail-item-count')?.textContent).toBe('3');
    expect(items[0].classList.contains('active')).toBeTrue();
    expect(items[0].getAttribute('aria-selected')).toBe('true');
    expect(items[0].querySelector('.test-badge')).toBeTruthy();

    expect(items[1].querySelector('.rail-item-count')).toBeNull();
    expect(items[1].classList.contains('active')).toBeFalse();
    expect(items[1].getAttribute('aria-selected')).toBe('false');
  });

  it('bubbles rail item clicks to the host binding', () => {
    element.querySelectorAll<HTMLButtonElement>('.rail-item')[0].click();
    expect(fixture.componentInstance.clicks).toBe(1);
  });
});
