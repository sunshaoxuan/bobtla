import { fetchJson } from "./network.js";

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
  renderList(container, items, "暂无术语");
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
  renderList(container, rows, "未检测到冲突");
}

export function renderErrorList(container, errors) {
  if (!container) {
    return;
  }
  const lines = Array.isArray(errors) ? errors.filter(Boolean) : [];
  renderList(container, lines, "");
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
      toastMessage: "无法加载术语表，请稍后重试。",
      toastKey: "settings-glossary"
    });
    renderGlossaryList(elements.glossaryList, entries);
  } catch (error) {
    console.warn("无法加载术语列表", error);
    renderGlossaryList(elements.glossaryList, []);
  }
}

function renderPolicies(elements, policies) {
  if (!policies) {
    renderList(elements.bannedList, [], "暂无禁译词");
    renderList(elements.styleList, [], "暂无风格模板");
    return;
  }
  renderList(elements.bannedList, policies.bannedTerms ?? [], "暂无禁译词");
  renderList(elements.styleList, policies.styleTemplates ?? [], "暂无风格模板");
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
    renderErrorList(elements.errorList, ["请先选择 CSV 或 TermBase 文件。"]);
    updateVisibility(elements.errorContainer, true);
    return;
  }

  updateVisibility(elements.errorContainer, false);
  updateVisibility(elements.conflictContainer, false);
  updateProgress(elements, 10, "上传中…");

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

  updateProgress(elements, 35, "解析文件…");

  try {
    const response = await (fetchImpl ?? DEFAULT_FETCH)("/api/glossary/upload", {
      method: "POST",
      body: formData
    });
    if (!response.ok) {
      const text = await response.text?.();
      throw new Error(text || `上传失败: ${response.status}`);
    }
    const result = await response.json();

    updateProgress(elements, 100, "上传完成");
    renderConflictList(elements.conflictList, result?.conflicts ?? []);
    updateVisibility(elements.conflictContainer, Array.isArray(result?.conflicts) && result.conflicts.length > 0);

    renderErrorList(elements.errorList, result?.errors ?? []);
    updateVisibility(elements.errorContainer, Array.isArray(result?.errors) && result.errors.length > 0);

    if (elements.statusLabel) {
      const imported = Number(result?.imported ?? 0);
      const updated = Number(result?.updated ?? 0);
      elements.statusLabel.textContent = `已导入 ${imported} 条，更新 ${updated} 条。`;
    }

    await refreshGlossary(elements, fetchImpl ?? DEFAULT_FETCH);
  } catch (error) {
    renderErrorList(elements.errorList, [error.message ?? String(error)]);
    updateVisibility(elements.errorContainer, true);
  }
}

export async function initSettingsPage({
  root,
  fetchImpl = DEFAULT_FETCH,
  formDataFactory
} = {}) {
  const elements = resolveElements(root);

  try {
    const configuration = await fetchJson("/api/configuration", {
      fetchImpl,
      toastMessage: "无法加载租户策略，将使用默认设置。",
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
  renderErrorList
};
