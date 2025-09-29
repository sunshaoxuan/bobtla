import { glossaryHierarchy } from "../config.js";

export class GlossaryManager {
  constructor() {
    this.entries = new Map();
    for (const level of glossaryHierarchy) {
      this.entries.set(level, new Map());
    }
  }

  upsertEntry(level, source, target, metadata = {}) {
    if (!this.entries.has(level)) {
      throw new Error(`Unknown glossary level: ${level}`);
    }
    const levelEntries = this.entries.get(level);
    levelEntries.set(source.toLowerCase(), { target, metadata });
  }

  loadBulk(level, rows) {
    for (const row of rows) {
      this.upsertEntry(level, row.source, row.target, row.metadata ?? {});
    }
  }

  resolve(sourceText, context = {}) {
    const tokens = sourceText.split(/(\W+)/);
    return tokens
      .map((token) => {
        if (!token.trim()) {
          return token;
        }
        const lower = token.toLowerCase();
        const entry = this.lookup(lower, context);
        if (!entry) {
          return token;
        }
        if (entry.metadata?.strategy === "retain") {
          return token;
        }
        if (entry.metadata?.strategy === "mixed") {
          return `${entry.target} (${token})`;
        }
        return entry.target;
      })
      .join("");
  }

  lookup(source, context = {}) {
    for (const level of glossaryHierarchy) {
      const levelEntries = this.entries.get(level);
      const entry = levelEntries.get(source);
      if (entry) {
        const allowed = entry.metadata?.channels;
        if (!allowed || !context.channel || allowed.includes(context.channel)) {
          return entry;
        }
      }
    }
    return null;
  }
}

export default {
  GlossaryManager
};
