import { maxCharactersPerRequest } from "../config.js";
import { TranslationError } from "../utils/errors.js";

export class TranslationPipeline {
  constructor({ router, offlineDraftStore }) {
    this.router = router;
    this.offlineDraftStore = offlineDraftStore;
  }

  async translateText({ text, sourceLanguage, targetLanguage, tenantId, userId, channelId, metadata, useRag = false, contextHints = [] }) {
    if (!text || !text.trim()) {
      throw new TranslationError("Text is required", { code: "EMPTY_TEXT" });
    }
    if (text.length > maxCharactersPerRequest) {
      throw new TranslationError("Text exceeds maximum length", { code: "MAX_LENGTH_EXCEEDED" });
    }
    const result = await this.router.translate({
      text,
      sourceLanguage,
      targetLanguage,
      tenantId,
      userId,
      channelId,
      useRag,
      contextHints,
      metadata
    });
    return {
      text: result.text,
      detectedLanguage: result.detectedLanguage ?? sourceLanguage,
      metadata: {
        ...(metadata ?? {}),
        modelId: result.modelId ?? metadata?.modelId,
        latencyMs: result.requestLatencyMs,
        costUsd: result.requestCostUsd
      }
    };
  }

  async translateAndReply(params) {
    const result = await this.translateText(params);
    return {
      ...result,
      replyPayload: this.buildAdaptiveCard({
        translatedText: result.text,
        sourceLanguage: result.detectedLanguage ?? params.sourceLanguage,
        targetLanguage: params.targetLanguage,
        metadata: result.metadata
      })
    };
  }

  async detectLanguage({ text, tenantId, userId }) {
    if (!text?.trim()) {
      throw new TranslationError("Text is required", { code: "EMPTY_TEXT" });
    }
    const detection = await this.router.detector?.detect({ text: text.trim(), tenantId, userId });
    return {
      language: detection?.language ?? "en",
      confidence: detection?.confidence ?? 0.5
    };
  }

  async rewriteTranslation({ text, tone = "neutral", metadata = {} }) {
    if (!text?.trim()) {
      throw new TranslationError("Text is required", { code: "EMPTY_TEXT" });
    }
    const trimmed = text.trim();
    let rewritten = trimmed;
    if (tone === "formal") {
      rewritten = `【正式】${trimmed}`;
    }
    return {
      text: rewritten,
      metadata: { ...metadata, tone }
    };
  }

  async replyWithTranslation({ translation, sourceLanguage, targetLanguage, metadata = {} }) {
    if (!translation?.trim()) {
      throw new TranslationError("Text is required", { code: "EMPTY_TEXT" });
    }
    const card = this.buildAdaptiveCard({
      translatedText: translation.trim(),
      sourceLanguage,
      targetLanguage,
      metadata
    });
    return { status: "ok", card };
  }

  saveOfflineDraft({ userId, tenantId, originalText, targetLanguage }) {
    const draft = this.offlineDraftStore.saveDraft(userId, {
      tenantId,
      originalText,
      targetLanguage,
      status: "PENDING"
    });
    return draft;
  }

  buildAdaptiveCard({ translatedText, sourceLanguage, targetLanguage, metadata = {} }) {
    return {
      type: "AdaptiveCard",
      version: "1.5",
      body: [
        { type: "TextBlock", text: "译文", wrap: true, size: "Medium", weight: "Bolder" },
        { type: "TextBlock", text: translatedText, wrap: true },
        {
          type: "TextBlock",
          text: `源语言: ${sourceLanguage} → 目标语言: ${targetLanguage}`,
          wrap: true,
          isSubtle: true,
          spacing: "None"
        }
      ],
      actions: [
        { type: "Action.Submit", title: "查看原文", data: { action: "showOriginal" } },
        { type: "Action.Submit", title: "切换译文语言", data: { action: "changeLanguage", metadata } }
      ]
    };
  }
}

export default {
  TranslationPipeline
};
