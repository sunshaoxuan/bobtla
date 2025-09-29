import test from "node:test";
import assert from "node:assert/strict";
import { ComplianceGateway } from "../src/services/complianceGateway.js";

test("compliance gateway blocks banned phrases", () => {
  const gateway = new ComplianceGateway({
    policy: {
      version: "test",
      requiredRegionTags: ["eu"],
      allowedRegionFallbacks: [],
      requiredCertifications: [],
      bannedPhrases: ["internal"],
      piiPatterns: {}
    }
  });

  assert.throws(
    () =>
      gateway.assertCanRoute({
        text: "This is internal", 
        provider: { id: "mock", regions: ["eu"], certifications: [] }
      }),
    /Compliance policy blocked translation/
  );
});

test("compliance gateway allows certified region", () => {
  const gateway = new ComplianceGateway({
    policy: {
      version: "test",
      requiredRegionTags: ["eu"],
      allowedRegionFallbacks: ["global"],
      requiredCertifications: ["SOC2"],
      bannedPhrases: [],
      piiPatterns: {}
    }
  });

  const result = gateway.assertCanRoute({
    text: "Hello",
    provider: { id: "mock", regions: ["global"], certifications: ["SOC2"] }
  });

  assert.equal(result.allowed, true);
});
