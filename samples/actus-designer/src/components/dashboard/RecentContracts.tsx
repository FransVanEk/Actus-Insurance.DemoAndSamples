"use client";

import useSWR from "swr";
import { ExternalLink, Clock, AlertCircle } from "lucide-react";
import clsx from "clsx";
import { fetcher, API_ROUTES } from "@/lib/api";
import type { ContractsResponse } from "@/types/api";
import { TableSkeleton } from "@/components/ui/Skeleton";

const statusConfig: Record<
  string,
  { label: string; bg: string; text: string; dot: string }
> = {
  active: {
    label: "Active",
    bg: "rgba(16, 185, 129, 0.1)",
    text: "#10b981",
    dot: "#10b981",
  },
  monitoring: {
    label: "Monitoring",
    bg: "rgba(245, 158, 11, 0.1)",
    text: "#f59e0b",
    dot: "#f59e0b",
  },
  expiring: {
    label: "Expiring",
    bg: "rgba(244, 63, 94, 0.1)",
    text: "#f43f5e",
    dot: "#f43f5e",
  },
  review: {
    label: "In Review",
    bg: "rgba(99, 102, 241, 0.1)",
    text: "#6366f1",
    dot: "#6366f1",
  },
};

const typeColors: Record<string, string> = {
  PAM: "#06b6d4",
  ANN: "#3b82f6",
  LAM: "#6366f1",
  NAM: "#10b981",
  CLM: "#f59e0b",
};

export default function RecentContracts() {
  const { data: response, isLoading, error } = useSWR<ContractsResponse>(
    API_ROUTES.contracts({ limit: 6 }),
    fetcher,
    { refreshInterval: 30000 }
  );

  const contracts = response?.data ?? [];

  if (isLoading) return <TableSkeleton rows={6} />;

  if (error) {
    return (
      <div className="glass-card p-8 flex items-center justify-center gap-3">
        <AlertCircle size={16} style={{ color: "#f43f5e" }} />
        <p className="text-sm" style={{ color: "#f43f5e" }}>Failed to load contracts</p>
      </div>
    );
  }

  return (
    <div
      className="glass-card fade-in"
      style={{ animationDelay: "400ms" }}
    >
      {/* Header */}
      <div
        className="flex items-center justify-between px-5 py-4"
        style={{ borderBottom: "1px solid var(--border-subtle)" }}
      >
        <div>
          <h3 className="text-sm font-semibold" style={{ color: "var(--text-primary)" }}>Recent Contracts</h3>
          <p className="text-xs mt-0.5" style={{ color: "var(--text-muted)" }}>
            Live portfolio — {response?.total.toLocaleString()} total · {contracts.length} shown
          </p>
        </div>
        <button
          className="flex items-center gap-1.5 text-xs px-3 py-1.5 rounded-lg transition-all duration-150"
          style={{
            color: "#06b6d4",
            background: "rgba(6, 182, 212, 0.08)",
            border: "1px solid rgba(6, 182, 212, 0.2)",
          }}
          onMouseEnter={(e) => {
            (e.currentTarget as HTMLButtonElement).style.background = "rgba(6, 182, 212, 0.15)";
          }}
          onMouseLeave={(e) => {
            (e.currentTarget as HTMLButtonElement).style.background = "rgba(6, 182, 212, 0.08)";
          }}
        >
          <ExternalLink size={11} />
          View All
        </button>
      </div>

      {/* Table */}
      <div className="overflow-x-auto">
        <table className="w-full text-sm">
          <thead>
            <tr style={{ borderBottom: "1px solid var(--border-subtle)" }}>
              {["Contract ID", "Type", "Counterparty", "Notional", "Maturity", "Next Event", "Status"].map(
                (col) => (
                  <th
                    key={col}
                    className="px-5 py-3 text-left text-xs font-semibold tracking-widest uppercase"
                    style={{ color: "var(--text-muted)" }}
                  >
                    {col}
                  </th>
                )
              )}
            </tr>
          </thead>
          <tbody>
            {contracts.map((contract, i) => {
              const status = statusConfig[contract.status];
              return (
                <tr
                  key={contract.id}
                  className="transition-colors duration-100 group cursor-pointer"
                  style={{
                    borderBottom: i < contracts.length - 1 ? "1px solid var(--border-subtle)" : "none",
                  }}
                  onMouseEnter={(e) => {
                    (e.currentTarget as HTMLTableRowElement).style.background = "var(--bg-card-hover)";
                  }}
                  onMouseLeave={(e) => {
                    (e.currentTarget as HTMLTableRowElement).style.background = "transparent";
                  }}
                >
                  <td className="px-5 py-3.5 font-mono text-xs font-medium" style={{ color: "var(--text-primary)" }}>
                    {contract.id}
                  </td>
                  <td className="px-5 py-3.5">
                    <span
                      className="text-xs font-bold px-2 py-0.5 rounded"
                      style={{
                        color: typeColors[contract.type] || "#a8c4e0",
                        background: `${typeColors[contract.type] || "#a8c4e0"}18`,
                      }}
                    >
                      {contract.type}
                    </span>
                  </td>
                  <td
                    className="px-5 py-3.5 text-sm"
                    style={{ color: "var(--text-secondary)" }}
                  >
                    {contract.counterparty}
                  </td>
                  <td className="px-5 py-3.5 font-semibold text-sm" style={{ color: "var(--text-primary)" }}>
                    {contract.notional}
                  </td>
                  <td
                    className="px-5 py-3.5 text-xs font-mono"
                    style={{ color: "var(--text-secondary)" }}
                  >
                    {contract.maturity}
                  </td>
                  <td className="px-5 py-3.5">
                    <div className="flex items-center gap-1.5">
                      <Clock size={11} style={{ color: "var(--text-muted)" }} />
                      <span
                        className="text-xs font-mono"
                        style={{ color: "var(--text-secondary)" }}
                      >
                        {contract.nextEvent}
                      </span>
                    </div>
                  </td>
                  <td className="px-5 py-3.5">
                    <span
                      className={clsx("status-badge")}
                      style={{
                        background: status.bg,
                        color: status.text,
                      }}
                    >
                      <span
                        className="w-1.5 h-1.5 rounded-full"
                        style={{ background: status.dot }}
                      />
                      {status.label}
                    </span>
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>
    </div>
  );
}
