import { NextResponse } from "next/server";
import type { ApiResponse, DashboardSummary } from "@/types/api";
import { serverFetch, UPSTREAM_PATHS } from "@/lib/serverApi";

const mockData: DashboardSummary = {
  activeContracts:   { value: 1284,        formatted: "1,284",   changePercent: 8.3  },
  portfolioNotional: { value: 4820000000,  formatted: "$4.82B",  changePercent: 5.1  },
  projectedCashFlow: { value: 91400000,    formatted: "$91.4M",  changePercent: 12.7 },
  eventsThisMonth:   { value: 347,         formatted: "347",     changePercent: -3.2 },
};

export async function GET() {
  let data = mockData;

  // Try real backend; fall back to mock when unavailable
  if (process.env.API_BASE_URL) {
    try {
      data = await serverFetch<DashboardSummary>(UPSTREAM_PATHS.dashboardSummary);
    } catch (err) {
      // Gracefully fall back to mock data - this is expected since backend doesn't have these endpoints
      console.log("[dashboard/summary] Using mock data - backend endpoints not available");
    }
  }

  const response: ApiResponse<DashboardSummary> = {
    success: true,
    data,
    timestamp: new Date().toISOString(),
  };

  return NextResponse.json(response);
}
