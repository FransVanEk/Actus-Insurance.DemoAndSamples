import { NextResponse } from "next/server";
import type { ApiResponse, ContractsSummaryResponse } from "@/types/api";
import { serverFetch, UPSTREAM_PATHS } from "@/lib/serverApi";

const mockData: ContractsSummaryResponse = {
  totalContracts: 1284,
  distribution: [
    { name: "PAM",   fullName: "Principal at Maturity", value: 38, count: 488 },
    { name: "ANN",   fullName: "Annuity",               value: 27, count: 347 },
    { name: "LAM",   fullName: "Linear Amortizer",      value: 18, count: 231 },
    { name: "NAM",   fullName: "Negative Amortizer",    value: 9,  count: 116 },
    { name: "CLM",   fullName: "Call Money",             value: 5,  count: 64  },
    { name: "Other", fullName: "Other Types",            value: 3,  count: 38  },
  ],
};

export async function GET() {
  let data = mockData;

  if (process.env.API_BASE_URL) {
    try {
      data = await serverFetch<ContractsSummaryResponse>(UPSTREAM_PATHS.contractsSummary);
    } catch (err) {
      console.warn("[contracts/summary] upstream unavailable, using mock:", err);
    }
  }

  const response: ApiResponse<ContractsSummaryResponse> = {
    success: true,
    data,
    timestamp: new Date().toISOString(),
  };

  return NextResponse.json(response);
}
