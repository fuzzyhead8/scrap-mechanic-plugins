import { readFileSync } from "node:fs";
import { fileURLToPath, pathToFileURL } from "node:url";
import { dirname, join } from "node:path";

import { createRequire } from "module";
const require = createRequire(import.meta.url);

import { writeFileSync } from "node:fs";

import { Client } from "@modelcontextprotocol/sdk/client/index.js";
import { StdioClientTransport } from "@modelcontextprotocol/sdk/client/stdio.js";

const __dirname = dirname(fileURLToPath(import.meta.url));
const projectRoot = join(__dirname, "..", "..");
const mcpPath = join(projectRoot, ".mcp.json");

let config;
try {
  const raw = readFileSync(mcpPath, "utf8").replace(/^\uFEFF/, "");
  config = JSON.parse(raw);
} catch (e) {
  console.error("Cannot read .mcp.json:", e.message);
  process.exit(1);
}

const servers = config.mcpServers ?? {};
if (Object.keys(servers).length === 0) {
  console.log("No MCP servers configured in .mcp.json");
  process.exit(0);
}

const TIMEOUT_MS = 15000;

for (const [name, sc] of Object.entries(servers)) {
  try {
    const transport = new StdioClientTransport({
      command: sc.command,
      args: sc.args ?? [],
      env: { ...process.env, ...(sc.env ?? {}) },
      cwd: projectRoot,
    });
    const client = new Client(
      { name: "check-mcp", version: "0.1.0" },
      { capabilities: {} }
    );
    await Promise.race([client.connect(transport), timeout(TIMEOUT_MS)]);
    const result = await Promise.race([client.listTools(), timeout(TIMEOUT_MS)]);
    const count = (result?.tools ?? []).length;
    console.log(name + ": ok (" + count + " tools)");
    client.close();
    transport.close();
  } catch (err) {
    console.log(name + ": error (" + err.message + ")");
  }
}

function timeout(ms) {
  return new Promise((_, reject) => setTimeout(() => reject(new Error("timeout")), ms));
}
