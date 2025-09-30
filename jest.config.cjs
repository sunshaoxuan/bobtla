module.exports = {
  testMatch: ["**/tests/**/*.jest.js"],
  testEnvironment: "jsdom",
  transform: {},
  extensionsToTreatAsEsm: [".js"],
  moduleNameMapper: {
    "^(\\.{1,2}/.*)\\.js$": "$1.js"
  },
  testEnvironmentOptions: {
    customExportConditions: ["node", "node-addons"]
  }
};
