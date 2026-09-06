import { CdkVirtualScrollViewport } from '@angular/cdk/scrolling';
import { provideZonelessChangeDetection, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { Subject } from 'rxjs';
import { ApiService } from '@shared';
import { IconModel } from '../../services/icon-pack.service';
import { IconGridComponent } from './icon-grid.component';

describe('IconGridComponent', () => {
  let fixture: ComponentFixture<IconGridComponent>;
  let component: IconGridComponent;

  function icons(count: number): IconModel[] {
    return Array.from({ length: count }, (_, i) => ({
      id: `icon-${i}`,
      packId: 'pack',
      name: `icon ${i}`,
      isAnimated: false,
      processingState: 'Ready' as const,
      availableSizes: [128],
    }));
  }

  beforeEach(() => {
    const apiSpy = jasmine.createSpyObj<ApiService>('ApiService', ['getIconImageUrl', 'onNotification']);
    apiSpy.getIconImageUrl.and.returnValue('http://host/icon.webp');
    apiSpy.onNotification.and.callFake(() => new Subject());
    Object.defineProperty(apiSpy, 'connectionStateSignal', { value: signal('disconnected') });

    TestBed.configureTestingModule({
      imports: [IconGridComponent],
      providers: [provideZonelessChangeDetection(), { provide: ApiService, useValue: apiSpy }],
    });
    fixture = TestBed.createComponent(IconGridComponent);
    component = fixture.componentInstance;
    fixture.nativeElement.style.width = '640px';
    fixture.nativeElement.style.height = '400px';
  });

  function setIcons(list: IconModel[]): void {
    fixture.componentRef.setInput('icons', list);
    component.ngOnChanges();
    fixture.detectChanges();
  }

  async function flushViewport(): Promise<void> {
    const viewport = fixture.debugElement
      .query(By.directive(CdkVirtualScrollViewport))
      .componentInstance as CdkVirtualScrollViewport;
    viewport.checkViewportSize();
    await new Promise(resolve => setTimeout(resolve, 20));
    fixture.detectChanges();
  }

  it('chunks icons into rows matching the container width', () => {
    setIcons(icons(20));
    (component as unknown as { containerWidth: { set(value: number): void } }).containerWidth.set(640);
    fixture.detectChanges();

    const rows = (component as unknown as { rows(): IconModel[][] }).rows();
    expect(rows[0].length).toBe(5);
    expect(rows.length).toBe(4);
  });

  it('always keeps at least one column on tiny containers', () => {
    setIcons(icons(3));
    (component as unknown as { containerWidth: { set(value: number): void } }).containerWidth.set(10);
    fixture.detectChanges();

    const rows = (component as unknown as { rows(): IconModel[][] }).rows();
    expect(rows.length).toBe(3);
    expect(rows[0].length).toBe(1);
  });

  it('virtualizes large lists: only a subset of 5000 icons is in the DOM', async () => {
    setIcons(icons(5000));
    (component as unknown as { containerWidth: { set(value: number): void } }).containerWidth.set(640);
    fixture.detectChanges();
    await flushViewport();

    const renderedTiles = fixture.nativeElement.querySelectorAll('shared-icon-tile').length;
    expect(renderedTiles).toBeGreaterThan(0);
    expect(renderedTiles).toBeLessThan(200);
  });

  it('emits iconClick when a tile is clicked', async () => {
    setIcons(icons(1));
    (component as unknown as { containerWidth: { set(value: number): void } }).containerWidth.set(640);
    fixture.detectChanges();
    await flushViewport();

    let clicked: { icon: IconModel; toggle: boolean; range: boolean } | null = null;
    component.iconClick.subscribe(event => (clicked = event));
    fixture.nativeElement.querySelector('shared-icon-tile button').click();

    expect(clicked).not.toBeNull();
    expect(clicked!.icon.id).toBe('icon-0');
    expect(clicked!.toggle).toBeFalse();
  });

  it('emits a toggle click when the multi-select checkbox is clicked', async () => {
    fixture.componentRef.setInput('selectable', true);
    fixture.componentRef.setInput('multiSelect', true);
    setIcons(icons(1));
    (component as unknown as { containerWidth: { set(value: number): void } }).containerWidth.set(640);
    fixture.detectChanges();
    await flushViewport();

    let clicked: { icon: IconModel; toggle: boolean } | null = null;
    component.iconClick.subscribe(event => (clicked = event));
    const checkbox = fixture.nativeElement.querySelector('shared-icon-tile .checkbox');
    expect(checkbox).not.toBeNull();
    checkbox.click();

    expect(clicked).not.toBeNull();
    expect(clicked!.toggle).toBeTrue();
  });
});
