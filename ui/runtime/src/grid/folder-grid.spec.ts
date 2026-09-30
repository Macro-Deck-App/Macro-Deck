import { WIDGET_REFERENCE_BORDER_RADIUS, WIDGET_REFERENCE_GAP } from '../domain/widget.interface';
import { DEFAULT_GRID_GEOMETRY } from './grid-metrics';
import { resolveFolderGrid } from './folder-grid';

describe('folder grid inheritance', () => {
  const stated = { cols: 8, rows: 4, spacing: 20, borderRadius: 30 };
  const nothing = { cols: null, rows: null, spacing: null, borderRadius: null };
  const profile = { columns: 6, rows: 5, spacing: 18, borderRadius: 26 };

  it('prefers what the folder states', () => {
    expect(resolveFolderGrid(stated, profile))
      .toEqual({ cols: 8, rows: 4, spacing: 20, borderRadius: 30, emptyCellStyle: 'visible', shadows: true });
  });

  it('falls back to the profile for everything the folder leaves out', () => {
    expect(resolveFolderGrid(nothing, profile))
      .toEqual({ cols: 6, rows: 5, spacing: 18, borderRadius: 26, emptyCellStyle: 'visible', shadows: true });
  });

  it('takes the nearest ancestor that states one, ahead of the profile', () => {
    // What `Folder.cols` promises: "null = inherit from the parent folder, then the profile
    // default". A subfolder of a six-column folder was drawn five columns wide on the deck while
    // the editor beside it drew six, because only the deck skipped the chain.
    const parent = { cols: 6, rows: null, spacing: null, borderRadius: null };

    expect(resolveFolderGrid(nothing, profile, [parent]).cols).toBe(6);
  });

  it('keeps walking past an ancestor that states nothing either', () => {
    const grandparent = { cols: 9, rows: null, spacing: null, borderRadius: null };

    expect(resolveFolderGrid(nothing, profile, [nothing, grandparent]).cols).toBe(9);
  });

  it('resolves each value on its own chain', () => {
    // A parent that states only its columns must not shadow the profile's spacing as well.
    const parent = { cols: 6, rows: null, spacing: null, borderRadius: null };

    expect(resolveFolderGrid(nothing, profile, [parent]))
      .toEqual({ cols: 6, rows: 5, spacing: 18, borderRadius: 26, emptyCellStyle: 'visible', shadows: true });
  });

  it('lets what the folder states win over an ancestor that states one too', () => {
    const parent = { cols: 6, rows: null, spacing: null, borderRadius: null };

    expect(resolveFolderGrid(stated, profile, [parent]).cols).toBe(8);
  });

  it('reaches the profile when no ancestor states one', () => {
    expect(resolveFolderGrid(nothing, profile, [nothing, nothing]).cols).toBe(6);
  });

  it('falls back to the built-in shape when neither states one', () => {
    expect(resolveFolderGrid(nothing, {})).toEqual({
      cols: DEFAULT_GRID_GEOMETRY.cols,
      rows: DEFAULT_GRID_GEOMETRY.rows,
      spacing: WIDGET_REFERENCE_GAP,
      borderRadius: WIDGET_REFERENCE_BORDER_RADIUS,
      emptyCellStyle: 'visible',
      shadows: true,
    });
  });

  it('inherits field by field', () => {
    expect(resolveFolderGrid({ ...nothing, cols: 8 }, profile))
      .toEqual({ cols: 8, rows: 5, spacing: 18, borderRadius: 26, emptyCellStyle: 'visible', shadows: true });
  });

  it('treats a stated zero as stated', () => {
    expect(resolveFolderGrid({ ...nothing, spacing: 0 }, profile).spacing).toBe(0);
  });

  it('keeps widget shadows on unless the profile switches them off', () => {
    expect(resolveFolderGrid(nothing, profile).shadows).toBeTrue();
    expect(resolveFolderGrid(nothing, { ...profile, shadows: null }).shadows).toBeTrue();
    expect(resolveFolderGrid(nothing, { ...profile, shadows: true }).shadows).toBeTrue();
    expect(resolveFolderGrid(nothing, { ...profile, shadows: false }).shadows).toBeFalse();
    expect(resolveFolderGrid(null).shadows).toBeTrue();
  });

  it('resolves a folder it has never heard of', () => {
    expect(resolveFolderGrid(null).cols).toBe(DEFAULT_GRID_GEOMETRY.cols);
  });

  describe('empty cells', () => {
    const transparent = { ...nothing, emptyCellStyle: 'transparent' as const };
    const visible = { ...nothing, emptyCellStyle: 'visible' as const };

    it('prefers what the folder states over the profile default', () => {
      expect(resolveFolderGrid(visible, { emptyCellStyle: 'transparent' }).emptyCellStyle).toBe('visible');
    });

    it('takes the nearest ancestor that states one, ahead of the profile', () => {
      expect(resolveFolderGrid(nothing, { emptyCellStyle: 'visible' }, [nothing, transparent]).emptyCellStyle)
        .toBe('transparent');
    });

    it('falls back to the profile default when no folder in the chain states one', () => {
      expect(resolveFolderGrid(nothing, { emptyCellStyle: 'transparent' }, [nothing]).emptyCellStyle)
        .toBe('transparent');
    });

    it('shows empty cells when neither the folders nor the profile say otherwise', () => {
      expect(resolveFolderGrid(nothing, profile).emptyCellStyle).toBe('visible');
    });

    it('treats a folder and profile from a host without the setting as visible', () => {
      const legacyFolder = { cols: null, rows: null, spacing: null, borderRadius: null };

      expect(resolveFolderGrid(legacyFolder, { columns: 5, rows: 3 }).emptyCellStyle).toBe('visible');
    });
  });
});
