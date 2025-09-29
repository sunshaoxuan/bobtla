import http from "http";
import { MockModelProvider } from "./models/modelProvider.js";
import { LanguageDetector } from "./services/languageDetector.js";
import { GlossaryManager } from "./services/glossaryManager.js";
import { BudgetGuard } from "./services/budgetGuard.js";
import { AuditLogger } from "./services/auditLogger.js";
import { OfflineDraftStore } from "./services/offlineDraftStore.js";
import { TranslationRouter } from "./services/translationRouter.js";
import { TranslationPipeline } from "./services/translationPipeline.js";
import { MessageExtensionHandler } from "./teams/messageExtension.js";
import { DEFAULT_MODEL_ALLOW_LIST } from "./config.js";

function buildHandler() {
  const providers = DEFAULT_MODEL_ALLOW_LIST.map((config) => new MockModelProvider({
    id: config.id,
    costPerCharUsd: config.costPerCharUsd,
    latencyTargetMs: config.latencyTargetMs
  }));
  const glossary = new GlossaryManager();
  glossary.loadBulk("tenant", [
    { source: "cpu", target: "中央处理器", metadata: { strategy: "mixed" } },
    { source: "compliance", target: "合规", metadata: {} }
  ]);
  const detector = new LanguageDetector(providers);
  const budget = new BudgetGuard({ dailyBudgetUsd: 20 });
  const audit = new AuditLogger({});
  const drafts = new OfflineDraftStore({});
  const router = new TranslationRouter({
    providers,
    budgetGuard: budget,
    glossaryManager: glossary,
    detector,
    auditLogger: audit,
    retry: 1
  });
  const pipeline = new TranslationPipeline({ router, offlineDraftStore: drafts });
  return new MessageExtensionHandler({ pipeline });
}

export function createServer({ handler = buildHandler() } = {}) {
  return http.createServer(async (req, res) => {
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
      if (payload.type === "offlineDraft") {
        const result = await handler.handleOfflineDraft(payload);
        res.setHeader("Content-Type", "application/json");
        res.end(JSON.stringify(result));
        return;
      }
      const result = await handler.handleTranslateCommand(payload);
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
