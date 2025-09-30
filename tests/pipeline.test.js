import test from "node:test";
import assert from "node:assert/strict";
import { MockModelProvider } from "../src/models/modelProvider.js";
import { TranslationRouter } from "../src/services/translationRouter.js";
import { TranslationPipeline } from "../src/services/translationPipeline.js";
import { OfflineDraftStore } from "../src/services/offlineDraftStore.js";
import { GlossaryManager } from "../src/services/glossaryManager.js";
import { LanguageDetector } from "../src/services/languageDetector.js";
import { BudgetGuard } from "../src/services/budgetGuard.js";

test("pipeline returns adaptive card payload", async () => {
  const provider = new MockModelProvider({
    id: "primary",
    costPerCharUsd: 0.0001,
    latencyTargetMs: 100,
    behavior: { translationPrefix: "[zh]", detectedLanguage: "en" }
  });
  const router = new TranslationRouter({
    providers: [provider],
    glossaryManager: new GlossaryManager(),
    detector: new LanguageDetector([provider]),
    budgetGuard: new BudgetGuard({ dailyBudgetUsd: 1 })
  });
  const pipeline = new TranslationPipeline({ router, offlineDraftStore: new OfflineDraftStore({}) });
  const result = await pipeline.translateAndReply({
    text: "Hello",
    targetLanguage: "zh-Hans",
    tenantId: "tenantA",
    userId: "userA",
    channelId: "channelA"
  });
  assert.equal(result.replyPayload.type, "AdaptiveCard");
  assert.equal(result.replyPayload.actions[1].data.action, "changeLanguage");
});

test("offline drafts respect retention and limits", () => {
  const store = new OfflineDraftStore({ maxEntriesPerUser: 2, retentionHours: 0.0001 });
  const pipeline = new TranslationPipeline({
    router: { translate: async () => ({ text: "test" }) },
    offlineDraftStore: store
  });
  const first = pipeline.saveOfflineDraft({ userId: "userA", tenantId: "tenantA", originalText: "one", targetLanguage: "es" });
  const second = pipeline.saveOfflineDraft({ userId: "userA", tenantId: "tenantA", originalText: "two", targetLanguage: "es" });
  const third = pipeline.saveOfflineDraft({ userId: "userA", tenantId: "tenantA", originalText: "three", targetLanguage: "es" });
  assert.equal(store.listDrafts("userA").length <= 2, true);
  store.records.get("userA").forEach((draft) => {
    draft.createdAt = Date.now() - 1000 * 60 * 60; // expire
  });
  assert.equal(store.listDrafts("userA").length, 0);
  assert.ok(first.id);
  assert.ok(second.id);
  assert.ok(third.id);
});

test("listOfflineDrafts returns saved drafts", () => {
  const store = new OfflineDraftStore({ maxEntriesPerUser: 5, retentionHours: 1 });
  const pipeline = new TranslationPipeline({
    router: { translate: async () => ({ text: "test" }) },
    offlineDraftStore: store
  });
  pipeline.saveOfflineDraft({ userId: "userA", tenantId: "tenantA", originalText: "hello", targetLanguage: "es" });
  const drafts = pipeline.listOfflineDrafts("userA");
  assert.equal(Array.isArray(drafts), true);
  assert.equal(drafts.length, 1);
  assert.equal(drafts[0].status, "PENDING");
  assert.equal(drafts[0].targetLanguage, "es");
});
