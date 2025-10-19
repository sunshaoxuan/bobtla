import { fetchJson } from "./network.js";
import { getString, formatString, initializeLocalization } from "./localization.js";

const DEFAULT_FETCH = typeof fetch === "function" ? fetch.bind(globalThis) : undefined;

function resolveElements(root = typeof document !== "undefined" ? document : undefined) {
  if (!root) {
    return {};
  }
  return {
    form: root.querySelector?.("[data-glossary-form]"),
    scopeSelect: root.querySelector?.("[data-glossary-scope]"),
    scopeInput: root.querySelector?.("[data-glossary-scope-id]"),
    fileInput: root.querySelector?.("[data-glossary-file]"),
    overwriteCheckbox: root.querySelector?.("[data-glossary-overwrite]"),
    progressContainer: root.querySelector?.("[data-glossary-progress]"),
    progressBar: root.querySelector?.("[data-glossary-progress] progress"),
    progressLabel: root.querySelector?.("[data-glossary-progress-label]"),
    conflictContainer: root.querySelector?.("[data-glossary-conflicts]"),
    conflictList: root.querySelector?.("[data-glossary-conflict-list]"),
    errorContainer: root.querySelector?.("[data-glossary-errors]"),
    errorList: root.querySelector?.("[data-glossary-error-list]"),
    glossaryList: root.querySelector?.("[data-glossary-list]"),
    bannedList: root.querySelector?.("[data-banned-term-list]"),
    styleList: root.querySelector?.("[data-style-template-list]"),
    statusLabel: root.querySelector?.("[data-settings-status]")
  };
}

function renderList(container, items, emptyText) {
  if (!container) {
    return;
  }
  const values = Array.isArray(items) ? items : [];
  if (typeof container.replaceChildren === "function" && typeof document !== "undefined") {
    if (values.length === 0 && emptyText) {
      const placeholder = document.createElement("li");
      placeholder.textContent = emptyText;
      container.replaceChildren(placeholder);
      return;
    }
    const nodes = values.map((value) => {
      const li = document.createElement("li");
      li.textContent = typeof value === "string" ? value : String(value ?? "");
      return li;
    });
    container.replaceChildren(...nodes);
  } else {
    container.items = values;
    if (emptyText) {
      container.emptyText = values.length === 0 ? emptyText : "";
    }
  }
}

export function renderGlossaryList(container, entries) {
  if (!container) {
    return;
  }
  const items = Array.isArray(entries)
    ? entries.map((entry) => {
        const scope = entry?.scope ? `（${entry.scope}）` : "";
        return `${entry?.source ?? ""} → ${entry?.target ?? ""}${scope}`;
      })
    : [];
  renderList(container, items, getString("tla.settings.glossary.empty"));
}

export function renderConflictList(container, conflicts) {
  if (!container) {
    return;
  }
  const rows = Array.isArray(conflicts)
    ? conflicts.map((conflict) =>
        `${conflict?.source ?? ""}: ${conflict?.existingTarget ?? ""} → ${conflict?.incomingTarget ?? ""}`
      )
    : [];
  renderList(container, rows, getString("tla.settings.glossary.noConflicts"));
}

export function renderErrorList(container, errors) {
  if (!container) {
    return;
  }
  const lines = Array.isArray(errors) ? errors.filter(Boolean) : [];
  renderList(container, lines, "");
}

function setTextContent(element, value) {
  if (!element) {
    return;
  }
  if ("textContent" in element) {
    element.textContent = value;
  } else {
    element.value = value;
  }
}

function setInnerHtml(element, html) {
  if (!element) {
    return;
  }
  if ("innerHTML" in element) {
    element.innerHTML = html;
  } else {
    element.content = html;
  }
}

export function renderGlossaryEntries(container, entries) {
  renderGlossaryList(container, entries);
  if (!container) {
    return;
  }

  const items = Array.isArray(entries) && entries.length > 0
    ? entries.map((entry) => {
        const scope = entry?.scope ? `（${entry.scope}）` : "";
        return `${entry?.source ?? ""} → ${entry?.target ?? ""}${scope}`;
      })
    : [getString("tla.settings.glossary.empty")];

  const markup = items.map((item) => `<li>${item}</li>`).join("");
  setInnerHtml(container, markup);
}

export function renderGlossaryUploadFeedback(elements, result = {}) {
  if (!elements) {
    return;
  }

  const imported = Number(result.imported ?? 0);
  const updated = Number(result.updated ?? 0);
  const conflicts = Array.isArray(result.conflicts) ? result.conflicts : [];
  const errors = Array.isArray(result.errors) ? result.errors : [];

  updateVisibility(elements.resultsContainer, true);
  setTextContent(elements.importedCount, String(imported));
  setTextContent(elements.updatedCount, String(updated));
  setTextContent(elements.conflictCount, String(conflicts.length));
  setTextContent(elements.errorCount, String(errors.length));

  renderConflictList(elements.conflictList, conflicts);
  const conflictMarkup = conflicts
    .map((conflict) => {
      const scope = conflict?.scope ? `（${conflict.scope}）` : "";
      return `<li>${conflict?.source ?? ""}: ${conflict?.existingTarget ?? ""} → ${conflict?.incomingTarget ?? ""}${scope}</li>`;
    })
    .join("");
  setInnerHtml(elements.conflictList, conflictMarkup);

  renderErrorList(elements.errorList, errors);
  const errorMarkup = errors.map((error) => `<li>${error}</li>`).join("");
  setInnerHtml(elements.errorList, errorMarkup);

  updateVisibility(elements.conflictContainer, conflicts.length > 0);
  updateVisibility(elements.errorContainer, errors.length > 0);

  const summaryTemplate = getString("tla.settings.upload.summary");
  const summary = typeof result.message === "string" && result.message.trim() !== ""
    ? result.message
    : formatString(summaryTemplate, imported, updated);
  if (elements.summaryLabel) {
    elements.summaryLabel.textContent = summary;
  }
  if (elements.statusLabel) {
    elements.statusLabel.textContent = summary;
  }
}

function updateVisibility(element, visible) {
  if (!element) {
    return;
  }
  if ("hidden" in element) {
    element.hidden = !visible;
  } else {
    element.isVisible = visible;
  }
}

function updateProgress(elements, value, label) {
  if (!elements.progressContainer) {
    return;
  }
  updateVisibility(elements.progressContainer, true);
  if (elements.progressBar) {
    elements.progressBar.value = value;
  } else {
    elements.progressContainer.value = value;
  }
  if (elements.progressLabel) {
    elements.progressLabel.textContent = label;
  } else {
    elements.progressContainer.label = label;
  }
}

async function refreshGlossary(elements, fetchImpl) {
  try {
    const entries = await fetchJson("/api/glossary", {
      fetchImpl,
      toastMessage: () => getString("tla.toast.settings.glossaryFetch"),
      toastKey: "settings-glossary"
    });
    renderGlossaryEntries(elements.glossaryList, entries);
  } catch (error) {
    console.warn("无法加载术语列表", error);
    renderGlossaryEntries(elements.glossaryList, []);
  }
}

function renderPolicies(elements, policies) {
  if (!policies) {
    renderList(elements.bannedList, [], getString("tla.settings.policies.noBannedTerms"));
    renderList(elements.styleList, [], getString("tla.settings.policies.noStyleTemplates"));
    return;
  }
  renderList(elements.bannedList, policies.bannedTerms ?? [], getString("tla.settings.policies.noBannedTerms"));
  renderList(elements.styleList, policies.styleTemplates ?? [], getString("tla.settings.policies.noStyleTemplates"));
  if (elements.scopeInput && !elements.scopeInput.value && policies.tenantId) {
    elements.scopeInput.value = policies.tenantId;
  }
}

function buildFormData(form, elements, formDataFactory) {
  if (!form) {
    throw new Error("glossary form is required");
  }
  const factory = typeof formDataFactory === "function" ? formDataFactory : (current) => new FormData(current);
  const formData = factory(form);

  const scopeType = elements.scopeSelect?.value ?? formData.get?.("scopeType") ?? "tenant";
  const scopeId = elements.scopeInput?.value ?? formData.get?.("scopeId") ?? "";
  const overwrite = Boolean(elements.overwriteCheckbox?.checked);

  if (typeof formData.set === "function") {
    formData.set("scopeType", scopeType);
    formData.set("scopeId", scopeId);
    formData.set("overwrite", overwrite ? "true" : "false");
  } else if (typeof formData.append === "function") {
    formData.append("scopeType", scopeType);
    formData.append("scopeId", scopeId);
    formData.append("overwrite", overwrite ? "true" : "false");
  } else {
    formData.scopeType = scopeType;
    formData.scopeId = scopeId;
    formData.overwrite = overwrite ? "true" : "false";
  }

  return formData;
}

async function handleUpload(event, elements, fetchImpl, formDataFactory) {
  event?.preventDefault?.();

  const file = elements.fileInput?.files?.[0];
  if (!file) {
    renderErrorList(elements.errorList, [getString("tla.settings.upload.selectFile")]);
    updateVisibility(elements.errorContainer, true);
    return;
  }

  updateVisibility(elements.errorContainer, false);
  updateVisibility(elements.conflictContainer, false);
  updateProgress(elements, 10, getString("tla.settings.upload.progress.uploading"));

  let formData;
  try {
    formData = buildFormData(elements.form, elements, formDataFactory);
  } catch (error) {
    renderErrorList(elements.errorList, [error.message]);
    updateVisibility(elements.errorContainer, true);
    return;
  }

  if (typeof formData.append === "function") {
    formData.append("file", file, file.name ?? "glossary.csv");
  } else {
    formData.file = file;
  }

  updateProgress(elements, 35, getString("tla.settings.upload.progress.parsing"));

  try {
    const response = await (fetchImpl ?? DEFAULT_FETCH)("/api/glossary/upload", {
      method: "POST",
      body: formData
    });
    if (!response.ok) {
      const text = await response.text?.();
      const defaultError = formatString(getString("tla.settings.upload.error.http"), response.status ?? "");
      throw new Error(text || defaultError);
    }
    const result = await response.json();

    updateProgress(elements, 100, getString("tla.settings.upload.progress.complete"));
    renderConflictList(elements.conflictList, result?.conflicts ?? []);
    updateVisibility(elements.conflictContainer, Array.isArray(result?.conflicts) && result.conflicts.length > 0);

    renderErrorList(elements.errorList, result?.errors ?? []);
    updateVisibility(elements.errorContainer, Array.isArray(result?.errors) && result.errors.length > 0);

    if (elements.statusLabel) {
      const imported = Number(result?.imported ?? 0);
      const updated = Number(result?.updated ?? 0);
      elements.statusLabel.textContent = formatString(getString("tla.settings.upload.summary"), imported, updated);
    }

    await refreshGlossary(elements, fetchImpl ?? DEFAULT_FETCH);
  } catch (error) {
    renderErrorList(elements.errorList, [error.message ?? String(error)]);
    updateVisibility(elements.errorContainer, true);
  }
}

export async function handleGlossaryUpload({
  elements,
  fetchImpl = DEFAULT_FETCH,
  formDataFactory
} = {}) {
  const resolved = elements ?? {};
  const submitButton = resolved.submitButton;
  if (submitButton) {
    submitButton.disabled = true;
  }

  try {
    const file = resolved.fileInput?.files?.[0];
    if (!file) {
      const missingFileMessage = getString("tla.settings.upload.error.noFile");
      renderGlossaryUploadFeedback(resolved, {
        imported: 0,
        updated: 0,
        conflicts: [],
        errors: [missingFileMessage],
        message: missingFileMessage
      });
      updateVisibility(resolved.errorContainer, true);
      return;
    }

    let formData;
    try {
      const factory = typeof formDataFactory === "function"
        ? formDataFactory
        : (current) => new FormData(current);
      formData = factory(resolved.form);
    } catch (error) {
      const message = error?.message ?? String(error);
      renderGlossaryUploadFeedback(resolved, {
        imported: 0,
        updated: 0,
        conflicts: [],
        errors: [message],
        message
      });
      updateVisibility(resolved.errorContainer, true);
      return;
    }

    const scope = resolved.scopeInput?.value ?? "tenant";
    const overwrite = resolved.overrideInput?.checked ? "true" : "false";

    if (typeof formData.set === "function") {
      formData.set("scope", scope);
      formData.set("overwrite", overwrite);
      formData.set("file", file);
    } else if (typeof formData.append === "function") {
      formData.append("scope", scope);
      formData.append("overwrite", overwrite);
      formData.append("file", file);
    } else {
      formData.scope = scope;
      formData.overwrite = overwrite;
      formData.file = file;
    }

    const uploadResponse = await (fetchImpl ?? DEFAULT_FETCH)("/api/glossary/upload", {
      method: "POST",
      body: formData
    });

    if (!uploadResponse?.ok) {
      let payload;
      try {
        payload = await uploadResponse?.json?.();
      } catch (error) {
        console.warn("解析上传失败响应时出错", error);
        payload = {};
      }

      const message = payload?.error
        ?? formatString(getString("tla.settings.upload.error.http"), uploadResponse?.status ?? "");
      renderGlossaryUploadFeedback(resolved, {
        imported: Number(payload?.imported ?? 0),
        updated: Number(payload?.updated ?? 0),
        conflicts: payload?.conflicts ?? [],
        errors: Array.isArray(payload?.errors) ? payload.errors : [],
        message
      });
      updateVisibility(resolved.errorContainer, true);
      return;
    }

    const result = await uploadResponse.json?.();
    const successMessage = formatString(
      getString("tla.settings.upload.summary"),
      Number(result?.imported ?? 0),
      Number(result?.updated ?? 0)
    );
    renderGlossaryUploadFeedback(resolved, {
      ...result,
      message: successMessage
    });

    try {
      const glossaryResponse = await (fetchImpl ?? DEFAULT_FETCH)("/api/glossary", { method: "GET" });
      if (glossaryResponse?.ok) {
        const entries = await glossaryResponse.json?.();
        renderGlossaryEntries(resolved.glossaryList, entries);
      }
    } catch (error) {
      console.warn("刷新术语列表失败", error);
    }
  } catch (error) {
    const message = error?.message ?? String(error);
    renderGlossaryUploadFeedback(resolved, {
      imported: 0,
      updated: 0,
      conflicts: [],
      errors: [message],
      message
    });
    updateVisibility(resolved.errorContainer, true);
  } finally {
    if (submitButton) {
      submitButton.disabled = false;
    }
  }
}

export async function initSettingsPage({
  root,
  fetchImpl = DEFAULT_FETCH,
  formDataFactory
} = {}) {
  const elements = resolveElements(root);

  await initializeLocalization(undefined, { fetchImpl });

  try {
    const configuration = await fetchJson("/api/configuration", {
      fetchImpl,
      toastMessage: () => getString("tla.toast.settings.configuration"),
      toastKey: "settings-configuration"
    });
    renderPolicies(elements, configuration?.tenantPolicies);
  } catch (error) {
    console.warn("加载配置失败", error);
    renderPolicies(elements, null);
  }

  await refreshGlossary(elements, fetchImpl);

  if (elements.form && typeof elements.form.addEventListener === "function") {
    elements.form.addEventListener("submit", (event) => handleUpload(event, elements, fetchImpl, formDataFactory));
  }

  return elements;
}

if (typeof document !== "undefined") {
  document.addEventListener("DOMContentLoaded", () => {
    initSettingsPage().catch((error) => console.error("初始化设置页面失败", error));
  });
}

export default {
  initSettingsPage,
  renderGlossaryList,
  renderConflictList,
  renderErrorList,
  renderGlossaryEntries,
  renderGlossaryUploadFeedback,
  handleGlossaryUpload
};
