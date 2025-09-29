import test from "node:test";
import assert from "node:assert/strict";
import { MessageExtensionHandler } from "../src/teams/messageExtension.js";
import { TranslationPipeline } from "../src/services/translationPipeline.js";
import { TranslationError, BudgetExceededError } from "../src/utils/errors.js";

test("message extension handles translation success", async () => {
  const pipeline = new TranslationPipeline({
    router: {
      translate: async () => ({ text: "hola", detectedLanguage: "en", modelId: "mock", requestLatencyMs: 100 })
    },
    offlineDraftStore: { saveDraft: () => ({}) },
    translateAndReply: undefined
  });
  pipeline.translateAndReply = TranslationPipeline.prototype.translateAndReply.bind(pipeline);
  const handler = new MessageExtensionHandler({ pipeline });
  const result = await handler.handleTranslateCommand({
    text: "hello",
    targetLanguage: "es",
    tenantId: "tenantA",
    userId: "userA",
    channelId: "channelA"
  });
  assert.equal(result.replyPayload.type, "AdaptiveCard");
});

test("message extension renders budget error", async () => {
  const pipeline = new TranslationPipeline({
    router: {
      translate: async () => {
        throw new BudgetExceededError("budget exceeded");
      }
    },
    offlineDraftStore: { saveDraft: () => ({}) }
  });
  pipeline.translateAndReply = async () => {
    throw new BudgetExceededError("budget exceeded");
  };
  const handler = new MessageExtensionHandler({ pipeline });
  const card = await handler.handleTranslateCommand({ text: "hi", targetLanguage: "es" });
  assert.equal(card.body[0].text, "预算已用尽");
});

test("message extension renders translation error", async () => {
  const pipeline = new TranslationPipeline({
    router: {
      translate: async () => {
        throw new TranslationError("model failure");
      }
    },
    offlineDraftStore: { saveDraft: () => ({}) }
  });
  pipeline.translateAndReply = async () => {
    throw new TranslationError("model failure");
  };
  const handler = new MessageExtensionHandler({ pipeline });
  const card = await handler.handleTranslateCommand({ text: "hi", targetLanguage: "es" });
  assert.equal(card.body[0].text, "翻译失败");
});
