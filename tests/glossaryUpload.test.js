import test from "node:test";
import assert from "node:assert/strict";
import {
  handleGlossaryUpload,
  renderGlossaryUploadFeedback,
  renderGlossaryEntries
} from "../src/webapp/app.js";
import { getString, formatString } from "../src/webapp/localization.js";

class StubFormData {
  constructor() {
    this.store = new Map();
  }

  set(name, value) {
    this.store.set(name, value);
  }

  append(name, value) {
    this.store.set(name, value);
  }

  get(name) {
    return this.store.get(name);
  }
}

function createElements() {
  return {
    form: { addEventListener() {} },
    scopeInput: { value: "tenant" },
    fileInput: { files: [] },
    overrideInput: { checked: false },
    submitButton: { disabled: false },
    resultsContainer: { hidden: true },
    summaryLabel: { textContent: "" },
    importedCount: { textContent: "" },
    updatedCount: { textContent: "" },
    conflictCount: { textContent: "" },
    errorCount: { textContent: "" },
    conflictContainer: { hidden: true },
    conflictList: { innerHTML: "" },
    errorContainer: { hidden: true },
    errorList: { innerHTML: "" },
    statusLabel: { textContent: "" },
    glossaryList: { innerHTML: "" }
  };
}

test("renderGlossaryUploadFeedback populates metrics and toggles sections", () => {
  const elements = createElements();
  renderGlossaryUploadFeedback(elements, {
    imported: 2,
    updated: 1,
    conflicts: [
      { source: "CPU", existingTarget: "中央处理器", incomingTarget: "处理器", scope: "tenant:contoso" }
    ],
    errors: ["行 1: 缺少源词"]
  });

  assert.equal(elements.resultsContainer.hidden, false);
  assert.equal(elements.importedCount.textContent, "2");
  assert.equal(elements.updatedCount.textContent, "1");
  assert.equal(elements.conflictCount.textContent, "1");
  assert.equal(elements.errorCount.textContent, "1");
  assert.equal(elements.conflictContainer.hidden, false);
  assert.equal(elements.errorContainer.hidden, false);
  assert.ok(elements.conflictList.innerHTML.includes("CPU"));
  assert.ok(elements.errorList.innerHTML.includes("缺少源词"));
});

test("handleGlossaryUpload sends form data and refreshes glossary", async () => {
  const elements = createElements();
  const file = { name: "terms.csv" };
  elements.fileInput.files = [file];

  const requests = [];
  const formDataInstances = [];
  const fetchMock = async (url, options = {}) => {
    requests.push({ url, options });
    if (url === "/api/glossary/upload") {
      return {
        ok: true,
        json: async () => ({ imported: 2, updated: 1, conflicts: [], errors: [] })
      };
    }
    if (url === "/api/glossary") {
      return {
        ok: true,
        json: async () => [{ source: "CPU", target: "处理器", scope: "tenant:contoso" }]
      };
    }
    throw new Error(`Unexpected request: ${url}`);
  };

  await handleGlossaryUpload({
    elements,
    fetchImpl: fetchMock,
    formDataFactory: () => {
      const fd = new StubFormData();
      formDataInstances.push(fd);
      return fd;
    }
  });

  assert.equal(requests.length, 2);
  assert.equal(requests[0].url, "/api/glossary/upload");
  assert.equal(requests[1].url, "/api/glossary");
  assert.equal(formDataInstances[0].get("scope"), "tenant");
  assert.equal(formDataInstances[0].get("overwrite"), "false");
  assert.equal(formDataInstances[0].get("file"), file);
  assert.equal(elements.submitButton.disabled, false);
  assert.equal(elements.resultsContainer.hidden, false);
  assert.ok(elements.summaryLabel.textContent.includes("2"));
  assert.ok(elements.glossaryList.innerHTML.includes("CPU"));
  const expectedSummary = formatString(getString("tla.settings.upload.summary"), 2, 1);
  assert.equal(elements.statusLabel.textContent, expectedSummary);
});

test("handleGlossaryUpload reports conflicts and keeps glossary refreshed", async () => {
  const elements = createElements();
  const file = { name: "terms.csv" };
  elements.fileInput.files = [file];

  const requests = [];
  const fetchMock = async (url, options = {}) => {
    requests.push({ url, options });
    if (url === "/api/glossary/upload") {
      return {
        ok: true,
        json: async () => ({
          imported: 1,
          updated: 0,
          conflicts: [
            { source: "CPU", existingTarget: "中央处理器", incomingTarget: "处理器", scope: "tenant:contoso" }
          ],
          errors: []
        })
      };
    }
    if (url === "/api/glossary") {
      return {
        ok: true,
        json: async () => []
      };
    }
    throw new Error(`Unexpected request: ${url}`);
  };

  await handleGlossaryUpload({ elements, fetchImpl: fetchMock, formDataFactory: () => new StubFormData() });

  assert.equal(requests.length, 2);
  assert.equal(elements.conflictContainer.hidden, false);
  assert.ok(elements.conflictList.innerHTML.includes("CPU"));
});

test("handleGlossaryUpload surfaces server validation errors", async () => {
  const elements = createElements();
  const file = { name: "terms.csv" };
  elements.fileInput.files = [file];

  const requests = [];
  const fetchMock = async (url, options = {}) => {
    requests.push({ url, options });
    if (url === "/api/glossary/upload") {
      return {
        ok: false,
        status: 400,
        json: async () => ({ error: "文件无效", errors: ["行 2: 缺少译文"] })
      };
    }
    throw new Error(`Unexpected request: ${url}`);
  };

  await handleGlossaryUpload({ elements, fetchImpl: fetchMock, formDataFactory: () => new StubFormData() });

  assert.equal(requests.length, 1);
  assert.equal(elements.errorContainer.hidden, false);
  assert.ok(elements.errorList.innerHTML.includes("行 2"));
  assert.ok(elements.statusLabel.textContent.includes("文件无效"));
});

test("renderGlossaryEntries prints placeholder when list empty", () => {
  const listElement = { innerHTML: "" };
  renderGlossaryEntries(listElement, []);
  assert.ok(listElement.innerHTML.includes(getString("tla.settings.glossary.empty")));
});
