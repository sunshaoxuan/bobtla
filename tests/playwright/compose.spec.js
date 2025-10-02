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
  return readFileSync(normalized);
}

function buildDefaultMetadata() {
  return {
    models: [{ id: "model-a", displayName: "Model A", costPerCharUsd: 0.00002 }],
    languages: [
      { id: "auto", name: "Auto", isDefault: true },
      { id: "es", name: "Español" },
      { id: "ja", name: "日本語" }
    ],
    features: { terminologyToggle: true, toneToggle: true },
    pricing: { currency: "USD" }
  };
}

async function setupComposePage(page, { translateCalls, replyCalls, metadataHandler, metadata = buildDefaultMetadata() } = {}) {
  await page.route("http://local.test/**", async (route) => {
    const url = new URL(route.request().url());
    const relative = url.pathname.replace(/^\//, "");
    const target = path.join(srcRoot, relative);
    const normalized = path.normalize(target);
    if (normalized.startsWith(srcRoot)) {
      const body = readFileFromSrc(relative);
      const ext = path.extname(normalized);
      const contentType =
        ext === ".html"
          ? "text/html"
          : ext === ".css"
            ? "text/css"
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

  await page.route("**/api/translate", async (route) => {
    const payload = JSON.parse(route.request().postData() ?? "{}");
    translateCalls?.push(payload);
    await route.fulfill({
      status: 200,
      headers: { "content-type": "application/json" },
      body: JSON.stringify({
        text: "hola",
        detectedLanguage: "en",
        metadata: { modelId: "model-a", tone: payload.metadata?.tone ?? "neutral" }
      })
    });
  });

  await page.route("**/api/reply", async (route) => {
    const payload = JSON.parse(route.request().postData() ?? "{}");
    replyCalls?.push(payload);
    const replyText = payload.replyText ?? payload.text ?? payload.translation ?? "";
    await route.fulfill({
      status: 200,
      headers: { "content-type": "application/json" },
      body: JSON.stringify({
        status: "ok",
        card: { type: "AdaptiveCard", body: [{ type: "TextBlock", text: replyText }] }
      })
    });
  });

  await page.addInitScript(() => {
    window.sentMessages = [];
    window.microsoftTeams = {
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
      },
      conversations: {
        async sendMessageToConversation(message) {
          window.sentMessages.push(message);
        }
      }
    };
  });

  await page.goto("http://local.test/webapp/compose.html");
}

test("compose page delegates to teams compose plugin", async ({ page }) => {
  const translateCalls = [];
  const replyCalls = [];
  await setupComposePage(page, { translateCalls, replyCalls });

  const input = page.locator("[data-compose-input]");
  const targetSelect = page.locator("[data-compose-target]");
  const toneToggle = page.locator("[data-tone-toggle]");
  const costHint = page.locator("[data-compose-cost]");
  const suggestButton = page.locator("[data-compose-suggest]");
  const applyButton = page.locator("[data-compose-apply]");
  const preview = page.locator("[data-compose-preview]");

  await targetSelect.selectOption("es");
  await toneToggle.check();
  await input.fill("hello compose");
  await expect(costHint).toContainText("USD");

  await suggestButton.click();
  await expect(preview).toHaveValue("hola");

  expect(translateCalls).toHaveLength(1);
  expect(translateCalls[0].targetLanguage).toBe("es");
  expect(translateCalls[0].metadata.tone).toBe("formal");

  await applyButton.click();

  expect(replyCalls).toHaveLength(1);
  expect(replyCalls[0].metadata.tone).toBe("formal");
  expect(replyCalls[0].replyText).toBe("hola");
  expect(replyCalls[0].text).toBe("hola");

  const sentMessages = await page.evaluate(() => window.sentMessages);
  expect(sentMessages).toHaveLength(1);
  expect(sentMessages[0].attachments[0].content.type).toBe("AdaptiveCard");
  await expect(preview).toHaveValue("已发送 Adaptive Card 回贴");
});

test("compose page falls back to local metadata when endpoint fails", async ({ page }) => {
  await setupComposePage(page, {
    metadataHandler: async (route) => {
      await route.fulfill({
        status: 500,
        headers: { "content-type": "application/json" },
        body: JSON.stringify({ error: "metadata unavailable" })
      });
    }
  });

  const targetOptions = await page
    .locator("[data-compose-target] option")
    .evaluateAll((options) => options.map((option) => option.value));
  expect(targetOptions).toContain("fr");

  const costHint = page.locator("[data-compose-cost]");
  await expect(costHint).toContainText("azureOpenAI:gpt-4o");
});
