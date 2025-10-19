const listeners = new Set();

function now() {
  if (typeof performance !== "undefined" && typeof performance.now === "function") {
    return performance.now();
  }
  return Date.now();
}

function createEvent(name, detail) {
  if (typeof window === "undefined" || typeof window.dispatchEvent !== "function") {
    return null;
  }

  const eventDetail = { ...detail };
  if (typeof window.CustomEvent === "function") {
    return new window.CustomEvent(name, { detail: eventDetail });
  }

  if (typeof CustomEvent === "function") {
    return new CustomEvent(name, { detail: eventDetail });
  }

  return null;
}

export function emitTelemetry(event) {
  if (!event || typeof event !== "object") {
    return;
  }

  const payload = {
    timestamp: now(),
    ...event
  };

  for (const listener of listeners) {
    try {
      listener(payload);
    } catch (error) {
      if (typeof console !== "undefined" && typeof console.error === "function") {
        console.error("telemetry listener failed", error);
      }
    }
  }

  const customEvent = createEvent("telemetry", payload);
  if (customEvent) {
    window.dispatchEvent(customEvent);
  }
}

export function onTelemetry(listener) {
  if (typeof listener !== "function") {
    return () => {};
  }

  listeners.add(listener);
  return () => {
    listeners.delete(listener);
  };
}

export function clearTelemetryListeners() {
  listeners.clear();
}
