"use client";

import { useState, useEffect } from "react";
import { Wifi, WifiOff, AlertTriangle } from "lucide-react";

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

export default function ApiConnectionStatus() {
  const [isConnected, setIsConnected] = useState<boolean | null>(null);
  const [lastChecked, setLastChecked] = useState<Date | null>(null);

  useEffect(() => {
    const checkStatus = async () => {
      const connected = await checkConnection();
      setIsConnected(connected);
      setLastChecked(new Date());
    };

    // Check immediately
    checkStatus();
    
    // Then check every 30 seconds
    const interval = setInterval(checkStatus, 30000);
    return () => clearInterval(interval);
  }, []);

  if (isConnected === null) {
    return null; // Don't show anything while checking
  }

  return (
    <div className={`flex items-center gap-2 text-xs px-3 py-2 rounded-lg transition-all ${
      isConnected 
        ? "bg-emerald-50 text-emerald-700 border border-emerald-200" 
        : "bg-yellow-50 text-yellow-700 border border-yellow-200"
    }`}>
      {isConnected ? (
        <>
          <Wifi className="w-3.5 h-3.5" />
          <span>API Connected</span>
        </>
      ) : (
        <>
          <WifiOff className="w-3.5 h-3.5" />
          <span>API Offline - Using Mock Data</span>
        </>
      )}
      {lastChecked && (
        <span className="text-gray-500 ml-1">
          · {lastChecked.toLocaleTimeString().slice(-8, -3)}
        </span>
      )}
    </div>
  );
}