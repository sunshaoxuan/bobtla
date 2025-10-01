import { test, expect } from "@playwright/test";
import { readFileSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const repoRoot = path.resolve(__dirname, "../..");
const srcRoot = path.join(repoRoot, "src");

const defaultMetadata = {
  models: [{ id: "model-a", displayName: "Model A", costPerCharUsd: 0.00002 }],
  languages: [
    { id: "auto", name: "Auto", isDefault: true },
    { id: "es", name: "Español" },
    { id: "ja", name: "日本語" }
  ],
  features: { terminologyToggle: true, toneToggle: true },
  pricing: { currency: "USD" }
};

async function setupDialogPage(page, { metadata = defaultMetadata, metadataHandler } = {}) {
  await page.route("http://local.test/**", async (route) => {
    const url = new URL(route.request().url());
    const relative = url.pathname.replace(/^\//, "");
    const target = path.join(srcRoot, relative);
    const normalized = path.normalize(target);
    if (normalized.startsWith(srcRoot)) {
      const body = readFileSync(normalized);
      const ext = path.extname(normalized);
      const contentType =
        ext === ".css"
          ? "text/css"
          : ext === ".html"
            ? "text/html"
            : "text/javascript";
      await route.fulfill({
        status: 200,
        headers: { "content-type": contentType },
        body
      });
      return;
    }
    await route.fallback();
  });

  const metadataRoute = metadataHandler
    ? metadataHandler
    : async (route) => {
        await route.fulfill({
          status: 200,
          headers: { "content-type": "application/json" },
          body: JSON.stringify(metadata)
        });
      };

  await page.route("**/api/metadata", metadataRoute);

  await page.route("**/api/detect", async (route) => {
    await route.fulfill({
      status: 200,
      headers: { "content-type": "application/json" },
      body: JSON.stringify({ language: "en", confidence: 0.88 })
    });
  });

  await page.route("**/api/translate", async (route) => {
    const payload = JSON.parse(route.request().postData() ?? "{}");
    const text = payload.targetLanguage === "ja" ? "こんにちは" : "hola";
    await route.fulfill({
      status: 200,
      headers: { "content-type": "application/json" },
      body: JSON.stringify({
        text,
        detectedLanguage: "en",
        metadata: { modelId: "model-a", tone: payload.targetLanguage === "ja" ? "formal" : "neutral" }
      })
    });
  });

  await page.route("**/api/rewrite", async (route) => {
    const payload = JSON.parse(route.request().postData() ?? "{}");
    await route.fulfill({
      status: 200,
      headers: { "content-type": "application/json" },
      body: JSON.stringify({ text: `【润色】${payload.text ?? ""}`, metadata: { tone: "formal" } })
    });
  });

  await page.route("**/api/reply", async (route) => {
    const payload = JSON.parse(route.request().postData() ?? "{}");
    await route.fulfill({
      status: 200,
      headers: { "content-type": "application/json" },
      body: JSON.stringify({ status: "ok", echo: payload, card: { type: "AdaptiveCard", body: [] } })
    });
  });

  await page.addInitScript(() => {
    window.microsoftTeams = {
      dialog: {
        submit(payload) {
          window.microsoftTeams.dialog.lastSubmit = payload;
        }
      },
      app: {
        async initialize() {
          return undefined;
        },
        async getContext() {
          return {
            tenant: { id: "tenant" },
            user: { id: "user" },
            channel: { id: "channel" },
            app: { locale: "en-US" }
          };
        }
      }
    };
  });

  await page.goto("http://local.test/webapp/dialog.html", { waitUntil: "load" });
  await page.waitForSelector("[data-source-text]");
}

test("switching language updates translate payload", async ({ page }) => {
  await setupDialogPage(page);
  const sourceInput = page.locator("[data-source-text]");
  const previewButton = page.locator("[data-preview-translation]");
  const translationInput = page.locator("[data-translation-text]");
  const targetSelect = page.locator("[data-target-select]");

  const detectRequest = page.waitForRequest("**/api/detect");
  await sourceInput.fill("hello world");
  await detectRequest;
  const translateRequest = page.waitForRequest("**/api/translate");
  await previewButton.click();
  const translatePayload = (await translateRequest).postDataJSON();
  await expect(translationInput).toHaveValue("hola");
  expect(translatePayload.useRag).toBe(false);
  expect(Array.isArray(translatePayload.contextHints)).toBe(true);
  expect(translatePayload.contextHints.length).toBe(0);
  await targetSelect.selectOption("ja");
  const secondDetect = page.waitForRequest("**/api/detect");
  await sourceInput.fill("good night");
  await secondDetect;
  const secondTranslate = page.waitForRequest("**/api/translate");
  await previewButton.click();
  await secondTranslate;
  await expect(translationInput).toHaveValue("こんにちは");
  const detected = await page.locator("[data-detected-language]").innerText();
  expect(detected).toContain("en");
});

test("edited translation is rewritten before reply", async ({ page }) => {
  await setupDialogPage(page);
  const sourceInput = page.locator("[data-source-text]");
  const previewButton = page.locator("[data-preview-translation]");
  const translationInput = page.locator("[data-translation-text]");
  const submitButton = page.locator("[data-submit-translation]");

  const detectRequest = page.waitForRequest("**/api/detect");
  await sourceInput.fill("hello there");
  await detectRequest;
  const translateRequest = page.waitForRequest("**/api/translate");
  await previewButton.click();
  await translateRequest;
  await translationInput.fill("hola team");
  const rewriteRequest = page.waitForRequest("**/api/rewrite");
  const replyRequest = page.waitForRequest("**/api/reply");
  await submitButton.click();
  await rewriteRequest;
  await replyRequest;
  await expect(translationInput).toHaveValue("【润色】hola team");
  const lastSubmit = await page.evaluate(() => window.microsoftTeams.dialog.lastSubmit);
  expect(lastSubmit.translation).toBe("【润色】hola team");
  expect(lastSubmit.replyStatus).toBe("ok");
  expect(lastSubmit.tone).toBe("formal");
});

test("rag toggle sends context hints", async ({ page }) => {
  await setupDialogPage(page);
  const ragToggle = page.locator("[data-rag-toggle]");
  const hintsInput = page.locator("[data-context-hints]");
  const sourceInput = page.locator("[data-source-text]");
  const previewButton = page.locator("[data-preview-translation]");

  await ragToggle.check();
  await hintsInput.fill("budget review\ncontract draft");
  const detectRequest = page.waitForRequest("**/api/detect");
  await sourceInput.fill("Need translation");
  await detectRequest;
  const translateRequest = page.waitForRequest("**/api/translate");
  await previewButton.click();
  const payload = (await translateRequest).postDataJSON();
  expect(payload.useRag).toBe(true);
  expect(payload.contextHints).toEqual(["budget review", "contract draft"]);
});

test("metadata endpoint failure uses fallback configuration", async ({ page }) => {
  await setupDialogPage(page, {
    metadataHandler: async (route) => {
      await route.fulfill({
        status: 500,
        headers: { "content-type": "application/json" },
        body: JSON.stringify({ error: "unavailable" })
      });
    }
  });

  const targetOptions = await page
    .locator("[data-target-select] option")
    .evaluateAll((options) => options.map((option) => option.value));
  expect(targetOptions).toContain("fr");
  const costHint = page.locator("[data-cost-hint]");
  await expect(costHint).toContainText("azureOpenAI:gpt-4o");
});
