import { readFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

import { Client } from "@modelcontextprotocol/sdk/client/index.js";
import { StdioClientTransport } from "@modelcontextprotocol/sdk/client/stdio.js";

const __dirname = dirname(fileURLToPath(import.meta.url));
const projectRoot = join(__dirname, "..", "..");
const mcpPath = join(projectRoot, ".mcp.json");
const TIMEOUT_MS = Number(process.env.MCP_CALL_TIMEOUT_MS ?? 20000);

function usage(exitCode = 1) {
  console.error(`Usage:
  node .pi/scripts/mcp-call.mjs servers
  node .pi/scripts/mcp-call.mjs tools <server>
  node .pi/scripts/mcp-call.mjs call <server> <tool> [json-args | @args-file.json]

Examples:
  node .pi/scripts/mcp-call.mjs tools smart-connections
  node .pi/scripts/mcp-call.mjs call obsidian list_files '{"path":"/"}'
`);
  process.exit(exitCode);
}

function timeout(ms, label) {
  return new Promise((_, reject) => setTimeout(() => reject(new Error(`${label} timed out after ${ms}ms`)), ms));
}

async function withTimeout(promise, label) {
  return Promise.race([promise, timeout(TIMEOUT_MS, label)]);
}

function readConfig() {
  return JSON.parse(readFileSync(mcpPath, "utf8").replace(/^\uFEFF/, ""));
}

function parseArgs(raw) {
  if (!raw) return {};
  const json = raw.startsWith("@") ? readFileSync(raw.slice(1), "utf8") : raw;
  try {
    return JSON.parse(json);
  } catch (error) {
    throw new Error(`Invalid JSON arguments: ${error.message}`);
  }
}

async function connect(serverName, serverConfig) {
  const transport = new StdioClientTransport({
    command: serverConfig.command,
    args: serverConfig.args ?? [],
    env: { ...process.env, ...(serverConfig.env ?? {}) },
    cwd: projectRoot,
  });
  const client = new Client({ name: "pi-mcp-call", version: "0.1.0" }, { capabilities: {} });
  await withTimeout(client.connect(transport), `${serverName} connect`);
  return { client, transport };
}

function printToolResult(result) {
  const textParts = Array.isArray(result?.content)
    ? result.content
        .filter((item) => item?.type === "text" && typeof item.text === "string")
        .map((item) => item.text)
    : [];

  if (textParts.length > 0) {
    console.log(textParts.join("\n\n"));
  } else {
    console.log(JSON.stringify(result, null, 2));
  }
}

async function main() {
  const [command, serverName, toolName, rawArgs] = process.argv.slice(2);
  if (!command || command === "help" || command === "--help" || command === "-h") usage(command ? 0 : 1);

  const config = readConfig();
  const servers = config.mcpServers ?? {};

  if (command === "servers") {
    console.log(Object.keys(servers).join("\n"));
    return;
  }

  if (!serverName || !servers[serverName]) {
    console.error(`Unknown or missing MCP server: ${serverName ?? "<missing>"}`);
    console.error(`Available servers: ${Object.keys(servers).join(", ")}`);
    usage(1);
  }

  const { client, transport } = await connect(serverName, servers[serverName]);
  try {
    if (command === "tools") {
      const result = await withTimeout(client.listTools(), `${serverName} listTools`);
      for (const tool of result?.tools ?? []) {
        console.log(`${tool.name}\t${tool.description ?? ""}`);
      }
      return;
    }

    if (command === "call") {
      if (!toolName) usage(1);
      const args = parseArgs(rawArgs);
      const result = await withTimeout(client.callTool({ name: toolName, arguments: args }), `${serverName}/${toolName}`);
      printToolResult(result);
      return;
    }

    console.error(`Unknown command: ${command}`);
    usage(1);
  } finally {
    try {
      await client.close?.();
    } catch {
      // Best-effort cleanup only.
    }
    try {
      await transport.close?.();
    } catch {
      // Best-effort cleanup only.
    }
  }
}

main().catch((error) => {
  console.error(error?.stack ?? error?.message ?? String(error));
  process.exit(1);
});