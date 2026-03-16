import { NextResponse } from "next/server";
import type { ApiResponse, RunResult } from "@/types/api";
import { serverFetch } from "@/lib/serverApi";

export async function GET(
  request: Request,
  { params }: { params: Promise<{ id: string }> }
) {
  try {
    const { id } = await params;

    // Try direct fetch without serverFetch to avoid auth issues
    const backendUrl = `${process.env.API_BASE_URL ?? "http://localhost:8080"}/runs/${id}/result`;
    const backendResponse = await fetch(backendUrl);
    
    if (!backendResponse.ok) {
      throw new Error(`Backend error ${backendResponse.status}: ${backendResponse.statusText}`);
    }
    
    const backendData = await backendResponse.json();

    const response: ApiResponse<RunResult> = {
      success: true,
      data: backendData,
      timestamp: new Date().toISOString(),
    };

    return NextResponse.json(response);
  } catch (error) {
    const { id } = await params;
    console.error(`[runs/${id}/result] Error getting result:`, error);
    
    const response: ApiResponse<never> = {
      success: false,
      error: error instanceof Error ? error.message : "Failed to get run result",
      timestamp: new Date().toISOString(),
    };

    return NextResponse.json(response, { status: 500 });
  }
}