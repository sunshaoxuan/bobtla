import { test, expect } from "@playwright/test";
import { readFileSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const repoRoot = path.resolve(__dirname, "../..");
const stageRoot = path.join(repoRoot, "src", "TlaPlugin", "wwwroot");

function readStageFile(relativePath) {
  const absolute = path.join(stageRoot, relativePath);
  const normalized = path.normalize(absolute);
  if (!normalized.startsWith(stageRoot)) {
    throw new Error(`Attempted to read outside stage root: ${relativePath}`);
  }
  return readFileSync(normalized);
}

function resolveContentType(filePath) {
  const ext = path.extname(filePath).toLowerCase();
  switch (ext) {
    case ".html":
      return "text/html";
    case ".css":
      return "text/css";
    case ".js":
      return "application/javascript";
    case ".json":
      return "application/json";
    default:
      return "application/octet-stream";
  }
}

async function serveStageStatic(route) {
  const url = new URL(route.request().url());
  const relative = url.pathname.replace(/^\//, "");
  try {
    const body = readStageFile(relative);
    await route.fulfill({
      status: 200,
      headers: { "content-type": resolveContentType(relative) },
      body
    });
  } catch (error) {
    await route.fulfill({ status: 404, body: "not found" });
  }
}

async function setupStageCompose(page, { metadata, translateHandler, replyHandler, auditHandler } = {}) {
  await page.route("**/api/metadata", async (route) => {
    await route.fulfill({
      status: 200,
      headers: { "content-type": "application/json" },
      body: JSON.stringify(
        metadata ?? {
          models: [
            { id: "azure:gpt4o", displayName: "GPT-4o", costPerCharUsd: 0.00002 },
            { id: "contoso:neural", displayName: "Contoso Neural", costPerCharUsd: 0.00001 }
          ],
          languages: [
            { id: "auto", name: "自动检测", isDefault: true },
            { id: "es", name: "Español" },
            { id: "ja", name: "日本語" },
            { id: "fr", name: "Français" }
          ],
          features: { terminologyToggle: true, toneToggle: true },
          pricing: { currency: "USD", dailyBudgetUsd: 25 }
        }
      )
    });
  });

  if (translateHandler) {
    await page.route("**/api/translate", translateHandler);
  } else {
    await page.route("**/api/translate", async (route) => {
      const payload = route.request().postDataJSON();
      const additional = Array.isArray(payload.additionalTargetLanguages)
        ? payload.additionalTargetLanguages.map((lang) => ({ language: lang, text: `${lang}-translation` }))
        : [];
      await route.fulfill({
        status: 200,
        headers: { "content-type": "application/json" },
        body: JSON.stringify({
          text: "hola",
          detectedLanguage: "en",
          metadata: { modelId: "azure:gpt4o", tone: payload.metadata?.tone ?? "neutral" },
          additionalTranslations: additional
        })
      });
    });
  }

  if (replyHandler) {
    await page.route("**/api/reply", replyHandler);
  } else {
    await page.route("**/api/reply", async (route) => {
      await route.fulfill({
        status: 200,
        headers: { "content-type": "application/json" },
        body: JSON.stringify(buildReplyResponse())
      });
    });
  }

  if (auditHandler) {
    await page.route("**/api/audit", auditHandler);
  } else {
    await page.route("**/api/audit", async (route) => {
      await route.fulfill({
        status: 200,
        headers: { "content-type": "application/json" },
        body: JSON.stringify([])
      });
    });
  }

  await page.route("http://stage.test/**", serveStageStatic);

  await page.addInitScript(() => {
    window.sentMessages = [];
    window.microsoftTeams = {
      app: {
        async initialize() {
          return undefined;
        },
        async getContext() {
          return {
            tenant: { id: "contoso" },
            user: { id: "user", aadObjectId: "user-aad" },
            channel: { id: "general" },
            app: { locale: "zh-CN" }
          };
        }
      },
      authentication: {
        async getAuthToken() {
          return "stage-token";
        }
      },
      conversations: {
        async sendMessageToConversation(message) {
          window.sentMessages.push(message);
        }
      }
    };
  });

  await page.goto("http://stage.test/webapp/compose.html");
}

const defaultReplyDispatches = [
  {
    messageId: "msg-primary",
    language: "es",
    status: "sent",
    postedAt: new Date().toISOString(),
    modelId: "azure:gpt4o",
    costUsd: 0.0123,
    latencyMs: 1200
  }
];

function buildReplyResponse({
  messageId = "msg-primary",
  status = "sent",
  finalText = "hola",
  toneApplied = "formal",
  language = "es",
  dispatches = defaultReplyDispatches
} = {}) {
  return {
    messageId,
    status,
    finalText,
    toneApplied,
    language,
    postedAt: new Date().toISOString(),
    dispatches
  };
}

test.describe("Stage compose broadcast and budget guards", () => {
  test("sends additional languages without broadcast toggle", async ({ page }) => {
    const replyRequests = [];
    const replyResponses = [];
    const replyAuthHeaders = [];

    await setupStageCompose(page, {
      replyHandler: async (route) => {
        const payload = route.request().postDataJSON();
        replyRequests.push(payload);
        replyAuthHeaders.push(route.request().headers().authorization);
        const response = buildReplyResponse({
          dispatches: [
            { messageId: "msg-primary", language: "es", status: "sent", postedAt: new Date().toISOString() },
            { messageId: "msg-ja", language: "ja", status: "sent", postedAt: new Date().toISOString() }
          ]
        });
        replyResponses.push(response);
        await route.fulfill({
          status: 200,
          headers: { "content-type": "application/json" },
          body: JSON.stringify(response)
        });
      },
      auditHandler: async (route) => {
        await route.fulfill({
          status: 200,
          headers: { "content-type": "application/json" },
          body: JSON.stringify([
            {
              tenantId: "contoso",
              userId: "user",
              modelId: "azure:gpt4o",
              translation: "hola",
              costUsd: 0.02,
              latencyMs: 1100,
              translations: [
                { language: "ja", modelId: "azure:gpt4o", costUsd: 0.01, latencyMs: 900, text: "ja-translation" }
              ]
            }
          ])
        });
      }
    });

    await page.locator("[data-compose-target]").selectOption("es");
    await page.locator("[data-compose-additional-targets]").selectOption(["ja"]);
    await page.locator("[data-compose-input]").fill("Hello team");
    const translatePromise = page.waitForRequest("**/api/translate");
    await page.locator("[data-compose-suggest]").click();
    const translateRequest = await translatePromise;
    await expect(page.locator("[data-compose-preview]")).toHaveValue("hola");

    const translatePayload = translateRequest.postDataJSON();
    expect(translatePayload.additionalTargetLanguages).toEqual(["ja"]);
    expect(translateRequest.headers().authorization).toBeUndefined();

    await page.locator("[data-compose-apply]").click();

    expect(replyRequests).toHaveLength(1);
    const [requestPayload] = replyRequests;
    expect(requestPayload.broadcastAdditionalLanguages).toBe(false);
    expect(requestPayload.additionalTargetLanguages).toEqual(["ja"]);
    expect(requestPayload.metadata.tone).toBe("formal");
    expect(requestPayload.metadata.useTerminology).toBe(true);

    const [responsePayload] = replyResponses;
    expect(responsePayload.dispatches).toHaveLength(2);
    expect(replyAuthHeaders.at(-1)).toBe("Bearer stage-token");
    const messages = await page.evaluate(() => window.sentMessages);
    expect(messages).toHaveLength(1);
    expect(messages[0].attachments[0].content.type).toBe("AdaptiveCard");

    const auditSnapshot = await page.evaluate(async () => {
      const res = await fetch("/api/audit");
      return res.json();
    });
    expect(auditSnapshot).toHaveLength(1);
    expect(auditSnapshot[0].translations[0].language).toBe("ja");
  });

  test("enables broadcast flag and records audit entries", async ({ page }) => {
    const replyRequests = [];
    const replyAuthHeaders = [];

    let auditLog = [];

    await setupStageCompose(page, {
      replyHandler: async (route) => {
        const payload = route.request().postDataJSON();
        replyRequests.push(payload);
        replyAuthHeaders.push(route.request().headers().authorization);
        const response = buildReplyResponse({
          dispatches: [
            { messageId: "msg-es", language: "es", status: "sent", postedAt: new Date().toISOString() },
            { messageId: "msg-fr", language: "fr", status: "sent", postedAt: new Date().toISOString(), modelId: "contoso:neural" }
          ]
        });
        auditLog = [
          {
            tenantId: payload.tenantId,
            userId: payload.userId,
            modelId: "azure:gpt4o",
            translation: "hola",
            costUsd: 0.018,
            latencyMs: 980,
            translations: payload.additionalTargetLanguages.map((lang) => ({
              language: lang,
              modelId: lang === "fr" ? "contoso:neural" : "azure:gpt4o",
              costUsd: 0.009,
              latencyMs: 850,
              text: `${lang}-translation`
            }))
          }
        ];
        await route.fulfill({
          status: 200,
          headers: { "content-type": "application/json" },
          body: JSON.stringify(response)
        });
      },
      auditHandler: async (route) => {
        await route.fulfill({
          status: 200,
          headers: { "content-type": "application/json" },
          body: JSON.stringify(auditLog)
        });
      }
    });

    await page.locator("[data-compose-target]").selectOption("es");
    await page.locator("[data-compose-additional-targets]").selectOption(["ja", "fr"]);
    await page.locator("[data-compose-broadcast]").check();
    await page.locator("[data-compose-input]").fill("Budget review");
    await page.locator("[data-compose-suggest]").click();
    await page.locator("[data-compose-apply]").click();

    expect(replyRequests).toHaveLength(1);
    const payload = replyRequests[0];
    expect(payload.broadcastAdditionalLanguages).toBe(true);
    expect(new Set(payload.additionalTargetLanguages)).toEqual(new Set(["ja", "fr"]));
    expect(payload.metadata.tone).toBe("formal");

    const auditSnapshot = await page.evaluate(async () => {
      const res = await fetch("/api/audit");
      return res.json();
    });

    expect(auditSnapshot).toHaveLength(1);
    const translations = auditSnapshot[0]?.translations ?? [];
    expect(new Set(translations.map((entry) => entry.language))).toEqual(new Set(["ja", "fr"]));
    expect(replyAuthHeaders.at(-1)).toBe("Bearer stage-token");
  });

  test("surfaces budget guard errors when Graph rejects the reply", async ({ page }) => {
    const replyRequests = [];

    await setupStageCompose(page, {
      replyHandler: async (route) => {
        replyRequests.push(route.request().postDataJSON());
        await route.fulfill({
          status: 402,
          headers: { "content-type": "application/json" },
          body: JSON.stringify({ error: { code: "Budget", message: "预算已用尽" } })
        });
      }
    });

    await page.locator("[data-compose-target]").selectOption("es");
    await page.locator("[data-compose-input]").fill("Budget alert");
    await page.locator("[data-compose-suggest]").click();
    await page.locator("[data-compose-apply]").click();

    expect(replyRequests).toHaveLength(1);
    await expect(page.locator("[data-compose-preview]")).toHaveValue(/发送失败：/);
    const messages = await page.evaluate(() => window.sentMessages);
    expect(messages).toHaveLength(0);
  });
});
