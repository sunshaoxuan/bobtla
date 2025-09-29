import { compliancePolicy as defaultPolicy } from "../config.js";
import { ComplianceError } from "../utils/errors.js";

const DEFAULT_PII_PATTERNS = {
  email: defaultPolicy.piiPatterns.email,
  phone: defaultPolicy.piiPatterns.phone,
  creditCard: defaultPolicy.piiPatterns.creditCard
};

function uniqueMatches(matches) {
  return matches.filter((value, index, self) => self.findIndex((v) => v.match === value.match && v.type === value.type) === index);
}

export class ComplianceGateway {
  constructor({ policy = defaultPolicy, piiPatterns, clock = () => Date.now() } = {}) {
    this.policy = policy;
    this.clock = clock;
    this.piiPatterns = { ...DEFAULT_PII_PATTERNS, ...(policy?.piiPatterns ?? {}), ...piiPatterns };
  }

  evaluate({ text, provider, targetLanguage }) {
    const findings = {
      pii: this.scanPii(text),
      bannedPhrases: this.scanBannedPhrases(text),
      timestamp: this.clock(),
      providerId: provider?.id,
      providerRegions: provider?.regions ?? ["global"],
      providerCertifications: provider?.certifications ?? [],
      targetLanguage
    };

    const violations = [];

    const requiredRegions = this.policy?.requiredRegionTags ?? [];
    if (!this.isRegionAllowed(provider)) {
      violations.push(
        `Provider region ${findings.providerRegions.join(",")} not allowed for policy ${requiredRegions.join(",")}`
      );
    }

    const requiredCertifications = this.policy?.requiredCertifications ?? [];
    if (!this.hasRequiredCertifications(provider)) {
      violations.push(
        `Provider certifications ${findings.providerCertifications.join(",")} missing ${requiredCertifications.join(",")}`
      );
    }

    if (findings.bannedPhrases.length > 0) {
      violations.push(`Detected banned phrases: ${findings.bannedPhrases.join(", ")}`);
    }

    if (findings.pii.length > 0 && !this.isRegionStrictlyAllowed(provider)) {
      violations.push(`Detected PII types (${findings.pii.map((p) => p.type).join(", ")}) but provider region is ${findings.providerRegions.join(",")}`);
    }

    return {
      ...findings,
      allowed: violations.length === 0,
      violations
    };
  }

  assertCanRoute(context) {
    const evaluation = this.evaluate(context);
    if (!evaluation.allowed) {
      throw new ComplianceError("Compliance policy blocked translation", { policy: evaluation });
    }
    return evaluation;
  }

  scanPii(text) {
    if (!text || !this.policy) {
      return [];
    }
    const matches = [];
    for (const [type, pattern] of Object.entries(this.piiPatterns)) {
      if (!pattern) continue;
      const result = text.match(pattern);
      if (result) {
        matches.push({ type, match: result[0] });
      }
    }
    return uniqueMatches(matches);
  }

  scanBannedPhrases(text) {
    if (!text) {
      return [];
    }
    const lowered = text.toLowerCase();
    return (this.policy?.bannedPhrases ?? []).filter((phrase) => lowered.includes(phrase.toLowerCase()));
  }

  isRegionAllowed(provider) {
    const required = this.policy?.requiredRegionTags ?? [];
    if (required.length === 0) {
      return true;
    }
    const providerRegions = provider?.regions ?? ["global"];
    if (providerRegions.some((region) => required.includes(region))) {
      return true;
    }
    const fallback = this.policy?.allowedRegionFallbacks ?? [];
    return providerRegions.some((region) => fallback.includes(region));
  }

  isRegionStrictlyAllowed(provider) {
    const required = this.policy?.requiredRegionTags ?? [];
    if (required.length === 0) {
      return true;
    }
    const providerRegions = provider?.regions ?? ["global"];
    return providerRegions.some((region) => required.includes(region));
  }

  hasRequiredCertifications(provider) {
    const required = this.policy?.requiredCertifications ?? [];
    if (required.length === 0) {
      return true;
    }
    const certifications = provider?.certifications ?? [];
    return required.every((ref) => certifications.includes(ref));
  }
}

export default {
  ComplianceGateway
};
