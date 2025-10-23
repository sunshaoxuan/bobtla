import { defineConfig } from "@playwright/test";

export default defineConfig({
  testDir: "tests/playwright",
  use: {
    trace: "retain-on-failure",
    screenshot: "only-on-failure",
    video: "retain-on-failure"
  },
  reporter: [
    ["list"],
    ["html", { outputFolder: "artifacts/playwright-report", open: "never" }]
  ],
  outputDir: "artifacts/playwright-output"
});
