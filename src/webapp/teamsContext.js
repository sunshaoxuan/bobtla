const DEFAULT_CONTEXT = {
  user: { id: "local-user" },
  tenant: { id: "local-tenant" },
  channel: { id: "local-channel" },
  app: { locale: "en-US" }
};

function createLocalFallbackSdk() {
  return {
    app: {
      async initialize() {
        return undefined;
      },
      async getContext() {
        return DEFAULT_CONTEXT;
      }
    },
    dialog: {
      submit() {
        return undefined;
      }
    },
    pages: {
      config: {
        registerOnSaveHandler() {
          return undefined;
        },
        async setConfig() {
          return undefined;
        },
        setValidityState() {
          return undefined;
        }
      }
    },
    conversations: {
      async sendMessageToConversation() {
        return undefined;
      }
    }
  };
}

export function resolveTeamsSdk(override) {
  if (override) {
    return override;
  }
  if (typeof window !== "undefined" && window.microsoftTeams) {
    return window.microsoftTeams;
  }
  if (typeof globalThis !== "undefined" && globalThis.microsoftTeams) {
    return globalThis.microsoftTeams;
  }
  return createLocalFallbackSdk();
}

export async function ensureTeamsContext({ teams } = {}) {
  const sdk = resolveTeamsSdk(teams);
  if (sdk?.app?.initialize) {
    await sdk.app.initialize();
  }
  const context = (await sdk?.app?.getContext?.()) ?? DEFAULT_CONTEXT;
  return { teams: sdk ?? createLocalFallbackSdk(), context };
}

export { DEFAULT_CONTEXT };

export default {
  resolveTeamsSdk,
  ensureTeamsContext
};
