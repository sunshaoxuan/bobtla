export const DEFAULT_REGION_POLICY = "eu-priority";
export const DEFAULT_MODEL_ALLOW_LIST = [
  { id: "azureOpenAI:gpt-4o", costPerCharUsd: 0.00002, latencyTargetMs: 1800 },
  { id: "anthropic:claude-3", costPerCharUsd: 0.000018, latencyTargetMs: 2200 },
  { id: "ollama:llama3", costPerCharUsd: 0.000005, latencyTargetMs: 2500 }
];
export const DEFAULT_DAILY_BUDGET_USD = 10;
export const DEFAULT_COMPLIANCE_REFS = ["ISMAP", "SOC2", "ISO27001"];
export const DEFAULT_GLOSSARY_SOURCES = ["tenant.csv", "channel.csv", "user.csv"];

export const securityPolicy = {
  storeSecretsInKeyVault: true,
  redactLogHashAlgorithm: "SHA-256",
  piiDetectionEnabled: true,
  networkIsolation: {
    vnetIntegration: true,
    outboundAllowList: [
      "https://api.cognitive.microsofttranslator.com",
      "https://teams.microsoft.com"
    ]
  }
};

export const retryPolicy = {
  maxAttempts: 3,
  backoffMs: 150,
  jitterMs: 75
};

export const offlineDraftPolicy = {
  maxEntriesPerUser: 20,
  retentionHours: 72
};

export const routingPolicy = {
  latencyP95TargetMs: 2000,
  latencyP99TargetMs: 5000,
  fallbackThresholdMs: 2500,
  backoffMs: 120,
  qualitySignalWeight: 0.6,
  costSignalWeight: 0.2,
  latencySignalWeight: 0.2
};

export const auditPolicy = {
  storeOriginalFingerprintOnly: true,
  immutableLogStore: "azure-table",
  accessibilityLabeling: true
};

export const glossaryHierarchy = ["tenant", "channel", "user"];

export const maxCharactersPerRequest = 50000;

export default {
  DEFAULT_REGION_POLICY,
  DEFAULT_MODEL_ALLOW_LIST,
  DEFAULT_DAILY_BUDGET_USD,
  DEFAULT_COMPLIANCE_REFS,
  DEFAULT_GLOSSARY_SOURCES,
  securityPolicy,
  retryPolicy,
  offlineDraftPolicy,
  routingPolicy,
  auditPolicy,
  glossaryHierarchy,
  maxCharactersPerRequest
};
