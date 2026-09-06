import { provideZonelessChangeDetection, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { ActionBlock, ActionFlow, WidgetType } from '@macro-deck/runtime';
import { ApiService, ProfileService } from '@shared';
import { ActionClipboardService } from './action-clipboard.service';
import { ActionCutOriginService } from './action-cut-origin.service';
import { AutomationService } from './automation.service';
import { ScriptService } from './script.service';

function block(id: string): ActionBlock {
  return { id, type: 'action', blockType: 'system.run', label: 'Run', color: '#000' };
}

function flows(...children: ActionBlock[]): ActionFlow[] {
  return [{ triggerId: 't', triggerType: 'onShortPress', children }];
}

function ipcWidget(data: unknown) {
  return {
    id: 'w1',
    type: WidgetType.ActionButton,
    positionX: 1,
    positionY: 2,
    width: 3,
    height: 4,
    data: JSON.stringify(data),
  };
}

describe('ActionCutOriginService', () => {
  let service: ActionCutOriginService;
  let clipboard: ActionClipboardService;
  let api: jasmine.SpyObj<ApiService>;
  let updateScript: jasmine.Spy;
  let updateAutomation: jasmine.Spy;
  const selectedProfileId = signal<string | null>('p1');
  const profiles = signal<{ id: string }[]>([{ id: 'p1' }, { id: 'p2' }]);

  beforeEach(() => {
    api = jasmine.createSpyObj<ApiService>('ApiService', [
      'getFolders',
      'updateWidget',
      'getScripts',
      'getAutomations',
    ]);
    api.updateWidget.and.resolveTo({ success: true });
    updateScript = jasmine.createSpy('updateScript').and.resolveTo({ success: true });
    updateAutomation = jasmine.createSpy('updateAutomation').and.resolveTo({ success: true });
    selectedProfileId.set('p1');
    profiles.set([{ id: 'p1' }, { id: 'p2' }]);

    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        { provide: ApiService, useValue: api },
        { provide: ProfileService, useValue: { selectedProfileId, profiles } },
        { provide: ScriptService, useValue: { updateScript } },
        { provide: AutomationService, useValue: { updateAutomation } },
      ],
    });
    service = TestBed.inject(ActionCutOriginService);
    clipboard = TestBed.inject(ActionClipboardService);
  });

  function pendingCut(target: Parameters<ActionCutOriginService['settle']>[0]): void {
    clipboard.cut(block('block-1'), { kind: 'widget', widgetId: 'w1' });
    clipboard.markPasted(target, 'pasted-1');
  }

  const saved = flows(block('pasted-1'));

  it('does nothing when the clipboard is empty', async () => {
    expect(await service.settle({ kind: 'widget', widgetId: 'w2' }, saved)).toBeNull();
    expect(api.getFolders).not.toHaveBeenCalled();
  });

  it('does nothing for a copy, which has no source to remove', async () => {
    clipboard.copy(block('block-1'));

    expect(await service.settle({ kind: 'widget', widgetId: 'w2' }, saved)).toBeNull();
    expect(api.getFolders).not.toHaveBeenCalled();
  });

  it('does nothing until the record the cut was pasted into is the one saving', async () => {
    pendingCut({ kind: 'widget', widgetId: 'w2' });

    expect(await service.settle({ kind: 'widget', widgetId: 'w9' }, saved)).toBeNull();
    expect(api.getFolders).not.toHaveBeenCalled();
    expect(clipboard.entry()).not.toBeNull();
  });

  it('removes the block from the source widget and clears the clipboard', async () => {
    api.getFolders.and.resolveTo({
      folders: [
        { id: 'f1', name: 'A', order: 0, widgets: [] },
        {
          id: 'f2',
          name: 'B',
          order: 1,
          widgets: [ipcWidget({ label: 'Hi', flows: JSON.stringify(flows(block('block-1'), block('block-2'))) })],
        },
      ],
    });
    pendingCut({ kind: 'widget', widgetId: 'w2' });

    const result = await service.settle({ kind: 'widget', widgetId: 'w2' }, saved);

    expect(result).toEqual({ success: true });
    const request = api.updateWidget.calls.mostRecent().args[0];
    expect(request.folderId).toBe('f2');
    expect(request.positionX).toBe(1);
    expect(request.width).toBe(3);

    const data = JSON.parse(request.data!) as { flows: string };
    const written = JSON.parse(data.flows) as ActionFlow[];
    expect(written[0].children.map(c => c.id)).toEqual(['block-2']);
    expect(clipboard.entry()).toBeNull();
  });

  it('keeps data keys it does not know about', async () => {
    api.getFolders.and.resolveTo({
      folders: [{
        id: 'f1',
        name: 'A',
        order: 0,
        widgets: [ipcWidget({
          futureSetting: { nested: true },
          flows: JSON.stringify(flows(block('block-1'))),
        })],
      }],
    });
    pendingCut({ kind: 'widget', widgetId: 'w2' });

    await service.settle({ kind: 'widget', widgetId: 'w2' }, saved);

    const data = JSON.parse(api.updateWidget.calls.mostRecent().args[0].data!) as Record<string, unknown>;
    expect(data['futureSetting']).toEqual({ nested: true });
  });

  it('searches the other profiles when the source is not in the selected one', async () => {
    api.getFolders.and.callFake((profileId?: string) =>
      Promise.resolve(profileId === 'p2'
        ? {
            folders: [{
              id: 'f9',
              name: 'Elsewhere',
              order: 0,
              widgets: [ipcWidget({ flows: JSON.stringify(flows(block('block-1'))) })],
            }],
          }
        : { folders: [] }),
    );
    pendingCut({ kind: 'widget', widgetId: 'w2' });

    await service.settle({ kind: 'widget', widgetId: 'w2' }, saved);

    expect(api.getFolders.calls.allArgs()).toEqual([['p1'], ['p2']]);
    expect(api.updateWidget.calls.mostRecent().args[0].folderId).toBe('f9');
  });

  it('treats a source widget that no longer exists as settled', async () => {
    api.getFolders.and.resolveTo({ folders: [] });
    pendingCut({ kind: 'widget', widgetId: 'w2' });

    expect(await service.settle({ kind: 'widget', widgetId: 'w2' }, saved)).toEqual({ success: true });
    expect(api.updateWidget).not.toHaveBeenCalled();
    expect(clipboard.entry()).toBeNull();
  });

  it('does not rewrite a widget whose block is already gone', async () => {
    api.getFolders.and.resolveTo({
      folders: [{
        id: 'f1',
        name: 'A',
        order: 0,
        widgets: [ipcWidget({ flows: JSON.stringify(flows(block('block-2'))) })],
      }],
    });
    pendingCut({ kind: 'widget', widgetId: 'w2' });

    expect(await service.settle({ kind: 'widget', widgetId: 'w2' }, saved)).toEqual({ success: true });
    expect(api.updateWidget).not.toHaveBeenCalled();
  });

  it('does nothing when the saved flows no longer contain the pasted action', async () => {
    pendingCut({ kind: 'widget', widgetId: 'w2' });

    const result = await service.settle({ kind: 'widget', widgetId: 'w2' }, flows(block('something-else')));

    expect(result).toBeNull();
    expect(api.getFolders).not.toHaveBeenCalled();
    expect(clipboard.entry()).not.toBeNull();
  });

  it('keeps the clipboard when the source could not be reached', async () => {
    api.getFolders.and.rejectWith(new Error('host unreachable'));
    pendingCut({ kind: 'widget', widgetId: 'w2' });

    expect(await service.settle({ kind: 'widget', widgetId: 'w2' }, saved)).toEqual({ success: false });
    expect(clipboard.entry()).not.toBeNull();
  });

  it('removes the block from a source script', async () => {
    api.getScripts.and.resolveTo({
      scripts: [{
        id: 's1',
        name: 'Script',
        description: '',
        flows: JSON.stringify(flows(block('block-1'), block('block-2'))),
        createdAt: '',
        updatedAt: '',
      }],
    });
    clipboard.cut(block('block-1'), { kind: 'script', scriptId: 's1' });
    clipboard.markPasted({ kind: 'widget', widgetId: 'w2' }, 'pasted-1');

    expect(await service.settle({ kind: 'widget', widgetId: 'w2' }, saved)).toEqual({ success: true });
    const [id, changes] = updateScript.calls.mostRecent().args as [string, { flows: ActionFlow[] }];
    expect(id).toBe('s1');
    expect(changes.flows[0].children.map(c => c.id)).toEqual(['block-2']);
  });

  it('removes the block from a source automation', async () => {
    api.getAutomations.and.resolveTo({
      automations: [{
        id: 'a1',
        name: 'Automation',
        description: '',
        enabled: true,
        flows: JSON.stringify(flows(block('block-1'))),
        createdAt: '',
        updatedAt: '',
      }],
    });
    clipboard.cut(block('block-1'), { kind: 'automation', automationId: 'a1' });
    clipboard.markPasted({ kind: 'script', scriptId: 's2' }, 'pasted-1');

    expect(await service.settle({ kind: 'script', scriptId: 's2' }, saved)).toEqual({ success: true });
    const [id, changes] = updateAutomation.calls.mostRecent().args as [string, { flows: ActionFlow[] }];
    expect(id).toBe('a1');
    expect(changes.flows[0].children).toEqual([]);
  });
});
