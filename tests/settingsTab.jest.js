import { initSettingsPage, renderGlossaryList } from "../src/webapp/settingsTab.js";

describe("settings tab glossary management", () => {
  test("renderGlossaryList writes human readable entries", () => {
    const container = document.createElement("ul");
    renderGlossaryList(container, [
      { source: "CPU", target: "中央处理器", scope: "tenant:contoso" },
      { source: "GPU", target: "图形处理器" }
    ]);
    expect(container.children).toHaveLength(2);
    expect(container.textContent).toContain("CPU");
    expect(container.textContent).toContain("tenant:contoso");
  });

  test("initSettingsPage uploads glossary file and renders conflicts", async () => {
    document.body.innerHTML = `
      <form data-glossary-form>
        <select data-glossary-scope>
          <option value="tenant" selected>租户</option>
        </select>
        <input data-glossary-scope-id value="contoso" />
        <input type="file" data-glossary-file />
        <input type="checkbox" data-glossary-overwrite />
        <button type="submit">提交</button>
      </form>
      <div data-glossary-progress hidden>
        <progress value="0" max="100"></progress>
        <span data-glossary-progress-label></span>
      </div>
      <div data-glossary-errors hidden>
        <ul data-glossary-error-list></ul>
      </div>
      <div data-glossary-conflicts hidden>
        <ul data-glossary-conflict-list></ul>
      </div>
      <ul data-glossary-list></ul>
      <ul data-banned-term-list></ul>
      <ul data-style-template-list></ul>
      <p data-settings-status></p>
    `;

    const capturedForms = [];
    const formDataFactory = () => {
      const store = new Map();
      const formData = {
        store,
        set(name, value) {
          store.set(name, value);
        },
        append(name, value) {
          if (!this.files) {
            this.files = [];
          }
          this.files.push({ name, value });
          store.set(name, value);
        },
        get(name) {
          return store.get(name);
        }
      };
      capturedForms.push(formData);
      return formData;
    };

    const initialEntries = [
      { source: "CPU", target: "中央处理器", scope: "tenant:contoso" }
    ];
    const uploadedEntries = [
      { source: "CPU", target: "处理器", scope: "tenant:contoso" },
      { source: "GPU", target: "显卡", scope: "tenant:contoso" }
    ];
    let glossaryFetchCount = 0;

    const fetchMock = jest.fn(async (url, options = {}) => {
      if (url.startsWith("/api/localization/catalog")) {
        return {
          ok: true,
          json: async () => ({
            locale: "ja-JP",
            defaultLocale: "ja-JP",
            displayName: "日本語 (日本)",
            strings: {}
          })
        };
      }
      if (url === "/api/configuration") {
        return {
          ok: true,
          json: async () => ({
            tenantPolicies: {
              tenantId: "contoso",
              bannedTerms: ["NDA"],
              styleTemplates: ["corporate"]
            }
          })
        };
      }
      if (url === "/api/glossary" && (!options.method || options.method === "GET")) {
        glossaryFetchCount += 1;
        return {
          ok: true,
          json: async () => (glossaryFetchCount === 1 ? initialEntries : uploadedEntries)
        };
      }
      if (url === "/api/glossary/upload") {
        expect(options.method).toBe("POST");
        expect(options.body).toBe(capturedForms[0]);
        return {
          ok: true,
          json: async () => ({
            imported: 1,
            updated: 0,
            conflicts: [
              {
                source: "CPU",
                existingTarget: "中央处理器",
                incomingTarget: "处理器",
                scope: "tenant:contoso"
              }
            ],
            errors: []
          })
        };
      }
      throw new Error(`Unhandled request: ${url}`);
    });

    const fileInput = document.querySelector("[data-glossary-file]");
    const mockFile = new File(["source,target\nGPU,显卡"], "terms.csv", { type: "text/csv" });
    Object.defineProperty(fileInput, "files", {
      value: [mockFile]
    });

    const elements = await initSettingsPage({ fetchImpl: fetchMock, formDataFactory });
    expect(elements.scopeInput.value).toBe("contoso");

    elements.form.dispatchEvent(new Event("submit", { bubbles: true, cancelable: true }));
    await new Promise((resolve) => setTimeout(resolve, 0));

    expect(fetchMock).toHaveBeenCalledWith(
      "/api/glossary/upload",
      expect.objectContaining({ method: "POST" })
    );

    const conflictList = document.querySelector("[data-glossary-conflict-list]");
    expect(conflictList.textContent).toContain("CPU");

    const glossaryList = document.querySelector("[data-glossary-list]");
    expect(glossaryList.textContent).toContain("GPU");

    const bannedList = document.querySelector("[data-banned-term-list]");
    expect(bannedList.textContent).toContain("NDA");

    const statusLabel = document.querySelector("[data-settings-status]");
    expect(statusLabel.textContent).toMatch(/[0-9]/);
  });
});
