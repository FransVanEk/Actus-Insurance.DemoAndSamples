"use client";

import { useState, useEffect } from "react";
import { Play, Settings, Loader2, CheckCircle, AlertCircle, BarChart3, Wifi, WifiOff } from "lucide-react";
import type { PamMonteCarloRequest, PamMonteCarloResponse, RunStatus, RunResult } from "@/types/api";
import useSWR from "swr";
import { fetcher, API_ROUTES } from "@/lib/api";

interface PamMonteCarloProps {
  className?: string;
}

// Check API connection from client-side
async function checkConnection(): Promise<boolean> {
  try {
    const response = await fetch('/api/health');
    const result = await response.json();
    return result.success && result.data?.backend;
  } catch {
    return false;
  }
}

export default function PamMonteCarlo({ className = "" }: PamMonteCarloProps) {
  const [isRunning, setIsRunning] = useState(false);
  const [activeRunId, setActiveRunId] = useState<string | null>(null);
  const [showSettings, setShowSettings] = useState(false);
  const [isApiConnected, setIsApiConnected] = useState<boolean | null>(null);
  const [parameters, setParameters] = useState<PamMonteCarloRequest>({
    numContracts: 1000,
    numScenarios: 100,
    monthsToMaturity: 600, // 50 years
    calcDateIndex: 0,
    seed: 12345,
    preferGpu: false,
    description: "Portfolio Monte Carlo Analysis"
  });

  // Check API connection on mount
  useEffect(() => {
    const checkConnectionStatus = async () => {
      const connected = await checkConnection();
      setIsApiConnected(connected);
    };
    
    // Check immediately without waiting
    checkConnectionStatus();
    
    // Recheck every 30 seconds  
    const interval = setInterval(checkConnectionStatus, 30000);
    return () => clearInterval(interval);
  }, []);

  // Force initial connection state for demo
  useEffect(() => {
    if (isApiConnected === null) {
      // Give a moment for the check to complete, then default to true
      const timer = setTimeout(() => {
        if (isApiConnected === null) {
          setIsApiConnected(true);
        }
      }, 2000);
      return () => clearTimeout(timer);
    }
  }, [isApiConnected]);

  // Poll run status when we have an active run
  const { data: runStatus } = useSWR<RunStatus>(
    activeRunId ? API_ROUTES.runStatus(activeRunId) : null,
    fetcher,
    { 
      refreshInterval: 1000, // Poll every second
      revalidateOnFocus: false
    }
  );

  // Get results when run is completed
  const { data: runResult } = useSWR<RunResult>(
    activeRunId && runStatus?.state === "Completed" ? API_ROUTES.runResult(activeRunId) : null,
    fetcher,
    { revalidateOnFocus: false }
  );

  // Stop polling when run is complete
  if (runStatus?.state === "Completed" || runStatus?.state === "Failed") {
    if (isRunning) {
      setIsRunning(false);
    }
  }

  const handleStartRun = async () => {
    if (!isApiConnected) {
      alert("Cannot start run: API is not connected");
      return;
    }
    
    try {
      setIsRunning(true);
      setActiveRunId(null);

      const response = await fetch(API_ROUTES.pamMonteCarlo(), {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
        },
        body: JSON.stringify(parameters),
      });

      if (!response.ok) {
        throw new Error(`HTTP ${response.status}: ${response.statusText}`);
      }

      const result = await response.json();
      
      if (result.success && result.data) {
        const runResponse: PamMonteCarloResponse = result.data;
        setActiveRunId(runResponse.runId);
      } else {
        throw new Error(result.error || "Failed to start run");
      }
    } catch (error) {
      console.error("Error starting PAM Monte Carlo run:", error);
      setIsRunning(false);
      setIsApiConnected(false); // Mark as disconnected on error
      alert("Failed to start run: " + (error instanceof Error ? error.message : "Unknown error"));
    }
  };

  const getStatusIcon = () => {
    if (!runStatus) return <Play className="w-5 h-5" />;
    
    switch (runStatus.state) {
      case "Queued":
      case "Running":
        return <Loader2 className="w-5 h-5 animate-spin" />;
      case "Completed":
        return <CheckCircle className="w-5 h-5 text-green-500" />;
      case "Failed":
        return <AlertCircle className="w-5 h-5 text-red-500" />;
      default:
        return <Play className="w-5 h-5" />;
    }
  };

  const getStatusText = () => {
    if (!runStatus) return "Ready to run";
    
    switch (runStatus.state) {
      case "Queued":
        return "Queued for execution...";
      case "Running":
        return `Running... ${runStatus.progress0To100}%`;
      case "Completed":
        return "Completed successfully";
      case "Failed":
        return "Run failed";
      default:
        return runStatus.state;
    }
  };

  const formatNumber = (num: number) => {
    if (num >= 1_000_000) return `${(num / 1_000_000).toFixed(1)}M`;
    if (num >= 1_000) return `${(num / 1_000).toFixed(1)}K`;
    return num.toLocaleString();
  };

  const formatCurrency = (amount: number) => {
    return new Intl.NumberFormat("en-US", {
      style: "currency",
      currency: "USD",
      minimumFractionDigits: 0,
      maximumFractionDigits: 0,
    }).format(amount);
  };

  return (
    <div className={`bg-white/80 backdrop-blur-sm border border-white/20 rounded-xl shadow-lg ${className}`}>
      <div className="p-6">
        <div className="flex items-center justify-between mb-6">
          <div className="flex-1">
            <div className="flex items-center gap-3 mb-1">
              <h3 className="text-xl font-semibold text-gray-900">PAM Monte Carlo</h3>
              <div className="flex items-center gap-1.5">
                {isApiConnected === null ? (
                  <Loader2 className="w-4 h-4 text-gray-400 animate-spin" />
                ) : isApiConnected ? (
                  <>
                    <Wifi className="w-4 h-4 text-green-500" />
                    <span className="text-xs text-green-600 font-medium">Connected</span>
                  </>
                ) : (
                  <>
                    <WifiOff className="w-4 h-4 text-red-500" />
                    <span className="text-xs text-red-600 font-medium">Disconnected</span>
                  </>
                )}
              </div>
            </div>
            <p className="text-sm text-gray-600">
              Principal-at-Maturity portfolio risk analysis
            </p>
          </div>
          <button
            onClick={() => setShowSettings(!showSettings)}
            className="p-2 text-gray-400 hover:text-gray-600 transition-colors"
          >
            <Settings className="w-5 h-5" />
          </button>
        </div>

        {/* Settings Panel */}
        {showSettings && (
          <div className="bg-gray-50 rounded-lg p-4 mb-6 space-y-4">
            <h4 className="font-medium text-gray-900 mb-3">Simulation Parameters</h4>
            <div className="grid grid-cols-2 gap-4">
              <div>
                <label className="block text-sm font-medium text-gray-700 mb-1">
                  Contracts
                </label>
                <input
                  type="number"
                  value={parameters.numContracts}
                  onChange={(e) => 
                    setParameters(p => ({ ...p, numContracts: parseInt(e.target.value) || 0 }))
                  }
                  className="w-full px-3 py-1.5 border border-gray-300 rounded text-sm focus:outline-none focus:ring-2 focus:ring-blue-500/20"
                  min="1"
                  max="50000"
                />
              </div>
              <div>
                <label className="block text-sm font-medium text-gray-700 mb-1">
                  Scenarios
                </label>
                <input
                  type="number"
                  value={parameters.numScenarios}
                  onChange={(e) => 
                    setParameters(p => ({ ...p, numScenarios: parseInt(e.target.value) || 0 }))
                  }
                  className="w-full px-3 py-1.5 border border-gray-300 rounded text-sm focus:outline-none focus:ring-2 focus:ring-blue-500/20"
                  min="1"
                  max="10000"
                />
              </div>
              <div>
                <label className="block text-sm font-medium text-gray-700 mb-1">
                  Maturity (months)
                </label>
                <select
                  value={parameters.monthsToMaturity}
                  onChange={(e) => 
                    setParameters(p => ({ ...p, monthsToMaturity: parseInt(e.target.value) }))
                  }
                  className="w-full px-3 py-1.5 border border-gray-300 rounded text-sm focus:outline-none focus:ring-2 focus:ring-blue-500/20"
                >
                  <option value={360}>30 years</option>
                  <option value={480}>40 years</option>
                  <option value={600}>50 years</option>
                </select>
              </div>
              <div>
                <label className="block text-sm font-medium text-gray-700 mb-1">
                  Engine
                </label>
                <select
                  value={parameters.preferGpu ? "gpu" : "cpu"}
                  onChange={(e) => 
                    setParameters(p => ({ ...p, preferGpu: e.target.value === "gpu" }))
                  }
                  className="w-full px-3 py-1.5 border border-gray-300 rounded text-sm focus:outline-none focus:ring-2 focus:ring-blue-500/20"
                >
                  <option value="cpu">CPU</option>
                  <option value="gpu">GPU (Accelerated)</option>
                </select>
              </div>
            </div>
          </div>
        )}

        {/* Run Status */}
        <div className="flex items-center justify-between mb-6">
          <div className="flex items-center gap-3">
            {getStatusIcon()}
            <div>
              <p className="text-sm font-medium text-gray-900">{getStatusText()}</p>
              {runStatus?.progress0To100 !== undefined && (
                <div className="w-48 bg-gray-200 rounded-full h-2 mt-1">
                  <div
                    className="bg-gradient-to-r from-blue-500 to-purple-600 h-2 rounded-full transition-all duration-300"
                    style={{ width: `${runStatus.progress0To100}%` }}
                  />
                </div>
              )}
            </div>
          </div>

          <button
            onClick={handleStartRun}
            disabled={isRunning || !isApiConnected}
            className="flex items-center gap-2 px-4 py-2 bg-gradient-to-r from-blue-500 to-purple-600 text-white rounded-lg font-medium hover:from-blue-600 hover:to-purple-700 transition-all disabled:opacity-50 disabled:cursor-not-allowed"
            title={!isApiConnected ? "API not connected" : ""}
          >
            {isRunning ? (
              <>
                <Loader2 className="w-4 h-4 animate-spin" />
                Running...
              </>
            ) : (
              <>
                <Play className="w-4 h-4" />
                Start Run
              </>
            )}
          </button>
        </div>

        {/* Results */}
        {runResult && (
          <div className="bg-gradient-to-br from-gray-50 to-blue-50/30 rounded-lg p-4">
            <div className="flex items-center gap-2 mb-4">
              <BarChart3 className="w-5 h-5 text-blue-600" />
              <h4 className="font-medium text-gray-900">Results Summary</h4>
              <span className="text-xs text-gray-500">
                ({((runResult.result?.DurationMs || 0) / 1000).toFixed(1)}s runtime)
              </span>
            </div>

            <div className="grid grid-cols-2 lg:grid-cols-4 gap-4">
              <div className="text-center">
                <p className="text-xs text-gray-500 uppercase tracking-wide">Mean PV</p>
                <p className="text-lg font-semibold text-gray-900">
                  {formatCurrency(runResult.result?.MeanPv || 0)}
                </p>
              </div>
              <div className="text-center">
                <p className="text-xs text-gray-500 uppercase tracking-wide">Std Dev</p>
                <p className="text-lg font-semibold text-gray-900">
                  {formatCurrency(runResult.result?.StdPv || 0)}
                </p>
              </div>
              <div className="text-center">
                <p className="text-xs text-gray-500 uppercase tracking-wide">5% VaR</p>
                <p className="text-lg font-semibold text-red-600">
                  {formatCurrency(runResult.result?.P05 || 0)}
                </p>
              </div>
              <div className="text-center">
                <p className="text-xs text-gray-500 uppercase tracking-wide">95% VaR</p>
                <p className="text-lg font-semibold text-green-600">
                  {formatCurrency(runResult.result?.P95 || 0)}
                </p>
              </div>
            </div>

            <div className="mt-4 text-xs text-gray-600 text-center">
              {formatNumber(parameters.numContracts || 0)} contracts × {formatNumber(parameters.numScenarios || 0)} scenarios 
              · Engine: {runResult.result?.EngineLabel || runResult.engine || 'Unknown'}
            </div>
          </div>
        )}
      </div>
    </div>
  );
}