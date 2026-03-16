/**
 * Server-side API helper — runs only inside Next.js API route handlers.
 * Never imported by client components (no "use client" files should import this).
 *
 * Reads credentials from server-only environment variables and forwards
 * requests to the real ACTUS backend. When the backend is unavailable the
 * route handler falls back to its local mock data automatically.
 */

// ─── Config ──────────────────────────────────────────────────────────────────

export const serverApiConfig = {
  /** Real ACTUS backend base URL, e.g. http://actus-api:8080 */
  baseUrl: process.env.API_BASE_URL ?? "http://localhost:8080",

  /** API key / Bearer token */
  apiKey: process.env.API_KEY ?? "",

  /** Optional Basic-auth credentials */
  username: process.env.API_USERNAME ?? "",
  password: process.env.API_PASSWORD ?? "",

  /** Optional tenant/org identifier */
  tenantId: process.env.API_TENANT_ID ?? "",
} as const;

// ─── Build auth headers ───────────────────────────────────────────────────────

function buildAuthHeaders(): HeadersInit {
  const headers: Record<string, string> = {
    "Content-Type": "application/json",
    Accept: "application/json",
  };

  if (serverApiConfig.apiKey) {
    headers["Authorization"] = `Bearer ${serverApiConfig.apiKey}`;
    headers["X-API-Key"] = serverApiConfig.apiKey;
  } else if (serverApiConfig.username && serverApiConfig.password) {
    const basic = Buffer.from(
      `${serverApiConfig.username}:${serverApiConfig.password}`
    ).toString("base64");
    headers["Authorization"] = `Basic ${basic}`;
  }

  if (serverApiConfig.tenantId) {
    headers["X-Tenant-ID"] = serverApiConfig.tenantId;
  }

  return headers;
}

// ─── Typed fetch helper ───────────────────────────────────────────────────────

interface ServerFetchOptions extends Omit<RequestInit, "headers"> {
  /** Extra headers merged with auth headers */
  headers?: Record<string, string>;
  /** Timeout in milliseconds (default: 10000) */
  timeoutMs?: number;
}

export async function serverFetch<T>(
  path: string,
  options: ServerFetchOptions = {}
): Promise<T> {
  const { headers: extraHeaders = {}, timeoutMs = 10_000, ...rest } = options;

  const url = `${serverApiConfig.baseUrl}${path}`;
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), timeoutMs);

  try {
    const res = await fetch(url, {
      ...rest,
      headers: { ...buildAuthHeaders(), ...extraHeaders },
      signal: controller.signal,
    });

    if (!res.ok) {
      throw new Error(`Upstream API error ${res.status}: ${res.statusText} — ${url}`);
    }

    return (await res.json()) as T;
  } finally {
    clearTimeout(timer);
  }
}

// ─── Upstream endpoint paths ──────────────────────────────────────────────────
// Map Next.js route concepts to the real ACTUS API paths.
// These paths don't exist in the current backend, so frontend will fall back to mock data

export const UPSTREAM_PATHS = {
  dashboardSummary:  "/api/v1/dashboard/summary", // Not available - will use mock
  contracts:         "/api/v1/contracts",         // Not available - will use mock  
  contractsSummary:  "/api/v1/contracts/summary", // Not available - will use mock
  projections:       "/api/v1/projections",      // Not available - will use mock
  riskOverview:      "/api/v1/risk/overview",     // Not available - will use mock
  events:            "/api/v1/events",           // Not available - will use mock
  // Available endpoints:
  runs:              "/runs",                     // Available - actual endpoint
  pamMonteCarlo:     "/runs/pam-monte-carlo",    // Available - actual endpoint
} as const;

// ─── API Connection Status ─────────────────────────────────────────────────────

export async function checkApiConnection(): Promise<boolean> {
  try {
    const response = await fetch(`${serverApiConfig.baseUrl}/runs`, {
      method: 'GET',
      headers: buildAuthHeaders(),
      signal: AbortSignal.timeout(5000)
    });
    return response.ok;
  } catch {
    return false;
  }
}

// Check backend connectivity from client-side
export async function checkBackendFromClient(): Promise<boolean> {
  try {
    const response = await fetch('/api/health', {
      method: 'GET',
      signal: AbortSignal.timeout(3000)
    });
    return response.ok;
  } catch {
    return false;
  }
}
