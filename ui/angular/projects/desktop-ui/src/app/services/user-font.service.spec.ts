import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { SystemFontFace, UserFont } from '@macro-deck/runtime';
import { ApiService } from '@shared';
import {
  FONT_FACE_BACKEND,
  FontFaceBackend,
  FontFaceHandle,
  FontLoaderService,
} from '../shared/services/font-loader.service';
import { FontService } from './font.service';
import { UserFontService } from './user-font.service';

function userFont(overrides: Partial<UserFont> = {}): UserFont {
  return {
    fontId: 'brand-regular',
    faceId: 'brand-sans-400-5-upright',
    family: 'Brand Sans',
    styleName: 'Regular',
    weight: 400,
    width: 5,
    slant: 'upright',
    format: 'ttf',
    sizeBytes: 1024,
    ...overrides,
  };
}

function systemFace(font: UserFont): SystemFontFace {
  return {
    faceId: font.faceId,
    family: font.family,
    weight: font.weight,
    width: font.width,
    slant: font.slant,
    styleName: font.styleName,
    remoteRenderable: true,
    userImported: true,
  };
}

class RecordingBackend implements FontFaceBackend {
  readonly added: FontFaceHandle[] = [];
  readonly deleted: FontFaceHandle[] = [];

  create(): FontFaceHandle {
    const handle: FontFaceHandle = { load: () => Promise.resolve(handle) };
    return handle;
  }

  add(face: FontFaceHandle): void {
    this.added.push(face);
  }

  delete(face: FontFaceHandle): void {
    this.deleted.push(face);
  }
}

describe('UserFontService', () => {
  let api: jasmine.SpyObj<ApiService>;
  let backend: RecordingBackend;
  let service: UserFontService;

  beforeEach(() => {
    api = jasmine.createSpyObj<ApiService>('ApiService', [
      'getUserFonts',
      'importUserFonts',
      'deleteUserFont',
      'getSystemFonts',
      'getFontFileUrl',
    ]);
    api.getUserFonts.and.resolveTo({ fonts: [] });
    api.getSystemFonts.and.resolveTo({ faces: [] });
    api.getFontFileUrl.and.callFake(faceId => `http://host/api/system/fonts/${faceId}/file`);
    backend = new RecordingBackend();

    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        { provide: ApiService, useValue: api },
        { provide: FONT_FACE_BACKEND, useValue: backend },
      ],
    });
    service = TestBed.inject(UserFontService);
  });

  it('makes a newly imported font available to the font pickers', async () => {
    const font = userFont();
    api.importUserFonts.and.resolveTo({
      success: true,
      results: [{ fileName: 'BrandSans.ttf', status: 'Imported', font }],
    });
    api.getUserFonts.and.resolveTo({ fonts: [font] });
    api.getSystemFonts.and.resolveTo({ faces: [systemFace(font)] });
    const file = new File(['x'], 'BrandSans.ttf');

    const outcome = await service.import([file]);

    expect(api.importUserFonts).toHaveBeenCalledWith([file]);
    expect(outcome.success).toBeTrue();
    expect(service.fonts()).toEqual([font]);
    expect(TestBed.inject(FontService).faceById(font.faceId)?.family).toBe('Brand Sans');
  });

  it('reports every rejected file and leaves the font list alone when nothing was imported', async () => {
    api.importUserFonts.and.resolveTo({
      success: true,
      results: [{ fileName: 'notes.txt', status: 'UnsupportedFormat', font: null }],
    });

    const outcome = await service.import([new File(['x'], 'notes.txt')]);

    expect(outcome.results.map(result => result.status)).toEqual(['UnsupportedFormat']);
    expect(api.getSystemFonts).not.toHaveBeenCalled();
  });

  it('reports a failed upload as a failure', async () => {
    api.importUserFonts.and.rejectWith(new Error('offline'));

    const outcome = await service.import([new File(['x'], 'BrandSans.ttf')]);

    expect(outcome.success).toBeFalse();
  });

  it('removes a font, unloads its face and refreshes the pickers', async () => {
    const font = userFont();
    api.getUserFonts.and.resolveTo({ fonts: [font] });
    api.deleteUserFont.and.resolveTo({ success: true });
    await service.load();
    const loader = TestBed.inject(FontLoaderService);
    loader.ensureFace(font.faceId);
    await Promise.resolve();
    await Promise.resolve();
    expect(backend.added.length).toBe(1);

    const removed = await service.remove(font.fontId);

    expect(removed).toBeTrue();
    expect(api.deleteUserFont).toHaveBeenCalledWith(font.fontId);
    expect(service.fonts()).toEqual([]);
    expect(backend.deleted).toEqual(backend.added);
    expect(api.getSystemFonts).toHaveBeenCalled();
  });

  it('keeps the font listed when the host refuses to remove it', async () => {
    const font = userFont();
    api.getUserFonts.and.resolveTo({ fonts: [font] });
    api.deleteUserFont.and.resolveTo({ success: false });
    await service.load();

    const removed = await service.remove(font.fontId);

    expect(removed).toBeFalse();
    expect(service.fonts()).toEqual([font]);
  });
});
