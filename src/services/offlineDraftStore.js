import { offlineDraftPolicy } from "../config.js";

export class OfflineDraftStore {
  constructor({ maxEntriesPerUser = offlineDraftPolicy.maxEntriesPerUser, retentionHours = offlineDraftPolicy.retentionHours } = {}) {
    this.maxEntriesPerUser = maxEntriesPerUser;
    this.retentionMs = retentionHours * 60 * 60 * 1000;
    this.records = new Map();
  }

  saveDraft(userId, draft) {
    const now = Date.now();
    const entry = { ...draft, createdAt: now, id: draft.id ?? `${userId}-${now}` };
    const existing = this.records.get(userId) ?? [];
    const filtered = existing.filter((d) => now - d.createdAt <= this.retentionMs);
    filtered.unshift(entry);
    this.records.set(userId, filtered.slice(0, this.maxEntriesPerUser));
    return entry;
  }

  listDrafts(userId) {
    const now = Date.now();
    const drafts = this.records.get(userId) ?? [];
    const filtered = drafts.filter((d) => now - d.createdAt <= this.retentionMs);
    this.records.set(userId, filtered);
    return filtered;
  }

  deleteDraft(userId, draftId) {
    const drafts = this.records.get(userId) ?? [];
    const next = drafts.filter((d) => d.id !== draftId);
    this.records.set(userId, next);
    return next.length !== drafts.length;
  }
}

export default {
  OfflineDraftStore
};
