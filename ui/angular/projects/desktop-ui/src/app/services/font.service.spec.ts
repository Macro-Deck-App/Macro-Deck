import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { SystemFontFace } from '@macro-deck/runtime';
import { ApiService } from '@shared';
import { FontService } from './font.service';

function face(overrides: Partial<SystemFontFace>): SystemFontFace {
  return {
    faceId: 'x',
    family: 'Family',
    weight: 400,
    width: 5,
    slant: 'upright',
    styleName: 'Regular',
    remoteRenderable: true,
    ...overrides,
  };
}

function configure(faces: SystemFontFace[]): { service: FontService; api: jasmine.SpyObj<ApiService> } {
  const api = jasmine.createSpyObj<ApiService>('ApiService', ['getSystemFonts']);
  api.getSystemFonts.and.resolveTo({ faces });

  TestBed.configureTestingModule({
    providers: [provideZonelessChangeDetection(), { provide: ApiService, useValue: api }],
  });
  return { service: TestBed.inject(FontService), api };
}

describe('FontService picker (issue #457)', () => {
  it('offers no bold or italic style for a family that only has a weight-400 upright face', async () => {
    const { service } = configure([
      face({ faceId: 'inter-400-5-upright', family: 'Inter', weight: 400, slant: 'upright', styleName: 'Regular' }),
    ]);

    await service.loadSystemFonts();

    const faces = service.facesForFamily('Inter');
    expect(faces.length).toBe(1);
    expect(faces[0].styleName).toBe('Regular');
    expect(faces.some(f => f.weight >= 700)).toBeFalse();
    expect(faces.some(f => f.slant === 'italic')).toBeFalse();
  });

  it('never offers a face the host cannot actually serve', async () => {
    const { service } = configure([
      face({ faceId: 'pingfang-400-5-upright', family: 'PingFang HK', remoteRenderable: false }),
      face({ faceId: 'inter-400-5-upright', family: 'Inter', remoteRenderable: true }),
    ]);

    await service.loadSystemFonts();

    expect(service.families().map(f => f.family)).toEqual(['Inter']);
    expect(service.faceById('pingfang-400-5-upright')).toBeUndefined();
    expect(service.facesForFamily('PingFang HK')).toEqual([]);
  });

  it('groups faces by family and sorts each family by weight then slant', async () => {
    const { service } = configure([
      face({ faceId: 'a-700-5-italic', family: 'Acme', weight: 700, slant: 'italic', styleName: 'Bold Italic' }),
      face({ faceId: 'a-400-5-upright', family: 'Acme', weight: 400, slant: 'upright', styleName: 'Regular' }),
      face({ faceId: 'a-400-5-italic', family: 'Acme', weight: 400, slant: 'italic', styleName: 'Italic' }),
    ]);

    await service.loadSystemFonts();

    const acme = service.facesForFamily('Acme');
    expect(acme.map(f => f.styleName)).toEqual(['Regular', 'Italic', 'Bold Italic']);
  });

  it('resolves a face by id, and an unknown id resolves to undefined', async () => {
    const { service } = configure([
      face({ faceId: 'inter-400-5-upright', family: 'Inter', styleName: 'Regular' }),
    ]);

    await service.loadSystemFonts();

    expect(service.faceById('inter-400-5-upright')?.family).toBe('Inter');
    expect(service.faceById('does-not-exist')).toBeUndefined();
    expect(service.faceById(undefined)).toBeUndefined();
  });
});
