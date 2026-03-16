import { NextResponse } from "next/server";
import type { ApiResponse } from "@/types/api";
import { serverFetch } from "@/lib/serverApi";

export interface PamMonteCarloRequest {
  numContracts?: number;
  numScenarios?: number;
  monthsToMaturity?: number;
  calcDateIndex?: number;
  seed?: number;
  baseDate?: string;
  preferGpu?: boolean;
  description?: string;
  // File upload mode (alternative to synthetic generation)
  portfolioCsv?: string;
  metadataCsv?: string;
  scenarioJson?: string;
}

export interface PamMonteCarloResponse {
  runId: string;
  statusUrl: string;
  resultUrl: string;
  state: string;
  description: string;
}

export async function POST(request: Request) {
  try {
    const body: PamMonteCarloRequest = await request.json();

    // Convert frontend camelCase to backend PascalCase
    const backendPayload = {
      NumContracts: body.numContracts ?? 1000,
      NumScenarios: body.numScenarios ?? 100,
      MonthsToMaturity: body.monthsToMaturity ?? 600, // 50 years
      CalcDateIndex: body.calcDateIndex ?? 0,
      Seed: body.seed ?? 12345,
      BaseDate: body.baseDate ?? new Date().toISOString().split('T')[0],
      PreferGpu: body.preferGpu ?? false,
      Description: body.description ?? "PAM Monte Carlo Analysis",
      // File data
      PortfolioCsv: body.portfolioCsv,
      MetadataCsv: body.metadataCsv,
      ScenarioJson: body.scenarioJson
    };

    // Direct fetch to backend without auth headers
    const backendUrl = `${process.env.API_BASE_URL ?? "http://localhost:8080"}/runs/pam-monte-carlo`;
    const backendResponse = await fetch(backendUrl, {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
      },
      body: JSON.stringify(backendPayload)
    });

    if (!backendResponse.ok) {
      throw new Error(`Backend error ${backendResponse.status}: ${backendResponse.statusText}`);
    }

    const data: PamMonteCarloResponse = await backendResponse.json();

    const response: ApiResponse<PamMonteCarloResponse> = {
      success: true,
      data,
      timestamp: new Date().toISOString(),
    };

    return NextResponse.json(response, { status: 202 });
  } catch (error) {
    console.error("[pam-monte-carlo] Error starting run:", error);
    
    const response: ApiResponse<never> = {
      success: false,
      error: error instanceof Error ? error.message : "Failed to start PAM Monte Carlo run",
      timestamp: new Date().toISOString(),
    };

    return NextResponse.json(response, { status: 500 });
  }
}