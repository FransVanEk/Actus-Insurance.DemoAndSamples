import type {
  ApiResponse,
  DashboardSummary,
  ContractsResponse,
  ContractsSummaryResponse,
  ProjectionsResponse,
  RiskOverviewResponse,
  EventsResponse,
} from "@/types/api";

// ─── Base URL ─────────────────────────────────────────────────────────────────
// NEXT_PUBLIC_API_URL can be set to an absolute URL (e.g. https://api.actus.io)
// to bypass the Next.js proxy entirely. Leave it empty to use the built-in
// /api/... proxy routes (recommended — keeps credentials server-side).

const BASE_URL =
  (typeof process !== "undefined" && process.env.NEXT_PUBLIC_API_URL) || "";

// ─── Base fetcher (used by SWR) ───────────────────────────────────────────────

export async function fetcher<T>(url: string): Promise<T> {
  const fullUrl = url.startsWith("http") ? url : `${BASE_URL}${url}`;
  const res = await fetch(fullUrl);
  if (!res.ok) {
    throw new Error(`API error ${res.status}: ${res.statusText}`);
  }
  const json: ApiResponse<T> = await res.json();
  if (!json.success || !json.data) {
    throw new Error(json.error ?? "Unknown API error");
  }
  return json.data;
}

// ─── Typed endpoint helpers ───────────────────────────────────────────────────

export const API_ROUTES = {
  dashboardSummary: () => "/api/dashboard/summary",
  contracts: (params?: { limit?: number; status?: string; type?: string }) => {
    const q = new URLSearchParams();
    if (params?.limit)  q.set("limit",  String(params.limit));
    if (params?.status) q.set("status", params.status);
    if (params?.type)   q.set("type",   params.type);
    const qs = q.toString();
    return `/api/contracts${qs ? `?${qs}` : ""}`;
  },
  contractsSummary: () => "/api/contracts/summary",
  projections: (params?: { year?: number; scenario?: string }) => {
    const q = new URLSearchParams();
    if (params?.year)     q.set("year",     String(params.year));
    if (params?.scenario) q.set("scenario", params.scenario);
    const qs = q.toString();
    return `/api/projections${qs ? `?${qs}` : ""}`;
  },
  riskOverview: () => "/api/risk/overview",
  events: (params?: { limit?: number; type?: string }) => {
    const q = new URLSearchParams();
    if (params?.limit) q.set("limit", String(params.limit));
    if (params?.type)  q.set("type",  params.type);
    const qs = q.toString();
    return `/api/events${qs ? `?${qs}` : ""}`;
  },
  // PAM Monte Carlo endpoints - through Next.js API proxy
  pamMonteCarlo: () => "/api/pam-monte-carlo",
  runStatus: (runId: string) => `/api/runs/${runId}/status`,
  runResult: (runId: string) => `/api/runs/${runId}/result`,
} as const;

// ─── Typed fetchers ───────────────────────────────────────────────────────────

export const fetchDashboardSummary = () =>
  fetcher<DashboardSummary>(API_ROUTES.dashboardSummary());

export const fetchContracts = (params?: Parameters<typeof API_ROUTES.contracts>[0]) =>
  fetcher<ContractsResponse>(API_ROUTES.contracts(params));

export const fetchContractsSummary = () =>
  fetcher<ContractsSummaryResponse>(API_ROUTES.contractsSummary());

export const fetchProjections = (params?: Parameters<typeof API_ROUTES.projections>[0]) =>
  fetcher<ProjectionsResponse>(API_ROUTES.projections(params));

export const fetchRiskOverview = () =>
  fetcher<RiskOverviewResponse>(API_ROUTES.riskOverview());

export const fetchEvents = (params?: Parameters<typeof API_ROUTES.events>[0]) =>
  fetcher<EventsResponse>(API_ROUTES.events(params));
