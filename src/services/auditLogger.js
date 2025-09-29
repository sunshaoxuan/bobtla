import { auditPolicy } from "../config.js";
import { fingerprint } from "../utils/hash.js";

export class AuditLogger {
  constructor({ storeOriginalFingerprintOnly = auditPolicy.storeOriginalFingerprintOnly } = {}) {
    this.storeOriginalFingerprintOnly = storeOriginalFingerprintOnly;
    this.records = [];
  }

  record({ userId, tenantId, sourceText, translatedText, modelId, latencyMs, metadata = {} }) {
    const entry = {
      timestamp: new Date().toISOString(),
      userId,
      tenantId,
      modelId,
      latencyMs,
      metadata,
      translatedText,
      sourceFingerprint: fingerprint(sourceText)
    };
    if (!this.storeOriginalFingerprintOnly) {
      entry.sourceText = sourceText;
    }
    this.records.push(entry);
    return entry;
  }

  query({ tenantId, start, end }) {
    return this.records.filter((record) => {
      if (tenantId && record.tenantId !== tenantId) {
        return false;
      }
      const ts = Date.parse(record.timestamp);
      if (start && ts < Date.parse(start)) {
        return false;
      }
      if (end && ts > Date.parse(end)) {
        return false;
      }
      return true;
    });
  }
}

export default {
  AuditLogger
};
