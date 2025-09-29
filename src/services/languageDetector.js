export class LanguageDetector {
  constructor(providers = []) {
    this.providers = providers;
  }

  async detect(request) {
    for (const provider of this.providers) {
      if (typeof provider.detect === "function") {
        const result = await provider.detect(request);
        if (result && result.language) {
          return result;
        }
      }
    }
    return { language: "en", confidence: 0.5 };
  }
}

export default {
  LanguageDetector
};
