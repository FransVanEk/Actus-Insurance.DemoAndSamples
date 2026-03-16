import { NextResponse } from "next/server";
import type { NextRequest } from "next/server";
import type { ApiResponse, EventsResponse, ActusEvent } from "@/types/api";
import { serverFetch, UPSTREAM_PATHS } from "@/lib/serverApi";

const mockEvents: ActusEvent[] = [
  {
    id: "evt-001",
    type: "info",
    title: "IP Event Scheduled",
    contractId: "PAM-2025-0041",
    description: "PAM-2025-0041 · Interest Payment due Mar 15",
    relativeTime: "in 21 days",
    timestamp: "2025-03-15T00:00:00.000Z",
  },
  {
    id: "evt-002",
    type: "warning",
    title: "Maturity Approaching",
    contractId: "CLM-2025-0034",
    description: "CLM-2025-0034 · Call Money matures Jun 30",
    relativeTime: "in 128 days",
    timestamp: "2025-06-30T00:00:00.000Z",
  },
  {
    id: "evt-003",
    type: "success",
    title: "Projection Completed",
    contractId: "ANN-2025-0039",
    description: "ANN-2025-0039 · Cash flow run finished",
    relativeTime: "2 hours ago",
    timestamp: new Date(Date.now() - 2 * 60 * 60 * 1000).toISOString(),
  },
  {
    id: "evt-004",
    type: "info",
    title: "PR Event Scheduled",
    contractId: "LAM-2025-0036",
    description: "LAM-2025-0036 · Principal repayment Feb 28",
    relativeTime: "in 6 days",
    timestamp: "2025-02-28T00:00:00.000Z",
  },
  {
    id: "evt-005",
    type: "warning",
    title: "Risk Threshold Alert",
    contractId: "NAM-2025-0029",
    description: "NAM-2025-0029 · DV01 exceeded limit",
    relativeTime: "4 hours ago",
    timestamp: new Date(Date.now() - 4 * 60 * 60 * 1000).toISOString(),
  },
  {
    id: "evt-006",
    type: "info",
    title: "MD Event Scheduled",
    contractId: "PAM-2025-0027",
    description: "PAM-2025-0027 · Maturity date event Nov 15",
    relativeTime: "in 267 days",
    timestamp: "2025-11-15T00:00:00.000Z",
  },
];

export async function GET(request: NextRequest) {
  const { searchParams } = new URL(request.url);
  const limit = Number(searchParams.get("limit") ?? mockEvents.length);
  const type  = searchParams.get("type");

  // Try real backend; fall back to mock when unavailable
  if (process.env.API_BASE_URL) {
    try {
      const upstreamPath = `${UPSTREAM_PATHS.events}?${searchParams.toString()}`;
      const upstream = await serverFetch<EventsResponse>(upstreamPath);
      return NextResponse.json({ success: true, data: upstream, timestamp: new Date().toISOString() } as ApiResponse<EventsResponse>);
    } catch (err) {
      console.warn("[events] upstream unavailable, using mock:", err);
    }
  }

  let filtered = [...mockEvents];
  if (type) filtered = filtered.filter((e) => e.type === type);
  const paginated = filtered.slice(0, limit);

  const response: ApiResponse<EventsResponse> = {
    success: true,
    data: { data: paginated, total: filtered.length },
    timestamp: new Date().toISOString(),
  };

  return NextResponse.json(response);
}
