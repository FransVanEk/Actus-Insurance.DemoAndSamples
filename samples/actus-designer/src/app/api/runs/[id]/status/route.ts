import { NextResponse } from "next/server";
import type { ApiResponse, RunStatus } from "@/types/api";
import { serverFetch } from "@/lib/serverApi";

export async function GET(
  request: Request,
  { params }: { params: Promise<{ id: string }> }
) {
  try {
    const { id } = await params;

    // Try direct fetch without serverFetch to debug
    const backendUrl = `${process.env.API_BASE_URL ?? "http://localhost:8080"}/runs/${id}/status`;
    const backendResponse = await fetch(backendUrl);
    
    if (!backendResponse.ok) {
      throw new Error(`Backend error ${backendResponse.status}: ${backendResponse.statusText}`);
    }
    
    const backendData = await backendResponse.json();

    const response: ApiResponse<RunStatus> = {
      success: true,
      data: backendData,
      timestamp: new Date().toISOString(),
    };

    return NextResponse.json(response);
  } catch (error) {
    const { id } = await params;
    console.error(`[runs/${id}/status] Error getting status:`, error);
    
    const response: ApiResponse<never> = {
      success: false,
      error: error instanceof Error ? error.message : "Failed to get run status",
      timestamp: new Date().toISOString(),
    };

    return NextResponse.json(response, { status: 500 });
  }
}