import { TranslationPipeline } from "../services/translationPipeline.js";
import { TranslationError, BudgetExceededError, ComplianceError } from "../utils/errors.js";

export class MessageExtensionHandler {
  constructor({ pipeline }) {
    if (!(pipeline instanceof TranslationPipeline)) {
      throw new Error("pipeline must be an instance of TranslationPipeline");
    }
    this.pipeline = pipeline;
  }

  async detectLanguage(payload) {
    return this.pipeline.detectLanguage(payload);
  }

  async translate(payload) {
    return this.pipeline.translateText(payload);
  }

  async rewrite(payload) {
    return this.pipeline.rewriteTranslation(payload);
  }

  async reply(payload) {
    return this.pipeline.replyWithTranslation({
      translation: payload.translation,
      sourceLanguage: payload.sourceLanguage,
      targetLanguage: payload.targetLanguage,
      metadata: payload.metadata
    });
  }

  async handleTranslateCommand(payload) {
    const {
      text,
      sourceLanguage,
      targetLanguage,
      tenantId,
      userId,
      channelId,
      useRag = false,
      contextHints = []
    } = payload ?? {};
    try {
      return await this.pipeline.translateAndReply({
        text,
        sourceLanguage,
        targetLanguage,
        tenantId,
        userId,
        channelId,
        useRag,
        contextHints,
        metadata: { command: "translate" }
      });
    } catch (error) {
      return this.handleError(error);
    }
  }

  async handleOfflineDraft({ originalText, targetLanguage, tenantId, userId }) {
    const draft = this.pipeline.saveOfflineDraft({
      userId,
      tenantId,
      originalText,
      targetLanguage
    });
    return {
      type: "offlineDraftSaved",
      draft
    };
  }

  handleError(error) {
    if (error instanceof BudgetExceededError) {
      return {
        type: "AdaptiveCard",
        version: "1.5",
        body: [
          { type: "TextBlock", text: "预算已用尽", weight: "Bolder", wrap: true },
          { type: "TextBlock", text: "请联系租户管理员调整预算或稍后再试。", wrap: true }
        ]
      };
    }
    if (error instanceof TranslationError) {
      return {
        type: "AdaptiveCard",
        version: "1.5",
        body: [
          { type: "TextBlock", text: "翻译失败", weight: "Bolder", wrap: true },
          { type: "TextBlock", text: error.message, wrap: true }
        ]
      };
    }
    if (error instanceof ComplianceError) {
      const policyDetails = error.policy?.violations?.join("；") ?? "请检查输入文本";
      return {
        type: "AdaptiveCard",
        version: "1.5",
        body: [
          { type: "TextBlock", text: "触发合规策略", weight: "Bolder", wrap: true },
          { type: "TextBlock", text: policyDetails, wrap: true }
        ]
      };
    }
    return {
      type: "AdaptiveCard",
      version: "1.5",
      body: [
        { type: "TextBlock", text: "发生未知错误", wrap: true },
        { type: "TextBlock", text: "请稍后再试，如果问题持续请联系管理员。", wrap: true }
      ]
    };
  }
}

export default {
  MessageExtensionHandler
};
