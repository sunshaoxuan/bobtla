import { routingPolicy } from "../config.js";
import { TranslationError, ComplianceError } from "../utils/errors.js";

function computeScore(result, provider) {
  const quality = result.confidence ?? 0.5;
  const latencyScore = provider.latencyTargetMs / Math.max(result.latencyMs ?? provider.latencyTargetMs, 1);
  const costScore = provider.costPerCharUsd > 0 ? 1 / provider.costPerCharUsd : 0;
  return {
    quality,
    latencyScore,
    costScore
  };
}

export class TranslationRouter {
  constructor({ providers, budgetGuard, glossaryManager, detector, auditLogger, complianceGateway, retry = 0 } = {}) {
    this.providers = providers ?? [];
    this.budgetGuard = budgetGuard;
    this.glossaryManager = glossaryManager;
    this.detector = detector;
    this.auditLogger = auditLogger;
    this.complianceGateway = complianceGateway;
    this.retry = retry;
  }

  async translate({ text, sourceLanguage, targetLanguage, tenantId, userId, channelId, metadata = {} }) {
    if (!text) {
      throw new TranslationError("Text is required", { code: "EMPTY_TEXT" });
    }
    const trimmed = text.trim();
    const detection = sourceLanguage
      ? { language: sourceLanguage, confidence: 1 }
      : await this.detector?.detect({ text: trimmed, tenantId, userId });

    const request = {
      text: trimmed,
      sourceLanguage: detection?.language,
      targetLanguage,
      tenantId,
      userId
    };

    const errors = [];
    for (const provider of this.providers) {
      let complianceResult;
      try {
        complianceResult = this.complianceGateway?.assertCanRoute({
          text: trimmed,
          provider,
          targetLanguage,
          sourceLanguage: detection?.language,
          tenantId,
          userId
        });
      } catch (error) {
        errors.push({ provider: provider.id, error });
        continue;
      }
      try {
        const result = await this.invokeProvider(provider, request);
        const glossaryApplied = this.applyGlossary(result.text, { channel: channelId });
        const enriched = { ...result, text: glossaryApplied };
        this.auditLogger?.record({
          userId,
          tenantId,
          sourceText: trimmed,
          translatedText: enriched.text,
          modelId: provider.id,
          latencyMs: result.latencyMs,
          metadata: { ...metadata, detectedLanguage: result.detectedLanguage, compliance: complianceResult }
        });
        return {
          ...enriched,
          detectedLanguage: result.detectedLanguage ?? detection?.language,
          requestLatencyMs: result.latencyMs,
          requestCostUsd: provider.costPerCharUsd * trimmed.length,
          compliance: complianceResult
        };
      } catch (error) {
        errors.push({ provider: provider.id, error });
      }
    }
    if (errors.length > 0 && errors.every((entry) => entry.error instanceof ComplianceError)) {
      const violations = errors
        .flatMap((entry) => entry.error.policy?.violations ?? [])
        .filter(Boolean);
      throw new ComplianceError("All providers blocked by compliance policy", {
        policy: { violations, providers: errors.map((e) => e.provider) }
      });
    }
    const detail = errors.map((e) => `${e.provider}:${e.error.code ?? e.error.message}`).join(", ");
    throw new TranslationError(`All models failed: ${detail}`, { code: "ROUTER_NO_SUCCESS", details: errors });
  }

  async invokeProvider(provider, request) {
    let attempt = 0;
    let lastError;
    while (attempt <= this.retry) {
      try {
        const result = await provider.translate(request);
        const cost = provider.costPerCharUsd * request.text.length;
        this.budgetGuard?.charge(cost);
        return result;
      } catch (error) {
        lastError = error;
        attempt += 1;
        if (attempt > this.retry) {
          throw error;
        }
        await new Promise((resolve) => setTimeout(resolve, routingPolicy.backoffMs ?? 100));
      }
    }
    throw lastError ?? new Error("Unknown translation failure");
  }

  applyGlossary(text, context) {
    if (!this.glossaryManager) {
      return text;
    }
    return this.glossaryManager.resolve(text, context);
  }

  static rankCandidates(results) {
    return results
      .map(({ result, provider }) => {
        const score = computeScore(result, provider);
        const weighted =
          routingPolicy.qualitySignalWeight * score.quality +
          routingPolicy.latencySignalWeight * score.latencyScore +
          routingPolicy.costSignalWeight * score.costScore;
        return { providerId: provider.id, weightedScore: weighted, raw: score };
      })
      .sort((a, b) => b.weightedScore - a.weightedScore);
  }
}

export default {
  TranslationRouter
};
