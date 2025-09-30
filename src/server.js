import http from "http";
import { MockModelProvider } from "./models/modelProvider.js";
import { LanguageDetector } from "./services/languageDetector.js";
import { GlossaryManager } from "./services/glossaryManager.js";
import { BudgetGuard } from "./services/budgetGuard.js";
import { AuditLogger } from "./services/auditLogger.js";
import { OfflineDraftStore } from "./services/offlineDraftStore.js";
import { TranslationRouter } from "./services/translationRouter.js";
import { ComplianceGateway } from "./services/complianceGateway.js";
import { TranslationPipeline } from "./services/translationPipeline.js";
import { MessageExtensionHandler } from "./teams/messageExtension.js";
import { DEFAULT_MODEL_ALLOW_LIST, compliancePolicy } from "./config.js";

function buildMetadata() {
  return {
    models: DEFAULT_MODEL_ALLOW_LIST.map((model) => ({
      id: model.id,
      displayName: model.id,
      costPerCharUsd: model.costPerCharUsd,
      latencyTargetMs: model.latencyTargetMs
    })),
    languages: [
      { id: "auto", name: "自动检测", isDefault: true },
      { id: "en", name: "English" },
      { id: "zh-Hans", name: "简体中文" },
      { id: "ja", name: "日本語" },
      { id: "fr", name: "Français" }
    ],
    features: {
      terminologyToggle: true,
      offlineDraft: true
    },
    pricing: {
      currency: "USD"
    }
  };
}

function buildHandler() {
  const providers = DEFAULT_MODEL_ALLOW_LIST.map((config) =>
    new MockModelProvider({
      id: config.id,
      costPerCharUsd: config.costPerCharUsd,
      latencyTargetMs: config.latencyTargetMs,
      regions: config.regions,
      certifications: config.certifications
    })
  );
  const glossary = new GlossaryManager();
  glossary.loadBulk("tenant", [
    { source: "cpu", target: "中央处理器", metadata: { strategy: "mixed" } },
    { source: "compliance", target: "合规", metadata: {} }
  ]);
  const detector = new LanguageDetector(providers);
  const budget = new BudgetGuard({ dailyBudgetUsd: 20 });
  const audit = new AuditLogger({});
  const drafts = new OfflineDraftStore({});
  const compliance = new ComplianceGateway({ policy: compliancePolicy });
  const router = new TranslationRouter({
    providers,
    budgetGuard: budget,
    glossaryManager: glossary,
    detector,
    auditLogger: audit,
    complianceGateway: compliance,
    retry: 1
  });
  const pipeline = new TranslationPipeline({ router, offlineDraftStore: drafts });
  return new MessageExtensionHandler({ pipeline });
}

export function createServer({ handler = buildHandler(), metadata = buildMetadata() } = {}) {
  return http.createServer(async (req, res) => {
    const url = new URL(req.url, `http://${req.headers.host}`);
    if (req.method === "GET" && url.pathname === "/api/metadata") {
      res.setHeader("Content-Type", "application/json");
      res.end(JSON.stringify(metadata));
      return;
    }
    if (req.method === "POST" && url.pathname === "/api/offline-draft") {
      let body = "";
      for await (const chunk of req) {
        body += chunk;
      }
      const payload = JSON.parse(body || "{}");
      const result = await handler.handleOfflineDraft(payload);
      res.setHeader("Content-Type", "application/json");
      res.statusCode = 201;
      res.end(JSON.stringify(result));
      return;
    }
    if (req.method === "GET" && url.pathname === "/api/offline-draft") {
      const userId = url.searchParams.get("userId");
      if (!userId) {
        res.statusCode = 400;
        res.setHeader("Content-Type", "application/json");
        res.end(JSON.stringify({ error: "userId is required" }));
        return;
      }
      const drafts = handler.pipeline?.listOfflineDrafts?.(userId) ?? [];
      res.setHeader("Content-Type", "application/json");
      res.end(JSON.stringify({ drafts }));
      return;
    }
    if (req.method !== "POST") {
      res.statusCode = 405;
      res.end("Method Not Allowed");
      return;
    }
    let body = "";
    for await (const chunk of req) {
      body += chunk;
    }
    try {
      const payload = JSON.parse(body || "{}");
      let result;
      if (url.pathname === "/api/detect") {
        result = await handler.detectLanguage(payload);
      } else if (url.pathname === "/api/translate") {
        result = await handler.translate(payload);
      } else if (url.pathname === "/api/rewrite") {
        result = await handler.rewrite(payload);
      } else if (url.pathname === "/api/reply") {
        result = await handler.reply(payload);
      } else if (payload.type === "offlineDraft") {
        result = await handler.handleOfflineDraft(payload);
      } else {
        result = await handler.handleTranslateCommand(payload);
      }
      res.setHeader("Content-Type", "application/json");
      res.end(JSON.stringify(result));
    } catch (error) {
      res.statusCode = 500;
      res.end(JSON.stringify({ error: error.message }));
    }
  });
}

if (process.env.NODE_ENV !== "test") {
  const server = createServer();
  const port = process.env.PORT ?? 3978;
  server.listen(port, () => {
    console.log(`TLA reference server listening on port ${port}`);
  });
}
