import { NextResponse } from "next/server";
import type { ApiResponse, RiskOverviewResponse } from "@/types/api";
import { serverFetch, UPSTREAM_PATHS } from "@/lib/serverApi";

const mockData: RiskOverviewResponse = {
  asOf: new Date().toISOString(),
  radar: [
    { metric: "Credit",    value: 72 },
    { metric: "Market",    value: 58 },
    { metric: "Liquidity", value: 85 },
    { metric: "Interest",  value: 64 },
    { metric: "FX",        value: 43 },
    { metric: "Ops",       value: 91 },
  ],
  metrics: [
    { label: "Portfolio DV01",  value: "$48,230",  change: "+$1,240",  trend: "down"   },
    { label: "Duration (Mod.)", value: "4.82 yrs", change: "-0.12",    trend: "up"     },
    { label: "VaR (95%, 1d)",   value: "$284,500", change: "+$12,300", trend: "down"   },
    { label: "Stress Test CVA", value: "$1.2M",    change: "Stable",   trend: "stable" },
  ],
};

export async function GET() {
  let data = mockData;

  if (process.env.API_BASE_URL) {
    try {
      data = await serverFetch<RiskOverviewResponse>(UPSTREAM_PATHS.riskOverview);
    } catch (err) {
      console.warn("[risk/overview] upstream unavailable, using mock:", err);
    }
  }

  const response: ApiResponse<RiskOverviewResponse> = {
    success: true,
    data,
    timestamp: new Date().toISOString(),
  };

  return NextResponse.json(response);
}
