const ACTIVE_TOASTS = new Map();
const DEFAULT_DURATION = 6000;

function ensureContainer() {
  if (typeof document === "undefined") {
    return null;
  }

  let container = document.querySelector?.("[data-toast-container]");
  if (container) {
    return container;
  }

  if (!document.body || typeof document.createElement !== "function") {
    return null;
  }

  container = document.createElement("div");
  container.className = "toast-container";
  container.setAttribute("data-toast-container", "");
  container.setAttribute("role", "region");
  container.setAttribute("aria-live", "polite");
  document.body.append(container);
  return container;
}

function scheduleRemoval(toast, key, duration) {
  if (!Number.isFinite(duration) || duration <= 0) {
    return;
  }

  const handle = setTimeout(() => {
    dismissToast(toast, key);
  }, duration);

  toast.dataset.timeoutId = String(handle);
}

function dismissToast(toast, key) {
  if (!toast) {
    return;
  }

  if (key) {
    ACTIVE_TOASTS.delete(key);
  }

  if (toast.dataset.timeoutId) {
    clearTimeout(Number(toast.dataset.timeoutId));
    delete toast.dataset.timeoutId;
  }

  toast.classList.add("toast--leaving");
  setTimeout(() => {
    toast.remove();
  }, 240);
}

export function showToast(message, variant = "info", options = {}) {
  if (typeof document === "undefined") {
    return null;
  }

  const container = ensureContainer();
  if (!container) {
    return null;
  }

  const { duration = DEFAULT_DURATION, key } = options;

  if (key && ACTIVE_TOASTS.has(key)) {
    const existing = ACTIVE_TOASTS.get(key);
    existing.textContent = message;
    existing.className = `toast toast--${variant}`;
    existing.setAttribute("data-variant", variant);
    scheduleRemoval(existing, key, duration);
    return existing;
  }

  const toast = document.createElement("div");
  toast.className = `toast toast--${variant}`;
  toast.textContent = message;
  toast.setAttribute("role", "alert");
  toast.setAttribute("aria-live", variant === "danger" ? "assertive" : "polite");

  const handleDismiss = () => dismissToast(toast, key);
  toast.addEventListener("click", handleDismiss, { once: true });

  container.append(toast);

  if (key) {
    ACTIVE_TOASTS.set(key, toast);
  }

  scheduleRemoval(toast, key, duration);
  return toast;
}

export function clearToasts() {
  if (typeof document === "undefined") {
    return;
  }

  const container = document.querySelector?.("[data-toast-container]");
  if (!container) {
    return;
  }

  for (const toast of container.querySelectorAll?.(".toast") ?? []) {
    toast.remove();
  }

  ACTIVE_TOASTS.clear();
}
