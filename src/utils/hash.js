import crypto from "crypto";

export function fingerprint(text) {
  return crypto.createHash("sha256").update(text, "utf8").digest("hex");
}

export default {
  fingerprint
};
