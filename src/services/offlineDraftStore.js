import { offlineDraftPolicy } from "../config.js";

export class OfflineDraftStore {
  constructor({
    maxEntriesPerUser = offlineDraftPolicy.maxEntriesPerUser,
    retentionHours = offlineDraftPolicy.retentionHours,
    clock
  } = {}) {
    this.maxEntriesPerUser = maxEntriesPerUser;
    this.retentionMs = retentionHours * 60 * 60 * 1000;
    this.records = new Map();
    this.clock = typeof clock === "function" ? clock : () => Date.now();
  }

  saveDraft(userId, draft) {
    const now = this.clock();
    const entry = {
      id: draft.id ?? `${userId}-${now}`,
      userId,
      tenantId: draft.tenantId,
      channelId: draft.channelId,
      originalText: draft.originalText,
      targetLanguage: draft.targetLanguage,
      sourceLanguage: draft.sourceLanguage,
      metadata: draft.metadata ?? null,
      status: draft.status ?? "PENDING",
      createdAt: now,
      updatedAt: now,
      completedAt: draft.completedAt ?? null,
      resultText: draft.resultText ?? null,
      errorReason: draft.errorReason ?? null,
      lastErrorCode: draft.lastErrorCode ?? null,
      attempts: draft.attempts ?? 0,
      nextAttemptAt: draft.nextAttemptAt ?? now
    };
    const existing = this.records.get(userId) ?? [];
    const filtered = this.#applyRetention(existing, now);
    filtered.unshift(entry);
    this.records.set(userId, filtered.slice(0, this.maxEntriesPerUser));
    return { ...entry };
  }

  listDrafts(userId) {
    const now = this.clock();
    const drafts = this.records.get(userId) ?? [];
    const filtered = this.#applyRetention(drafts, now);
    this.records.set(userId, filtered);
    return filtered.map((draft) => ({ ...draft }));
  }

  deleteDraft(userId, draftId) {
    const drafts = this.records.get(userId) ?? [];
    const next = drafts.filter((d) => d.id !== draftId);
    this.records.set(userId, next);
    return next.length !== drafts.length;
  }

  updateDraft(userId, draftId, updates = {}) {
    const drafts = this.records.get(userId) ?? [];
    const index = drafts.findIndex((draft) => draft.id === draftId);
    if (index === -1) {
      return null;
    }
    const now = this.clock();
    const current = drafts[index];
    const next = {
      ...current,
      ...updates,
      updatedAt: now,
      completedAt: updates.completedAt !== undefined ? updates.completedAt : current.completedAt,
      resultText: updates.resultText !== undefined ? updates.resultText : current.resultText,
      errorReason: updates.errorReason !== undefined ? updates.errorReason : current.errorReason,
      attempts: updates.attempts !== undefined ? updates.attempts : current.attempts,
      nextAttemptAt: updates.nextAttemptAt !== undefined ? updates.nextAttemptAt : current.nextAttemptAt,
      lastErrorCode: updates.lastErrorCode !== undefined ? updates.lastErrorCode : current.lastErrorCode
    };
    drafts[index] = next;
    this.records.set(userId, drafts);
    return { ...next };
  }

  getPendingDrafts({ limit } = {}) {
    const now = this.clock();
    const pending = [];
    for (const drafts of this.records.values()) {
      for (const draft of drafts) {
        if (draft.status === "PENDING" && (draft.nextAttemptAt ?? 0) <= now) {
          pending.push({ ...draft });
          if (limit && pending.length >= limit) {
            return pending;
          }
        }
      }
    }
    return pending;
  }

  cleanupExpired() {
    const now = this.clock();
    for (const [userId, drafts] of this.records.entries()) {
      const filtered = this.#applyRetention(drafts, now);
      if (filtered.length === 0) {
        this.records.delete(userId);
      } else {
        this.records.set(userId, filtered);
      }
    }
  }

  #applyRetention(drafts, now) {
    return drafts.filter((draft) => now - draft.createdAt <= this.retentionMs);
  }
}

export default {
  OfflineDraftStore
};
