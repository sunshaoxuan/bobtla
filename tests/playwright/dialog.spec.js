import { test, expect } from "@playwright/test";
import { readFileSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const repoRoot = path.resolve(__dirname, "../..");
const srcRoot = path.join(repoRoot, "src");

function readFileFromSrc(relativePath) {
  const absolute = path.join(srcRoot, relativePath);
  const normalized = path.normalize(absolute);
  if (!normalized.startsWith(srcRoot)) {
    throw new Error(`Attempted to read outside src: ${relativePath}`);
  }
  return readFileSync(normalized, "utf8");
}

test.beforeEach(async ({ page }) => {
  await page.route("http://local.test/**", async (route) => {
    const url = new URL(route.request().url());
    const relative = url.pathname.replace(/^\//, "");
    const target = path.join(srcRoot, relative);
    const normalized = path.normalize(target);
    if (normalized.startsWith(srcRoot)) {
      const body = readFileSync(normalized);
      const contentType = normalized.endsWith(".css") ? "text/css" : "text/javascript";
      await route.fulfill({
        status: 200,
        headers: { "content-type": contentType },
        body
      });
      return;
    }
    await route.fallback();
  });

  const metadata = {
    models: [{ id: "model-a", displayName: "Model A", costPerCharUsd: 0.00002 }],
    languages: [
      { id: "auto", name: "Auto", isDefault: true },
      { id: "es", name: "Español" },
      { id: "ja", name: "日本語" }
    ],
    features: { terminologyToggle: true, toneToggle: true },
    pricing: { currency: "USD" }
  };

  await page.route("**/api/metadata", async (route) => {
    await route.fulfill({
      status: 200,
      headers: { "content-type": "application/json" },
      body: JSON.stringify(metadata)
    });
  });

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

  const html = readFileFromSrc("webapp/dialog.html");
  const sanitized = html.replace('<script type="module" src="./dialog.js"></script>', "");
  const injection = `
    <script type="module">
      import { initMessageExtensionDialog } from "http://local.test/teamsClient/messageExtensionDialog.js";
      window.__initDialog = initMessageExtensionDialog;
    </script>
  `;
  const patched = sanitized.replace("</body>", `${injection}</body>`);
  await page.setContent(patched, { waitUntil: "load" });
  await page.evaluate(() => window.__initDialog());
});

test("switching language updates translate payload", async ({ page }) => {
  const sourceInput = page.locator("[data-source-text]");
  const previewButton = page.locator("[data-preview-translation]");
  const translationInput = page.locator("[data-translation-text]");
  const targetSelect = page.locator("[data-target-select]");

  await sourceInput.fill("hello world");
  await previewButton.click();
  await expect(translationInput).toHaveValue("hola");
  await targetSelect.selectOption("ja");
  await sourceInput.fill("good night");
  await previewButton.click();
  await expect(translationInput).toHaveValue("こんにちは");
  const detected = await page.locator("[data-detected-language]").innerText();
  expect(detected).toContain("en");
});

test("edited translation is rewritten before reply", async ({ page }) => {
  const sourceInput = page.locator("[data-source-text]");
  const previewButton = page.locator("[data-preview-translation]");
  const translationInput = page.locator("[data-translation-text]");
  const submitButton = page.locator("[data-submit-translation]");

  await sourceInput.fill("hello there");
  await previewButton.click();
  await translationInput.fill("hola team");
  await submitButton.click();
  await expect(translationInput).toHaveValue("【润色】hola team");
  const lastSubmit = await page.evaluate(() => window.microsoftTeams.dialog.lastSubmit);
  expect(lastSubmit.translation).toBe("【润色】hola team");
  expect(lastSubmit.replyStatus).toBe("ok");
  expect(lastSubmit.tone).toBe("formal");
});
