#!/usr/bin/env node
import { Server } from "@modelcontextprotocol/sdk/server/index.js";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import {
  CallToolRequestSchema,
  ErrorCode,
  ListToolsRequestSchema,
  McpError,
} from "@modelcontextprotocol/sdk/types.js";

const UA =
  "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/136.0.0.0 Safari/537.36";

const FETCH_HEADERS = {
  "User-Agent": UA,
  Accept:
    "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,*/*;q=0.8",
  "Accept-Language": "en-US,en;q=0.9",
  "sec-ch-ua": '"Chromium";v="136", "Not/A)Brand";v="8"',
  "sec-ch-ua-mobile": "?0",
  "sec-ch-ua-platform": '"Windows"',
};

const REQUEST_TIMEOUT_MS = Number(process.env.WEB_TOOLS_TIMEOUT_MS ?? 20000);

function timeoutSignal() {
  if (typeof AbortSignal?.timeout === "function") return AbortSignal.timeout(REQUEST_TIMEOUT_MS);
  const controller = new AbortController();
  setTimeout(() => controller.abort(), REQUEST_TIMEOUT_MS).unref?.();
  return controller.signal;
}

function parseDDGHtml(html) {
  const results = [];
  const resultRegex =
    /<a[^>]+class="result__a"[^>]+href="([^"]+)"[^>]*>([\s\S]*?)<\/a>[\s\S]*?<a[^>]+class="result__url"[^>]*>([\s\S]*?)<\/a>[\s\S]*?<(?:a|td)[^>]+class="result__snippet"[^>]*>([\s\S]*?)<\/(?:a|td)>/gi;

  let match;
  while ((match = resultRegex.exec(html)) !== null) {
    const url = decodeDDGRedirect(match[1]);
    const title = stripTags(match[2]).trim();
    const hostname = stripTags(match[3]).trim();
    const description = stripTags(match[4]).trim();
    if (url && title) results.push({ title, url, description, hostname });
  }
  return results;
}

function decodeDDGRedirect(url) {
  if (url.includes("uddg=")) {
    try {
      const uddg = url.match(/uddg=([^&]+)/);
      if (uddg) return decodeURIComponent(uddg[1]);
    } catch {
      // fall through
    }
  }
  if (url.startsWith("//")) return "https:" + url;
  return url;
}

function stripTags(html) {
  return html
    .replace(/<[^>]+>/g, "")
    .replace(/&amp;/g, "&")
    .replace(/&lt;/g, "<")
    .replace(/&gt;/g, ">")
    .replace(/&quot;/g, '"')
    .replace(/&#39;/g, "'")
    .replace(/&nbsp;/g, " ")
    .replace(/&#\d+;/g, "")
    .replace(/&\w+;/g, "")
    .trim();
}

function extractText(html, contentType) {
  if (!contentType.includes("html") && !html.trimStart().startsWith("<")) return html;

  let text = html;
  text = text.replace(/<script[\s\S]*?<\/script>/gi, "");
  text = text.replace(/<style[\s\S]*?<\/style>/gi, "");
  text = text.replace(/<noscript[\s\S]*?<\/noscript>/gi, "");
  text = text.replace(
    /<\/(p|div|h[1-6]|li|tr|br|hr|blockquote|pre|article|section|header|footer|nav|main)>/gi,
    "\n",
  );
  text = text.replace(/<br\s*\/?>/gi, "\n");
  text = text.replace(/<[^>]+>/g, "");
  text = text
    .replace(/&amp;/g, "&")
    .replace(/&lt;/g, "<")
    .replace(/&gt;/g, ">")
    .replace(/&quot;/g, '"')
    .replace(/&#39;/g, "'")
    .replace(/&nbsp;/g, " ")
    .replace(/&mdash;/g, "—")
    .replace(/&ndash;/g, "–")
    .replace(/&#\d+;/g, "")
    .replace(/&\w+;/g, "");
  text = text.replace(/[ \t]+/g, " ");
  text = text.replace(/\n{3,}/g, "\n\n");
  return text.trim();
}

const server = new Server(
  { name: "pi-web-tools-mcp", version: "0.1.0" },
  { capabilities: { tools: {} } },
);

server.setRequestHandler(ListToolsRequestSchema, async () => ({
  tools: [
    {
      name: "web_search",
      description:
        "Search the web using DuckDuckGo. Returns results with title, URL, hostname, and description. Ported from the Pi duckduckgo-search extension.",
      inputSchema: {
        type: "object",
        properties: {
          query: { type: "string", description: "The search query string" },
          max_results: {
            type: "number",
            description: "Maximum results to return (default 5, max 20)",
            default: 5,
            minimum: 1,
            maximum: 20,
          },
        },
        required: ["query"],
      },
    },
    {
      name: "web_fetch",
      description:
        "Fetch a URL and extract text content. Ported from the Pi duckduckgo-search extension.",
      inputSchema: {
        type: "object",
        properties: {
          url: { type: "string", description: "The URL to fetch and extract text from" },
        },
        required: ["url"],
      },
    },
  ],
}));

server.setRequestHandler(CallToolRequestSchema, async (request) => {
  const args = request.params.arguments ?? {};
  try {
    switch (request.params.name) {
      case "web_search":
        return await webSearch(args);
      case "web_fetch":
        return await webFetch(args);
      default:
        throw new McpError(ErrorCode.MethodNotFound, `Unknown tool: ${request.params.name}`);
    }
  } catch (err) {
    if (err instanceof McpError) throw err;
    const msg = err instanceof Error ? err.message : String(err);
    return { content: [{ type: "text", text: msg }], isError: true };
  }
});

async function webSearch(args) {
  if (!args || typeof args.query !== "string" || args.query.trim() === "") {
    throw new McpError(ErrorCode.InvalidParams, "web_search requires a non-empty query string");
  }
  const maxResults = Math.min(Math.max(Number(args.max_results ?? 5), 1), 20);
  const body = new URLSearchParams({ q: args.query, b: "", kl: "wt-wt" });

  const response = await fetch("https://html.duckduckgo.com/html/", {
    method: "POST",
    headers: {
      ...FETCH_HEADERS,
      "Content-Type": "application/x-www-form-urlencoded",
      Referer: "https://duckduckgo.com/",
      Origin: "https://duckduckgo.com",
    },
    body: body.toString(),
    redirect: "follow",
    signal: timeoutSignal(),
  });

  if (!response.ok) {
    return {
      content: [{ type: "text", text: `Search request failed: HTTP ${response.status}` }],
      isError: true,
    };
  }

  const html = await response.text();
  const items = parseDDGHtml(html).slice(0, maxResults);
  const text = items
    .map((r, i) => `[${i + 1}] ${r.title}\n    ${r.url}\n    ${r.description}`)
    .join("\n\n");

  return {
    content: [{ type: "text", text: items.length ? text : `No results found for "${args.query}".` }],
    details: { query: args.query, resultCount: items.length, results: items },
  };
}

async function webFetch(args) {
  if (!args || typeof args.url !== "string" || args.url.trim() === "") {
    throw new McpError(ErrorCode.InvalidParams, "web_fetch requires a non-empty url string");
  }

  const response = await fetch(args.url, {
    headers: FETCH_HEADERS,
    redirect: "follow",
    signal: timeoutSignal(),
  });

  if (!response.ok) {
    return {
      content: [
        { type: "text", text: `Failed to fetch ${args.url}: HTTP ${response.status} ${response.statusText}` },
      ],
      isError: true,
    };
  }

  const contentType = response.headers.get("content-type") ?? "";
  const html = await response.text();
  const text = extractText(html, contentType);
  const maxLen = 50_000;
  const truncated = text.length > maxLen;
  const content = truncated ? text.slice(0, maxLen) : text;

  return {
    content: [
      {
        type: "text",
        text: content + (truncated ? `\n\n[... truncated ${text.length - maxLen} characters ...]` : ""),
      },
    ],
    details: { url: args.url, contentType, length: text.length, truncated },
  };
}

const transport = new StdioServerTransport();
await server.connect(transport);
console.error("Pi-compatible web tools MCP server running on stdio");