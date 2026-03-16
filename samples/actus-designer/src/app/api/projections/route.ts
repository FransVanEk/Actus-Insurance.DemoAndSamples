import { NextResponse } from "next/server";
import type { NextRequest } from "next/server";
import type { ApiResponse, ProjectionsResponse } from "@/types/api";
import { serverFetch, UPSTREAM_PATHS } from "@/lib/serverApi";

const cashFlowByYear: Record<number, ProjectionsResponse> = {
  2025: {
    year: 2025,
    scenario: "Base Case",
    currency: "USD",
    data: [
      { month: "Jan", principal: 4200000, interest: 310000, fees: 42000 },
      { month: "Feb", principal: 3800000, interest: 290000, fees: 38000 },
      { month: "Mar", principal: 5100000, interest: 355000, fees: 51000 },
      { month: "Apr", principal: 4700000, interest: 338000, fees: 47000 },
      { month: "May", principal: 6200000, interest: 412000, fees: 62000 },
      { month: "Jun", principal: 5600000, interest: 385000, fees: 56000 },
      { month: "Jul", principal: 7100000, interest: 468000, fees: 71000 },
      { month: "Aug", principal: 6500000, interest: 430000, fees: 65000 },
      { month: "Sep", principal: 7900000, interest: 510000, fees: 79000 },
      { month: "Oct", principal: 8300000, interest: 538000, fees: 83000 },
      { month: "Nov", principal: 7600000, interest: 492000, fees: 76000 },
      { month: "Dec", principal: 9100000, interest: 574000, fees: 91000 },
    ],
  },
  2026: {
    year: 2026,
    scenario: "Base Case",
    currency: "USD",
    data: [
      { month: "Jan", principal: 5100000, interest: 368000, fees: 51000 },
      { month: "Feb", principal: 4600000, interest: 342000, fees: 46000 },
      { month: "Mar", principal: 6200000, interest: 428000, fees: 62000 },
      { month: "Apr", principal: 5900000, interest: 415000, fees: 59000 },
      { month: "May", principal: 7400000, interest: 496000, fees: 74000 },
      { month: "Jun", principal: 6800000, interest: 468000, fees: 68000 },
      { month: "Jul", principal: 8500000, interest: 556000, fees: 85000 },
      { month: "Aug", principal: 7900000, interest: 518000, fees: 79000 },
      { month: "Sep", principal: 9400000, interest: 610000, fees: 94000 },
      { month: "Oct", principal: 9900000, interest: 640000, fees: 99000 },
      { month: "Nov", principal: 9100000, interest: 590000, fees: 91000 },
      { month: "Dec", principal: 10800000, interest: 692000, fees: 108000 },
    ],
  },
};

export async function GET(request: NextRequest) {
  const { searchParams } = new URL(request.url);
  const year     = Number(searchParams.get("year")     ?? new Date().getFullYear());
  const scenario = searchParams.get("scenario") ?? "base";

  // Try real backend; fall back to mock when unavailable
  if (process.env.API_BASE_URL) {
    try {
      const upstreamPath = `${UPSTREAM_PATHS.projections}?${searchParams.toString()}`;
      const upstream = await serverFetch<ProjectionsResponse>(upstreamPath);
      return NextResponse.json({ success: true, data: upstream, timestamp: new Date().toISOString() } as ApiResponse<ProjectionsResponse>);
    } catch (err) {
      console.warn("[projections] upstream unavailable, using mock:", err);
    }
  }

  const yearData = cashFlowByYear[year] ?? cashFlowByYear[2025];
  let data = yearData.data;
  if (scenario === "stress") {
    data = data.map((d) => ({
      ...d,
      principal: Math.round(d.principal * 0.8),
      fees: Math.round(d.fees * 1.3),
    }));
  }

  const response: ApiResponse<ProjectionsResponse> = {
    success: true,
    data: { ...yearData, data, scenario: scenario === "stress" ? "Stress" : "Base Case" },
    timestamp: new Date().toISOString(),
  };

  return NextResponse.json(response);
}
