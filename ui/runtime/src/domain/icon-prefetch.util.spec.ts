import { Folder } from './folder.interface';
import { IconPrefetchMetrics, collectIconPrefetchTargets, iconSizeBucket, widgetIconZoomFactor } from './icon-prefetch.util';
import { ActionButtonData, GridWidget, SliderData, WidgetType } from './widget.interface';
import { WIDGET_GRID_VIEW_ID } from './index';

describe('icon-prefetch.util', () => {
  describe('iconSizeBucket', () => {
    it('buckets pixel sizes into the served renditions', () => {
      expect(iconSizeBucket(1)).toBe(128);
      expect(iconSizeBucket(128)).toBe(128);
      expect(iconSizeBucket(129)).toBe(256);
      expect(iconSizeBucket(256)).toBe(256);
      expect(iconSizeBucket(257)).toBe(512);
    });
  });

  describe('collectIconPrefetchTargets', () => {
    function button(id: string, offIcon?: string, onIcon?: string, w = 1, h = 1): GridWidget {
      const data: ActionButtonData = {
        label: '',
        stateMode: true,
        states: [
          { id: 'off', label: 'Off', appearance: { iconId: offIcon } },
          { id: 'on', label: 'On', appearance: { iconId: onIcon } },
        ],
      };
      return { id, folderId: 'f', x: 0, y: 0, w, h, type: WidgetType.ActionButton, data };
    }

    function momentaryButton(id: string, iconId?: string, w = 1, h = 1): GridWidget {
      const data: ActionButtonData = { label: '', iconId };
      return { id, folderId: 'f', x: 0, y: 0, w, h, type: WidgetType.ActionButton, data };
    }

    function slider(id: string, iconId?: string): GridWidget {
      return { id, folderId: 'f', x: 0, y: 1, w: 1, h: 1, type: WidgetType.Slider, data: { iconId } as SliderData };
    }

    function makeFolder(id: string, widgets: GridWidget[], cols = 5, rows = 3): Folder {
      return {
        id,
        name: id,
        parentId: null,
        order: 0,
        isExpanded: false,
        isDefault: false,
        cols,
        rows,
        background: '',
        spacing: null,
        borderRadius: null,
        viewId: WIDGET_GRID_VIEW_ID,
        viewConfiguration: null,
        widgets
      };
    }

    function metrics(overrides: Partial<IconPrefetchMetrics> = {}): IconPrefetchMetrics {
      return {
        excludeFolderId: null,
        viewportWidth: 800,
        viewportHeight: 480,
        outerMargin: 4,
        devicePixelRatio: 1,
        resolveGrid: folder => ({ cols: folder.cols ?? 5, rows: folder.rows ?? 3, spacing: 12 }),
        ...overrides
      };
    }

    it('collects both action-button state icons and slider icons', () => {
      const folder = makeFolder('f1', [button('w1', 'icon-off', 'icon-on'), slider('w2', 'icon-slider')]);

      const targets = collectIconPrefetchTargets([folder], metrics());

      expect(targets.map(t => t.iconId).sort()).toEqual(['icon-off', 'icon-on', 'icon-slider']);
    });

    // Issue #425: the walker reads the typed `icon` reference the same way it reads the legacy
    // `iconId` - a widget already migrated to the reference model must prefetch identically.
    it('reads the typed icon-pack reference the same way it reads the legacy iconId', () => {
      const data: ActionButtonData = {
        label: '',
        stateMode: true,
        states: [
          { id: 'off', label: 'Off', appearance: { icon: { type: 'icon-pack', reference: 'icon-off' } } },
          { id: 'on', label: 'On', appearance: { icon: { type: 'icon-pack', reference: 'icon-on' } } },
        ],
      };
      const withTypedIcons: GridWidget = { id: 'w1', folderId: 'f', x: 0, y: 0, w: 1, h: 1, type: WidgetType.ActionButton, data };
      const sliderWithTypedIcon: GridWidget = {
        id: 'w2', folderId: 'f', x: 0, y: 1, w: 1, h: 1, type: WidgetType.Slider,
        data: { icon: { type: 'icon-pack', reference: 'icon-slider' } } as SliderData,
      };

      const targets = collectIconPrefetchTargets(
        [makeFolder('f1', [withTypedIcons, sliderWithTypedIcon])], metrics());

      expect(targets.map(t => t.iconId).sort()).toEqual(['icon-off', 'icon-on', 'icon-slider']);
    });

    // A non-icon-pack provider reference has no id the icon-pack image endpoint can serve - it must
    // be skipped rather than prefetched as a bogus id.
    it('never prefetches a non-icon-pack provider reference', () => {
      const data: ActionButtonData = { label: '', icon: { type: 'plugin-asset', reference: 'spotify:album/x' } };
      const widget: GridWidget = { id: 'w1', folderId: 'f', x: 0, y: 0, w: 1, h: 1, type: WidgetType.ActionButton, data };

      const targets = collectIconPrefetchTargets([makeFolder('f1', [widget])], metrics());

      expect(targets).toEqual([]);
    });

    it('skips the excluded folder and widgets without icons', () => {
      const excluded = makeFolder('current', [button('w1', 'visible-anyway')]);
      const other = makeFolder('other', [button('w2', 'wanted'), button('w3')]);

      const targets = collectIconPrefetchTargets([excluded, other], metrics({ excludeFolderId: 'current' }));

      expect(targets).toEqual([jasmine.objectContaining({ iconId: 'wanted' })]);
    });

    it('dedupes the same icon at the same bucket across folders', () => {
      const a = makeFolder('a', [button('w1', 'shared')]);
      const b = makeFolder('b', [button('w2', 'shared')]);

      const targets = collectIconPrefetchTargets([a, b], metrics());

      expect(targets.length).toBe(1);
    });

    it('derives the bucket from the folder grid metrics like the widget grid', () => {
      const small = collectIconPrefetchTargets([makeFolder('f', [button('w1', 'i')])], metrics());
      expect(small[0].size).toBe(256);

      const dense = collectIconPrefetchTargets([makeFolder('f', [button('w1', 'i')], 16, 3)], metrics());
      expect(dense[0].size).toBe(128);

      const retina = collectIconPrefetchTargets(
        [makeFolder('f', [button('w1', 'i')])],
        metrics({ devicePixelRatio: 2 })
      );
      expect(retina[0].size).toBe(512);
    });

    it('keeps distinct buckets when folders render the same icon at different sizes', () => {
      const coarse = makeFolder('a', [button('w1', 'i')], 5, 3);
      const dense = makeFolder('b', [button('w2', 'i')], 16, 3);

      const targets = collectIconPrefetchTargets([coarse, dense], metrics());

      expect(targets.map(t => t.size).sort((x, y) => x - y)).toEqual([128, 256]);
    });

    it('returns nothing while the viewport is not laid out yet', () => {
      const folder = makeFolder('f', [button('w1', 'i')]);

      expect(collectIconPrefetchTargets([folder], metrics({ viewportWidth: 0, viewportHeight: 0 }))).toEqual([]);
    });

    it('sizes multi-cell widgets by their full span', () => {
      const folder = makeFolder('f', [button('w1', 'i', undefined, 2, 2)]);

      const targets = collectIconPrefetchTargets([folder], metrics());

      expect(targets[0].size).toBe(512);
    });

    it('raises the bucket for a zoomed icon, by the largest zoom of the button\'s states', () => {
      const dense = (widget: GridWidget) => collectIconPrefetchTargets([makeFolder('f', [widget], 16, 3)], metrics());
      const plain = button('w1', 'i');
      const zoomed = button('w2', 'i');
      (zoomed.data as ActionButtonData).states!.find(s => s.id === 'on')!.appearance!.iconDisplay = { zoom: 400 };

      expect(dense(plain)[0].size).toBe(128);
      expect(dense(zoomed)[0].size).toBe(256);
    });

    it('collects a momentary button\'s root icon and its zoom', () => {
      const widget = momentaryButton('w1', 'icon-1');
      (widget.data as ActionButtonData).iconDisplay = { zoom: 300 };

      const targets = collectIconPrefetchTargets([makeFolder('f', [widget])], metrics());

      expect(targets.map(t => t.iconId)).toEqual(['icon-1']);
      expect(widgetIconZoomFactor(widget)).toBe(3);
    });
  });
});
