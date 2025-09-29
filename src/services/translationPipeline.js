import { maxCharactersPerRequest } from "../config.js";
import { TranslationError } from "../utils/errors.js";

export class TranslationPipeline {
  constructor({ router, offlineDraftStore }) {
    this.router = router;
    this.offlineDraftStore = offlineDraftStore;
  }

  async translateAndReply({ text, sourceLanguage, targetLanguage, tenantId, userId, channelId, metadata }) {
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
      metadata
    });
    return {
      ...result,
      replyPayload: this.buildAdaptiveCard({
        translatedText: result.text,
        sourceLanguage: result.detectedLanguage ?? sourceLanguage,
        targetLanguage,
        metadata
      })
    };
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
