import { computed, provideZonelessChangeDetection, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { EMPTY, Observable } from 'rxjs';
import { UiRenderHost, UiResource } from '@macro-deck/runtime';

import { ThemeService } from '../../services/theme.service';
import { ApiService, HOST_URL_RESOLVER } from '../../transport';
import { UiNodeEventBus } from './ui-node-event-bus';
import { UiWidgetRenderHostFactory } from './ui-widget-render-host';
import { UiWidgetResourceBaseUrl } from './ui-widget-resource-base-url';

describe('UiWidgetRenderHostFactory resource urls', () => {
  const theme = signal<'light' | 'dark'>('dark');
  let host: UiRenderHost;

  beforeEach(async () => {
    theme.set('dark');
    const api = jasmine.createSpyObj<ApiService>('ApiService', ['getLocalization', 'onNotification']);
    api.onNotification.and.callFake(<T>(): Observable<T> => EMPTY);
    (api as unknown as { connectionStateSignal: () => string }).connectionStateSignal = () => 'disconnected';

    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        { provide: HOST_URL_RESOLVER, useValue: async () => 'http://host' },
        { provide: ApiService, useValue: api },
        { provide: ThemeService, useValue: { resolvedTheme: theme.asReadonly() } },
      ],
    });
    host = TestBed.inject(UiWidgetRenderHostFactory).bind(new UiNodeEventBus());
    await TestBed.inject(UiWidgetResourceBaseUrl).get();
  });

  it('asks again for an icon with appearances when the theme changes', () => {
    const icon: UiResource = { resourceId: 'app.macro-deck.widget-icon.icon-pack.1.a', contentHash: 'h' };
    const url = computed(() => host.resourceUrl(icon));

    const dark = url();
    theme.set('light');
    const light = url();

    expect(dark).toContain('colorScheme=dark');
    expect(light).toContain('colorScheme=light');
    expect(light).not.toBe(dark);
  });

  it('leaves the url of an icon without appearances untouched by the theme', () => {
    const icon: UiResource = { resourceId: 'app.macro-deck.widget-icon.icon-pack.1', contentHash: 'h' };

    expect(host.resourceUrl(icon)).toBe('http://host/api/ui/resources/app.macro-deck.widget-icon.icon-pack.1?v=h');
    theme.set('light');
    expect(host.resourceUrl(icon)).toBe('http://host/api/ui/resources/app.macro-deck.widget-icon.icon-pack.1?v=h');
  });
});
