import test from "node:test";
import assert from "node:assert/strict";
import { BotCommandProcessor } from "../src/teams/botCommands.js";
import { TranslationPipeline } from "../src/services/translationPipeline.js";

test("translate command delegates to pipeline", async () => {
  const pipeline = new TranslationPipeline({ router: {}, offlineDraftStore: { saveDraft() {} } });
  let calledPayload;
  pipeline.translateAndReply = async (payload) => {
    calledPayload = payload;
    return { replyPayload: { type: "AdaptiveCard", body: [] } };
  };
  const processor = new BotCommandProcessor({ pipeline });
  const card = await processor.handleCommand({
    text: "/translate to=ja hello world",
    tenantId: "tenant",
    userId: "user",
    channelId: "channel"
  });
  assert.equal(card.type, "AdaptiveCard");
  assert.equal(calledPayload.targetLanguage, "ja");
  assert.equal(calledPayload.text, "hello world");
  assert.equal(calledPayload.metadata.origin, "botCommand");
});

test("unknown command returns help card", async () => {
  const pipeline = new TranslationPipeline({ router: {}, offlineDraftStore: { saveDraft() {} } });
  pipeline.translateAndReply = async () => ({ replyPayload: {} });
  const processor = new BotCommandProcessor({ pipeline });
  const card = await processor.handleCommand({ text: "/unsupported", tenantId: "t" });
  assert.equal(card.body[0].text, "翻译助手命令");
});
