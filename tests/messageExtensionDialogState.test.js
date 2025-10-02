import test from "node:test";
import assert from "node:assert/strict";
import {
  buildDialogState,
  calculateCostHint,
  buildTranslatePayload,
  buildReplyPayload,
  updateStateWithResponse
} from "../src/teamsClient/state.js";

test("buildDialogState chooses default language from context locale", () => {
  const models = [
    { id: "model-a" },
    { id: "model-b" }
  ];
  const languages = [
    { id: "auto", name: "Auto", isDefault: true },
    { id: "ja", name: "日本語" },
    { id: "en", name: "English" }
  ];
  const state = buildDialogState({ models, languages, context: { app: { locale: "ja-JP" } } });
  assert.equal(state.targetLanguage, "ja");
  assert.equal(state.modelId, "model-a");
  assert.equal(state.useRag, false);
  assert.deepEqual(state.contextHints, []);
  assert.deepEqual(state.additionalTargetLanguages, []);
});

test("calculateCostHint multiplies characters and cost", () => {
  const hint = calculateCostHint({ charCount: 120, modelId: "model-a" }, [{ id: "model-a", costPerCharUsd: 0.00002 }], {
    currency: "USD"
  });
  assert.equal(hint.includes("0.002400"), true);
});

test("buildTranslatePayload forwards metadata and context", () => {
  const state = {
    text: "Hello",
    sourceLanguage: "auto",
    targetLanguage: "zh-Hans",
    modelId: "model-a",
    useTerminology: false,
    tone: "formal",
    additionalTargetLanguages: ["ja", "zh-Hans", "en", "ja"]
  };
  const context = { tenant: { id: "tenant1" }, user: { id: "user1" }, channel: { id: "channel1" } };
  const payload = buildTranslatePayload(state, context);
  assert.equal(payload.targetLanguage, "zh-Hans");
  assert.equal(payload.sourceLanguage, undefined);
  assert.equal(payload.useRag, false);
  assert.deepEqual(payload.contextHints, []);
  assert.deepEqual(payload.additionalTargetLanguages, ["ja", "en"]);
  assert.deepEqual(payload.metadata, {
    origin: "messageExtension",
    modelId: "model-a",
    useTerminology: false,
    tone: "formal"
  });
});

test("buildTranslatePayload sanitizes rag preferences", () => {
  const state = {
    text: "Hello",
    sourceLanguage: "en",
    targetLanguage: "ja",
    modelId: "model-b",
    useTerminology: true,
    tone: "neutral",
    useRag: true,
    contextHints: ["  budget ", "", "contract"]
  };
  const context = { tenant: { id: "tenant" }, user: { id: "user" } };
  const payload = buildTranslatePayload(state, context);
  assert.equal(payload.useRag, true);
  assert.deepEqual(payload.contextHints, ["budget", "contract"]);
});

test("buildReplyPayload reuses rag preferences", () => {
  const state = {
    text: "Hello",
    sourceLanguage: "auto",
    detectedLanguage: "en",
    targetLanguage: "ja",
    modelId: "model-b",
    useTerminology: true,
    tone: "neutral",
    useRag: true,
    contextHints: ["pricing"],
    additionalTargetLanguages: ["en", "ja", "fr"]
  };
  const context = { tenant: { id: "tenant" }, user: { id: "user" }, channel: { id: "channel" } };
  const payload = buildReplyPayload(state, context, "こんにちは");
  assert.equal(payload.replyText, "こんにちは");
  assert.equal(payload.text, "こんにちは");
  assert.equal("translation" in payload, false);
  assert.equal(payload.useRag, true);
  assert.deepEqual(payload.contextHints, ["pricing"]);
  assert.deepEqual(payload.additionalTargetLanguages, ["en", "fr"]);
});

test("updateStateWithResponse stores translation text", () => {
  const state = { text: "Hello", translation: "", modelId: "model-a", tone: "neutral" };
  const response = { text: "你好", metadata: { modelId: "model-b", tone: "formal" }, detectedLanguage: "zh-Hans" };
  const next = updateStateWithResponse(state, response);
  assert.equal(next.translation, "你好");
  assert.equal(next.modelId, "model-b");
  assert.equal(next.tone, "formal");
  assert.equal(next.detectedLanguage, "zh-Hans");
});
