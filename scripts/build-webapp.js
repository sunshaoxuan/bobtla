import fs from "fs/promises";
import path from "path";
import { fileURLToPath } from "url";

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const repoRoot = path.resolve(__dirname, "..");
const sourceWebappDir = path.join(repoRoot, "src", "webapp");
const sourceTeamsClientDir = path.join(repoRoot, "src", "teamsClient");
const outputRoot = path.join(repoRoot, "src", "TlaPlugin", "wwwroot");

async function ensureEmptyDirectory(directory) {
  await fs.rm(directory, { recursive: true, force: true });
  await fs.mkdir(directory, { recursive: true });
}

async function copyDirectory(source, destination) {
  const entries = await fs.readdir(source, { withFileTypes: true });
  await fs.mkdir(destination, { recursive: true });

  await Promise.all(
    entries.map(async (entry) => {
      const sourcePath = path.join(source, entry.name);
      const destinationPath = path.join(destination, entry.name);
      if (entry.isDirectory()) {
        await copyDirectory(sourcePath, destinationPath);
      } else if (entry.isFile()) {
        await fs.mkdir(path.dirname(destinationPath), { recursive: true });
        await fs.copyFile(sourcePath, destinationPath);
      }
    })
  );
}

function rewriteRelativeAssetReferences(html) {
  return html.replace(/(href|src)=["'](?!(?:https?:|\/\/|\/))(.?\/)?([^"']+)["']/g, (_match, attribute, _prefix, file) => {
    return `${attribute}="../webapp/${file}"`;
  });
}

async function createRouteIndex(routeFolder, sourceFile) {
  const sourcePath = path.join(sourceWebappDir, sourceFile);
  const destinationPath = path.join(outputRoot, routeFolder, "index.html");
  const original = await fs.readFile(sourcePath, "utf8");
  const rewritten = rewriteRelativeAssetReferences(original);
  await fs.mkdir(path.dirname(destinationPath), { recursive: true });
  await fs.writeFile(destinationPath, rewritten, "utf8");
}

async function build() {
  await ensureEmptyDirectory(outputRoot);

  await copyDirectory(sourceWebappDir, path.join(outputRoot, "webapp"));
  await copyDirectory(sourceTeamsClientDir, path.join(outputRoot, "teamsClient"));

  const routes = [
    { folder: "dashboard", source: "index.html" },
    { folder: "settings", source: "settings.html" },
    { folder: "compose", source: "compose.html" },
    { folder: "dialog", source: "dialog.html" }
  ];

  for (const route of routes) {
    await createRouteIndex(route.folder, route.source);
  }
}

build().catch((error) => {
  console.error("Failed to build webapp assets:", error);
  process.exitCode = 1;
});
