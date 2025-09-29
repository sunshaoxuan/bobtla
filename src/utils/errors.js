export class TranslationError extends Error {
  constructor(message, { code = "TRANSLATION_ERROR", details = {} } = {}) {
    super(message);
    this.name = "TranslationError";
    this.code = code;
    this.details = details;
  }
}

export class BudgetExceededError extends Error {
  constructor(message, { remainingUsd = 0 } = {}) {
    super(message);
    this.name = "BudgetExceededError";
    this.remainingUsd = remainingUsd;
  }
}

export class ComplianceError extends Error {
  constructor(message, { policy } = {}) {
    super(message);
    this.name = "ComplianceError";
    this.policy = policy;
  }
}

export default {
  TranslationError,
  BudgetExceededError,
  ComplianceError
};
