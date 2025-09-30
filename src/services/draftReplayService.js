import { BudgetExceededError, ComplianceError } from "../utils/errors.js";

function defaultBackoffStrategy(attempt) {
  const base = 1000 * Math.pow(2, attempt - 1);
  return Math.min(base, 60_000);
}

export class DraftReplayService {
  constructor({
    offlineDraftStore,
    pipeline,
    notifier,
    intervalMs = 5000,
    maxAttempts = 3,
    backoffStrategy = defaultBackoffStrategy,
    logger = console
  } = {}) {
    if (!offlineDraftStore) {
      throw new Error("offlineDraftStore is required");
    }
    if (!pipeline) {
      throw new Error("pipeline is required");
    }
    this.store = offlineDraftStore;
    this.pipeline = pipeline;
    this.notifier = notifier;
    this.intervalMs = intervalMs;
    this.maxAttempts = maxAttempts;
    this.backoffStrategy = backoffStrategy;
    this.logger = logger ?? console;
    this.running = false;
    this.timer = null;
    this.processing = Promise.resolve();
  }

  start() {
    if (this.running) {
      return;
    }
    this.running = true;
    this.#schedule(0);
  }

  stop() {
    this.running = false;
    if (this.timer) {
      clearTimeout(this.timer);
      this.timer = null;
    }
  }

  async #processCycle() {
    try {
      this.store.cleanupExpired?.();
    } catch (error) {
      this.logger?.warn?.("Failed to cleanup offline drafts", error);
    }
    const pending = this.store.getPendingDrafts?.() ?? [];
    for (const draft of pending) {
      await this.#processDraft(draft);
    }
  }

  async #processDraft(draft) {
    const attempts = (draft.attempts ?? 0) + 1;
    const locked = this.store.updateDraft(draft.userId, draft.id, {
      status: "PROCESSING",
      attempts,
      errorReason: null
    });
    if (!locked) {
      return;
    }
    try {
      const result = await this.pipeline.translateText({
        text: draft.originalText,
        sourceLanguage: draft.sourceLanguage ?? "auto",
        targetLanguage: draft.targetLanguage,
        tenantId: draft.tenantId,
        userId: draft.userId,
        channelId: draft.channelId,
        metadata: {
          ...(draft.metadata ?? {}),
          origin: "offlineReplay",
          draftId: draft.id
        }
      });
      this.store.updateDraft(draft.userId, draft.id, {
        status: "SUCCEEDED",
        resultText: result.text,
        completedAt: this.#now(),
        attempts,
        errorReason: null,
        nextAttemptAt: null,
        lastErrorCode: null
      });
      this.notifier?.notifyDraftCompleted?.({
        userId: draft.userId,
        tenantId: draft.tenantId,
        draftId: draft.id,
        resultText: result.text,
        draft,
        metadata: result.metadata
      });
    } catch (error) {
      const shouldRetry = attempts < this.maxAttempts;
      const baseUpdate = {
        attempts,
        errorReason: error.message,
        lastErrorCode: error.code ?? error.name,
        status: shouldRetry ? "PENDING" : "FAILED",
        completedAt: shouldRetry ? undefined : this.#now()
      };
      if (shouldRetry) {
        const delay = this.#resolveBackoffDelay(attempts, error);
        baseUpdate.nextAttemptAt = this.#now() + delay;
      } else {
        baseUpdate.nextAttemptAt = null;
      }
      this.store.updateDraft(draft.userId, draft.id, baseUpdate);
      if (!shouldRetry) {
        this.logger?.warn?.("Offline draft permanently failed", { draftId: draft.id, error: error.message });
      }
    }
  }

  #resolveBackoffDelay(attempt, error) {
    try {
      if (this.backoffStrategy) {
        return this.backoffStrategy(attempt, error);
      }
    } catch (strategyError) {
      this.logger?.warn?.("Backoff strategy failed", strategyError);
    }
    if (error instanceof BudgetExceededError) {
      return 15_000;
    }
    if (error instanceof ComplianceError) {
      return 30_000;
    }
    return defaultBackoffStrategy(attempt);
  }

  #schedule(delay) {
    if (!this.running) {
      return;
    }
    if (this.timer) {
      clearTimeout(this.timer);
    }
    this.timer = setTimeout(() => {
      this.processing = this.#processCycle()
        .catch((error) => {
          this.logger?.error?.("Draft replay cycle failed", error);
        })
        .finally(() => {
          this.#schedule(this.intervalMs);
        });
    }, delay);
  }

  #now() {
    try {
      if (typeof this.store.clock === "function") {
        return this.store.clock();
      }
    } catch (error) {
      this.logger?.warn?.("Failed to read draft store clock", error);
    }
    return Date.now();
  }
}

export default {
  DraftReplayService
};
