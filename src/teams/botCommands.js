import { TranslationPipeline } from "../services/translationPipeline.js";

export class BotCommandProcessor {
  constructor({ pipeline }) {
    if (!(pipeline instanceof TranslationPipeline)) {
      throw new Error("pipeline must be an instance of TranslationPipeline");
    }
    this.pipeline = pipeline;
  }

  parseCommand(text = "") {
    const trimmed = text.trim();
    if (!trimmed.startsWith("/")) {
      return null;
    }
    const [commandToken, ...rest] = trimmed.slice(1).split(/\s+/);
    const command = commandToken?.toLowerCase();
    const argsText = rest.join(" ").trim();
    if (!command) {
      return null;
    }
    if (command === "help") {
      return { command };
    }
    if (command === "translate") {
      const options = {};
      const words = rest;
      const remaining = [];
      for (const word of words) {
        const [key, value] = word.split("=");
        if (value) {
          options[key.toLowerCase()] = value;
        } else {
          remaining.push(word);
        }
      }
      return {
        command,
        text: remaining.join(" ").trim(),
        targetLanguage: options.to ?? options.lang ?? "en",
        sourceLanguage: options.from,
        modelId: options.model,
        useTerminology: options.terminology !== "off"
      };
    }
    if (command === "config") {
      return { command, args: argsText };
    }
    return { command: "help" };
  }

  buildHelpCard() {
    return {
      type: "AdaptiveCard",
      version: "1.5",
      body: [
        { type: "TextBlock", text: "翻译助手命令", weight: "Bolder", size: "Medium" },
        { type: "TextBlock", text: "/translate to=ja 内容 — 翻译指定文本", wrap: true },
        { type: "TextBlock", text: "/config — 查看租户配置", wrap: true },
        { type: "TextBlock", text: "/help — 查看帮助", wrap: true }
      ]
    };
  }

  buildConfigCard(config = {}) {
    return {
      type: "AdaptiveCard",
      version: "1.5",
      body: [
        { type: "TextBlock", text: "当前租户配置", weight: "Bolder" },
        {
          type: "FactSet",
          facts: [
            { title: "默认目标语言", value: config.defaultTargetLanguage ?? "未设置" },
            { title: "启用术语库", value: config.features?.terminology ? "是" : "否" },
            { title: "允许模型", value: (config.allowedModels ?? []).join(", ") || "无" }
          ]
        }
      ]
    };
  }

  async handleCommand({ text, tenantId, userId, channelId, context = {} }) {
    const parsed = this.parseCommand(text);
    if (!parsed) {
      return this.buildHelpCard();
    }
    if (parsed.command === "help") {
      return this.buildHelpCard();
    }
    if (parsed.command === "config") {
      return this.buildConfigCard(context.configuration);
    }
    if (parsed.command === "translate") {
      if (!parsed.text) {
        return {
          type: "AdaptiveCard",
          version: "1.5",
          body: [{ type: "TextBlock", text: "请提供要翻译的文本", wrap: true }]
        };
      }
      const payload = {
        text: parsed.text,
        sourceLanguage: parsed.sourceLanguage,
        targetLanguage: parsed.targetLanguage,
        tenantId,
        userId,
        channelId,
        metadata: {
          origin: "botCommand",
          modelId: parsed.modelId,
          useTerminology: parsed.useTerminology
        }
      };
      const result = await this.pipeline.translateAndReply(payload);
      return result.replyPayload;
    }
    return this.buildHelpCard();
  }
}

export default {
  BotCommandProcessor
};
