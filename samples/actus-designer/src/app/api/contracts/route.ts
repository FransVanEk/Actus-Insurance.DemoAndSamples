import { NextResponse } from "next/server";
import type { NextRequest } from "next/server";
import type { ApiResponse, ContractsResponse, Contract } from "@/types/api";
import { serverFetch, UPSTREAM_PATHS } from "@/lib/serverApi";

const mockContracts: Contract[] = [
  {
    id: "PAM-2025-0041",
    type: "PAM",
    counterparty: "Deutsche Bank AG",
    notional: "$12,500,000",
    currency: "USD",
    maturity: "2030-06-15",
    status: "active",
    nextEvent: "IP 2025-03-15",
  },
  {
    id: "ANN-2025-0039",
    type: "ANN",
    counterparty: "Credit Suisse",
    notional: "$8,200,000",
    currency: "USD",
    maturity: "2028-12-01",
    status: "active",
    nextEvent: "PR 2025-03-01",
  },
  {
    id: "LAM-2025-0036",
    type: "LAM",
    counterparty: "JPMorgan Chase",
    notional: "€5,750,000",
    currency: "EUR",
    maturity: "2027-09-30",
    status: "monitoring",
    nextEvent: "PR 2025-02-28",
  },
  {
    id: "CLM-2025-0034",
    type: "CLM",
    counterparty: "UBS Group AG",
    notional: "£3,100,000",
    currency: "GBP",
    maturity: "2025-06-30",
    status: "expiring",
    nextEvent: "OPTEX 2025-04-01",
  },
  {
    id: "NAM-2025-0029",
    type: "NAM",
    counterparty: "Barclays PLC",
    notional: "$9,800,000",
    currency: "USD",
    maturity: "2032-03-31",
    status: "active",
    nextEvent: "IP 2025-03-31",
  },
  {
    id: "PAM-2025-0027",
    type: "PAM",
    counterparty: "BNP Paribas",
    notional: "€6,450,000",
    currency: "EUR",
    maturity: "2029-11-15",
    status: "review",
    nextEvent: "MD 2025-11-15",
  },
  {
    id: "ANN-2025-0024",
    type: "ANN",
    counterparty: "Santander Group",
    notional: "$4,300,000",
    currency: "USD",
    maturity: "2026-08-01",
    status: "expiring",
    nextEvent: "IP 2025-03-01",
  },
  {
    id: "LAM-2025-0021",
    type: "LAM",
    counterparty: "HSBC Holdings",
    notional: "€7,800,000",
    currency: "EUR",
    maturity: "2031-05-20",
    status: "active",
    nextEvent: "PR 2025-05-20",
  },
];

export async function GET(request: NextRequest) {
  const { searchParams } = new URL(request.url);
  const limit  = Number(searchParams.get("limit")  ?? mockContracts.length);
  const status = searchParams.get("status");
  const type   = searchParams.get("type");

  // Try real backend; fall back to mock when unavailable
  if (process.env.API_BASE_URL) {
    try {
      const upstreamPath = `${UPSTREAM_PATHS.contracts}?${searchParams.toString()}`;
      const upstream = await serverFetch<ContractsResponse>(upstreamPath);
      return NextResponse.json({ success: true, data: upstream, timestamp: new Date().toISOString() } as ApiResponse<ContractsResponse>);
    } catch (err) {
      console.log("[contracts] Using mock data - backend endpoints not available");
    }
  }

  let filtered = [...mockContracts];
  if (status) filtered = filtered.filter((c) => c.status === status);
  if (type)   filtered = filtered.filter((c) => c.type === type);
  const paginated = filtered.slice(0, limit);

  const response: ApiResponse<ContractsResponse> = {
    success: true,
    data: { data: paginated, total: filtered.length, page: 1, pageSize: limit },
    timestamp: new Date().toISOString(),
  };

  return NextResponse.json(response);
}
