import { NextResponse } from "next/server";
import type { ApiResponse } from "@/types/api";
import { checkApiConnection } from "@/lib/serverApi";

export async function GET() {
  try {
    const baseUrl = process.env.API_BASE_URL ?? "http://localhost:8080";
    const backendResponse = await fetch(`${baseUrl}/runs`, {
      method: 'GET',
      signal: AbortSignal.timeout(5000)
    });
    
    const isBackendHealthy = backendResponse.ok;

    const response: ApiResponse<{ status: string; backend: boolean }> = {
      success: true,
      data: {
        status: isBackendHealthy ? "healthy" : "backend_unavailable",
        backend: isBackendHealthy
      },
      timestamp: new Date().toISOString(),
    };

    return NextResponse.json(response);
  } catch (error) {
    const response: ApiResponse<null> = {
      success: false,
      error: "Health check failed",
      timestamp: new Date().toISOString(),
    };

    return NextResponse.json(response, { status: 500 });
  }
}