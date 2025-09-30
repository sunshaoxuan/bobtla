import test from "node:test";
import assert from "node:assert/strict";
import { buildDialogState, calculateCostHint, buildTranslatePayload, updateStateWithResponse } from "../src/teamsClient/state.js";

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
    tone: "formal"
  };
  const context = { tenant: { id: "tenant1" }, user: { id: "user1" }, channel: { id: "channel1" } };
  const payload = buildTranslatePayload(state, context);
  assert.equal(payload.targetLanguage, "zh-Hans");
  assert.equal(payload.sourceLanguage, undefined);
  assert.deepEqual(payload.metadata, {
    origin: "messageExtension",
    modelId: "model-a",
    useTerminology: false,
    tone: "formal"
  });
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
