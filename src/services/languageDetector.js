export class LanguageDetector {
  constructor(providers = []) {
    this.providers = providers;
  }

  async detect(request) {
    const text = request?.text ?? "";
    for (const provider of this.providers) {
      if (typeof provider.detect === "function") {
        const result = await provider.detect(request);
        if (result && result.language) {
          return this.#normalize(result, text);
        }
      }
    }
    return this.#fallback(text);
  }

  #normalize(result, text) {
    const fallback = this.#fallback(text);
    const primaryLanguage = result.language ?? fallback.language;
    const confidence = typeof result.confidence === "number" ? this.#clamp(result.confidence) : fallback.confidence;
    const providedCandidates = Array.isArray(result.candidates)
      ? result.candidates
          .filter((item) => item && item.language)
          .map((item) => ({
            language: item.language,
            confidence: typeof item.confidence === "number" ? this.#clamp(item.confidence) : confidence
          }))
      : [];

    const registry = new Map();
    const register = (language, score) => {
      if (!language) return;
      const next = this.#clamp(score);
      if (!registry.has(language) || registry.get(language) < next) {
        registry.set(language, next);
      }
    };

    register(primaryLanguage, confidence);
    for (const candidate of providedCandidates) {
      register(candidate.language, candidate.confidence);
    }
    for (const candidate of fallback.candidates) {
      register(candidate.language, candidate.confidence);
    }

    const candidates = Array.from(registry.entries())
      .map(([language, value]) => ({ language, confidence: this.#round(value) }))
      .sort((a, b) => b.confidence - a.confidence || a.language.localeCompare(b.language))
      .slice(0, 6);

    return {
      language: candidates[0]?.language ?? primaryLanguage ?? fallback.language,
      confidence: candidates[0]?.confidence ?? this.#round(confidence),
      candidates
    };
  }

  #fallback(text) {
    const registry = new Map();
    const register = (language, score) => {
      if (!language) return;
      const clamped = this.#clamp(score);
      if (!registry.has(language) || registry.get(language) < clamped) {
        registry.set(language, clamped);
      }
    };

    const matches = this.#evaluateHeuristics(text ?? "");
    for (const entry of matches) {
      register(entry.language, entry.confidence);
    }

    if (!registry.size) {
      register("en", 0.5);
    }

    const ordered = Array.from(registry.entries())
      .map(([language, confidence]) => ({ language, confidence: this.#round(confidence) }))
      .sort((a, b) => b.confidence - a.confidence || a.language.localeCompare(b.language));

    return {
      language: ordered[0].language,
      confidence: ordered[0].confidence,
      candidates: ordered.slice(0, 6)
    };
  }

  #evaluateHeuristics(text) {
    const normalized = typeof text === "string" ? text.trim() : "";
    if (!normalized) {
      return [];
    }

    const results = [];
    const register = (language, confidence) => {
      results.push({ language, confidence });
    };

    const pushWithAlternatives = (language, confidence, alternatives = []) => {
      register(language, confidence);
      for (const option of alternatives) {
        const delta = typeof option.delta === "number" ? option.delta : -0.2;
        register(option.language, Math.max(0.1, confidence + delta));
      }
    };

    const has = (pattern) => pattern.test(normalized);

    if (has(/[\p{Script=Hiragana}\p{Script=Katakana}]/u)) {
      pushWithAlternatives("ja", 0.96, [
        { language: "zh-Hans", delta: -0.25 },
        { language: "ko", delta: -0.3 }
      ]);
    }

    if (has(/[\u4E00-\u9FFF]/u)) {
      pushWithAlternatives("zh-Hans", 0.88, [
        { language: "ja", delta: -0.18 },
        { language: "ko", delta: -0.25 }
      ]);
    }

    if (has(/[\uAC00-\uD7AF]/u)) {
      pushWithAlternatives("ko", 0.94, [
        { language: "ja", delta: -0.22 },
        { language: "zh-Hans", delta: -0.28 }
      ]);
    }

    if (has(/[\u0400-\u04FF]/u)) {
      pushWithAlternatives("ru", 0.9, [{ language: "uk", delta: -0.1 }]);
    }

    if (has(/[\u0600-\u06FF]/u)) {
      pushWithAlternatives("ar", 0.9, [{ language: "fa", delta: -0.08 }]);
    }

    if (has(/[\u0900-\u097F]/u)) {
      pushWithAlternatives("hi", 0.9, [{ language: "mr", delta: -0.08 }]);
    }

    if (has(/[\u0E00-\u0E7F]/u)) {
      register("th", 0.92);
    }

    if (has(/[ñáéíóúü¿¡]/iu)) {
      pushWithAlternatives("es", 0.84, [
        { language: "pt", delta: -0.12 },
        { language: "en", delta: -0.22 }
      ]);
    }

    if (has(/[ãõáâàêéíóôúç]/iu)) {
      pushWithAlternatives("pt", 0.82, [{ language: "es", delta: -0.1 }]);
    }

    if (has(/[àâçéèêëîïôûùüÿœæ]/iu)) {
      pushWithAlternatives("fr", 0.82, [{ language: "it", delta: -0.12 }]);
    }

    if (has(/[äöüß]/iu)) {
      pushWithAlternatives("de", 0.82, [{ language: "sv", delta: -0.14 }]);
    }

    if (has(/[åäö]/iu)) {
      pushWithAlternatives("sv", 0.78, [{ language: "fi", delta: -0.16 }]);
    }

    if (has(/[æøå]/iu)) {
      pushWithAlternatives("no", 0.78, [{ language: "da", delta: -0.1 }]);
    }

    if (has(/[ąćęłńóśźż]/iu)) {
      register("pl", 0.82);
    }

    if (has(/[čďěňřšťůž]/iu)) {
      register("cs", 0.8);
    }

    const totalLetters = (normalized.match(/\p{L}/gu) ?? []).length;
    const asciiLetters = (normalized.match(/[A-Za-z]/g) ?? []).length;
    if (totalLetters > 0) {
      const ratio = asciiLetters / totalLetters;
      const base = ratio >= 0.95 ? 0.74 : ratio >= 0.75 ? 0.68 : 0.58;
      pushWithAlternatives("en", base, [
        { language: "de", delta: -0.16 },
        { language: "fr", delta: -0.16 },
        { language: "es", delta: -0.18 }
      ]);
    }

    return results;
  }

  #clamp(value) {
    if (Number.isNaN(value)) {
      return 0;
    }
    return Math.max(0, Math.min(0.99, value));
  }

  #round(value) {
    return Math.round(this.#clamp(value) * 100) / 100;
  }
}

export default {
  LanguageDetector
};
