export class ModelProvider {
  constructor({ id, costPerCharUsd, latencyTargetMs, reliability = 0.999 }) {
    this.id = id;
    this.costPerCharUsd = costPerCharUsd;
    this.latencyTargetMs = latencyTargetMs;
    this.reliability = reliability;
  }

  async translate(_request) {
    throw new Error("translate must be implemented by subclasses");
  }

  async detect(_request) {
    return null;
  }
}

export class MockModelProvider extends ModelProvider {
  constructor(options) {
    super(options);
    this.behavior = options.behavior ?? {};
  }

  async translate(request) {
    const { behavior } = this;
    if (behavior.failures && behavior.failures > 0) {
      behavior.failures -= 1;
      const error = new Error(`Model ${this.id} simulated failure`);
      error.code = behavior.errorCode ?? "MODEL_FAILURE";
      throw error;
    }
    const latency = behavior.latency ?? 50;
    if (latency > (behavior.timeout ?? Infinity)) {
      throw new Error("Timeout");
    }
    const prefix = behavior.translationPrefix ?? `[${this.id}]`;
    return {
      text: `${prefix} ${request.text}`,
      detectedLanguage: behavior.detectedLanguage ?? request.sourceLanguage ?? "en",
      latencyMs: latency,
      modelId: this.id,
      confidence: behavior.confidence ?? 0.8
    };
  }

  async detect(request) {
    if (this.behavior.detectedLanguage) {
      return {
        language: this.behavior.detectedLanguage,
        confidence: this.behavior.confidence ?? 0.7
      };
    }
    if (request.text && /[\u4e00-\u9fa5]/.test(request.text)) {
      return { language: "zh-Hans", confidence: 0.9 };
    }
    if (request.text && /[áéíóúñü]/i.test(request.text)) {
      return { language: "es", confidence: 0.6 };
    }
    return { language: "en", confidence: 0.5 };
  }
}

export default {
  ModelProvider,
  MockModelProvider
};
