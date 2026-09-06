import { WIDGET_REFERENCE_BORDER_RADIUS, WIDGET_REFERENCE_GAP } from '../domain/widget.interface';
import { DEFAULT_GRID_GEOMETRY } from './grid-metrics';
import { resolveFolderGrid } from './folder-grid';

describe('folder grid inheritance', () => {
  const stated = { cols: 8, rows: 4, spacing: 20, borderRadius: 30 };
  const nothing = { cols: null, rows: null, spacing: null, borderRadius: null };
  const profile = { columns: 6, rows: 5, spacing: 18, borderRadius: 26 };

  it('prefers what the folder states', () => {
    expect(resolveFolderGrid(stated, profile))
      .toEqual({ cols: 8, rows: 4, spacing: 20, borderRadius: 30 });
  });

  it('falls back to the profile for everything the folder leaves out', () => {
    expect(resolveFolderGrid(nothing, profile))
      .toEqual({ cols: 6, rows: 5, spacing: 18, borderRadius: 26 });
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
      .toEqual({ cols: 6, rows: 5, spacing: 18, borderRadius: 26 });
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
    });
  });

  it('inherits field by field', () => {
    expect(resolveFolderGrid({ ...nothing, cols: 8 }, profile))
      .toEqual({ cols: 8, rows: 5, spacing: 18, borderRadius: 26 });
  });

  it('treats a stated zero as stated', () => {
    expect(resolveFolderGrid({ ...nothing, spacing: 0 }, profile).spacing).toBe(0);
  });

  it('resolves a folder it has never heard of', () => {
    expect(resolveFolderGrid(null).cols).toBe(DEFAULT_GRID_GEOMETRY.cols);
  });
});
