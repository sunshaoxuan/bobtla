import test from "node:test";
import assert from "node:assert/strict";
import { OfflineDraftStore } from "../src/services/offlineDraftStore.js";
import { TranslationPipeline } from "../src/services/translationPipeline.js";
import { DraftReplayService } from "../src/services/draftReplayService.js";
import { BudgetExceededError, ComplianceError } from "../src/utils/errors.js";

async function waitFor(predicate, { timeoutMs = 1000, intervalMs = 25 } = {}) {
  const start = Date.now();
  while (Date.now() - start < timeoutMs) {
    if (await predicate()) {
      return;
    }
    await new Promise((resolve) => setTimeout(resolve, intervalMs));
  }
  throw new Error("waitFor timed out");
}

function createSilentLogger() {
  return {
    warn() {},
    error() {},
    info() {}
  };
}

test("DraftReplayService processes pending drafts successfully", async () => {
  const store = new OfflineDraftStore({ retentionHours: 1 });
  const router = {
    async translate({ text }) {
      return { text: `[ok] ${text}`, detectedLanguage: "en", latencyMs: 10 };
    }
  };
  const pipeline = new TranslationPipeline({ router, offlineDraftStore: store });
  const notifierEvents = [];
  const draft = pipeline.saveOfflineDraft({
    userId: "user-1",
    tenantId: "tenant-1",
    originalText: "hello world",
    targetLanguage: "zh-Hans"
  });
  const service = new DraftReplayService({
    offlineDraftStore: store,
    pipeline,
    notifier: {
      notifyDraftCompleted(payload) {
        notifierEvents.push(payload);
      }
    },
    intervalMs: 20,
    maxAttempts: 2,
    backoffStrategy: () => 0,
    logger: createSilentLogger()
  });
  service.start();
  try {
    await waitFor(() => {
      const drafts = store.listDrafts("user-1");
      return drafts[0]?.status === "SUCCEEDED";
    });
  } finally {
    service.stop();
  }
  const saved = store.listDrafts("user-1")[0];
  assert.equal(saved.status, "SUCCEEDED");
  assert.equal(saved.resultText, "[ok] hello world");
  assert.equal(saved.lastErrorCode, null);
  assert.ok(saved.completedAt);
  assert.equal(notifierEvents.length, 1);
  assert.equal(notifierEvents[0].draftId, draft.id);
  assert.equal(notifierEvents[0].resultText, "[ok] hello world");
});

test("DraftReplayService retries transient failures including budget and compliance", async () => {
  const store = new OfflineDraftStore({ retentionHours: 1 });
  const failures = [
    new BudgetExceededError("budget", { remainingUsd: 0 }),
    new ComplianceError("compliance", { policy: { violations: ["pii"] } })
  ];
  let call = 0;
  const router = {
    async translate({ text }) {
      if (call < failures.length) {
        const error = failures[call++];
        throw error;
      }
      call += 1;
      return { text: `[retry-${call}] ${text}`, detectedLanguage: "en", latencyMs: 10 };
    }
  };
  const pipeline = new TranslationPipeline({ router, offlineDraftStore: store });
  pipeline.saveOfflineDraft({
    userId: "user-2",
    tenantId: "tenant-1",
    originalText: "Need approval",
    targetLanguage: "fr"
  });
  const service = new DraftReplayService({
    offlineDraftStore: store,
    pipeline,
    intervalMs: 20,
    maxAttempts: 5,
    backoffStrategy: () => 0,
    logger: createSilentLogger()
  });
  service.start();
  try {
    await waitFor(() => {
      const drafts = store.listDrafts("user-2");
      return drafts[0]?.status === "SUCCEEDED";
    });
  } finally {
    service.stop();
  }
  const saved = store.listDrafts("user-2")[0];
  assert.equal(call, 3);
  assert.equal(saved.status, "SUCCEEDED");
  assert.equal(saved.attempts, 3);
  assert.equal(saved.resultText.startsWith("[retry-"), true);
  assert.equal(saved.errorReason, null);
});

test("DraftReplayService cleans up expired drafts", async () => {
  let current = Date.now();
  const clock = () => current;
  const store = new OfflineDraftStore({ retentionHours: 0.0001, clock });
  const router = {
    async translate({ text }) {
      return { text: `[done] ${text}`, detectedLanguage: "en", latencyMs: 5 };
    }
  };
  const pipeline = new TranslationPipeline({ router, offlineDraftStore: store });
  const draft = pipeline.saveOfflineDraft({
    userId: "user-3",
    tenantId: "tenant-1",
    originalText: "outdated",
    targetLanguage: "de"
  });
  store.updateDraft("user-3", draft.id, { status: "SUCCEEDED", completedAt: clock() });
  const before = store.records.get("user-3");
  assert.equal(before?.length, 1);
  current += store.retentionMs + 10;
  const service = new DraftReplayService({
    offlineDraftStore: store,
    pipeline,
    intervalMs: 20,
    maxAttempts: 2,
    backoffStrategy: () => 0,
    logger: createSilentLogger()
  });
  service.start();
  try {
    await waitFor(() => !store.records.has("user-3"));
  } finally {
    service.stop();
  }
  assert.equal(store.records.has("user-3"), false);
});
