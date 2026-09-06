import { Injectable, inject } from '@angular/core';

import { ActionFlow, findBlock, removeBlockFromFlows } from '@macro-deck/runtime';
import { ApiService, ProfileService } from '@shared';
import { ActionClipboardService, ActionFlowOwner, sameActionFlowOwner } from './action-clipboard.service';
import { AutomationService } from './automation.service';
import { ScriptService } from './script.service';

@Injectable({ providedIn: 'root' })
export class ActionCutOriginService {
  private readonly api = inject(ApiService);
  private readonly clipboard = inject(ActionClipboardService);
  private readonly profiles = inject(ProfileService);
  private readonly scripts = inject(ScriptService);
  private readonly automations = inject(AutomationService);

  async settle(target: ActionFlowOwner, savedFlows: ActionFlow[]): Promise<{ success: boolean } | null> {
    const entry = this.clipboard.entry();
    if (!entry?.isCut || !entry.origin || !entry.pendingPasteOwner) return null;
    if (!sameActionFlowOwner(entry.pendingPasteOwner, target)) return null;
    if (!entry.pendingPasteBlockId || !findBlock(savedFlows, entry.pendingPasteBlockId)) return null;

    try {
      await this.removeFrom(entry.origin.owner, entry.origin.blockId);
    } catch (error) {
      console.error('Failed to remove the moved action from its source:', error);
      return { success: false };
    }

    this.clipboard.clear();
    return { success: true };
  }

  private async removeFrom(owner: ActionFlowOwner, blockId: string): Promise<void> {
    switch (owner.kind) {
      case 'widget':
        return this.removeFromWidget(owner.widgetId, blockId);
      case 'script':
        return this.removeFromScript(owner.scriptId, blockId);
      case 'automation':
        return this.removeFromAutomation(owner.automationId, blockId);
    }
  }

  private async removeFromWidget(widgetId: string, blockId: string): Promise<void> {
    for (const profileId of this.candidateProfileIds()) {
      const response = await this.api.getFolders(profileId);
      for (const folder of response.folders ?? []) {
        const widget = folder.widgets?.find(w => w.id === widgetId);
        if (!widget) continue;

        const data = this.withBlockRemoved(widget.data, blockId);
        if (!data) return;

        await this.api.updateWidget({
          id: widget.id,
          folderId: folder.id,
          type: widget.type,
          positionX: widget.positionX,
          positionY: widget.positionY,
          width: widget.width,
          height: widget.height,
          data,
        });
        return;
      }
    }
  }

  private async removeFromScript(scriptId: string, blockId: string): Promise<void> {
    const response = await this.api.getScripts();
    const script = response.scripts?.find(s => s.id === scriptId);
    if (!script) return;

    const removal = removeBlockFromFlows(parseFlows(script.flows), blockId);
    if (!removal.removed) return;

    await this.scripts.updateScript(scriptId, { flows: removal.flows });
  }

  private async removeFromAutomation(automationId: string, blockId: string): Promise<void> {
    const response = await this.api.getAutomations();
    const automation = response.automations?.find(a => a.id === automationId);
    if (!automation) return;

    const removal = removeBlockFromFlows(parseFlows(automation.flows), blockId);
    if (!removal.removed) return;

    await this.automations.updateAutomation(automationId, { flows: removal.flows });
  }

  private candidateProfileIds(): (string | undefined)[] {
    const selected = this.profiles.selectedProfileId();
    const ids = this.profiles.profiles().map(p => p.id).filter(id => id !== selected);
    const ordered = selected ? [selected, ...ids] : ids;
    return ordered.length > 0 ? ordered : [undefined];
  }

  private withBlockRemoved(raw: string | undefined, blockId: string): string | null {
    if (!raw) return null;

    let record: Record<string, unknown>;
    try {
      const parsed: unknown = JSON.parse(raw);
      if (typeof parsed !== 'object' || parsed === null) return null;
      record = parsed as Record<string, unknown>;
    } catch {
      return null;
    }

    const stored = record['flows'];
    const removal = removeBlockFromFlows(parseFlows(stored), blockId);
    if (!removal.removed) return null;

    record['flows'] = typeof stored === 'string' ? JSON.stringify(removal.flows) : removal.flows;
    return JSON.stringify(record);
  }
}

function parseFlows(value: unknown): ActionFlow[] {
  if (Array.isArray(value)) return value as ActionFlow[];
  if (typeof value !== 'string' || value.length === 0) return [];
  try {
    const parsed: unknown = JSON.parse(value);
    return Array.isArray(parsed) ? (parsed as ActionFlow[]) : [];
  } catch {
    return [];
  }
}
