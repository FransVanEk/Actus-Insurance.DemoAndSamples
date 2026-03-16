"use client";

import useSWR from "swr";
import {
  PieChart,
  Pie,
  Cell,
  ResponsiveContainer,
  Tooltip,
} from "recharts";
import { fetcher, API_ROUTES } from "@/lib/api";
import type { ContractsSummaryResponse } from "@/types/api";
import { Skeleton } from "@/components/ui/Skeleton";

const TYPE_COLORS: Record<string, string> = {
  PAM: "#06b6d4",
  ANN: "#3b82f6",
  LAM: "#6366f1",
  NAM: "#10b981",
  CLM: "#f59e0b",
  Other: "#64748b",
};

const CustomTooltip = ({
  active,
  payload,
}: {
  active?: boolean;
  payload?: { payload: { name: string; fullName: string; value: number }; color: string }[];
}) => {
  if (active && payload && payload.length) {
    const d = payload[0].payload;
    return (
      <div
        className="px-4 py-3 rounded-xl text-sm"
        style={{
          background: "var(--bg-card)",
          border: "1px solid var(--border-color)",
          boxShadow: "0 8px 32px rgba(0,0,0,0.25)",
        }}
      >
        <p className="font-bold text-sm" style={{ color: "var(--text-primary)" }}>{d.name}</p>
        <p className="text-xs mt-0.5" style={{ color: "var(--text-secondary)" }}>
          {d.fullName}
        </p>
        <p className="text-lg font-bold mt-1" style={{ color: payload[0].color }}>
          {d.value}%
        </p>
      </div>
    );
  }
  return null;
};

export default function ContractTypeChart() {
  const { data: summary, isLoading } = useSWR<ContractsSummaryResponse>(
    API_ROUTES.contractsSummary(),
    fetcher,
    { revalidateOnFocus: false }
  );

  const data = (summary?.distribution ?? []).map((d) => ({
    ...d,
    color: TYPE_COLORS[d.name] ?? "#3d5470",
  }));

  return (
    <div className="glass-card p-5 fade-in" style={{ animationDelay: "300ms" }}>
      <div className="mb-5">
        <h3 className="text-sm font-semibold" style={{ color: "var(--text-primary)" }}>Contract Types</h3>
        <p className="text-xs mt-0.5" style={{ color: "var(--text-muted)" }}>
          {isLoading ? "Loading..." : `${summary?.totalContracts.toLocaleString()} contracts · by ACTUS type`}
        </p>
      </div>

      {isLoading ? (
        <Skeleton height={130} rounded="lg" />
      ) : (
      <div className="flex items-center gap-4">
        {/* Pie chart */}
        <div className="flex-shrink-0" style={{ width: 130, height: 130 }}>
          <ResponsiveContainer width="100%" height="100%">
            <PieChart>
              <Pie
                data={data}
                cx="50%"
                cy="50%"
                innerRadius={38}
                outerRadius={58}
                paddingAngle={2}
                dataKey="value"
              >
                {data.map((entry, index) => (
                  <Cell key={index} fill={entry.color} stroke="transparent" />
                ))}
              </Pie>
              <Tooltip content={<CustomTooltip />} />
            </PieChart>
          </ResponsiveContainer>
        </div>

        {/* Legend */}
        <div className="flex-1 space-y-2">
          {data.map((entry) => (
            <div key={entry.name} className="flex items-center gap-2.5">
              <div
                className="w-2 h-2 rounded-full flex-shrink-0"
                style={{ background: entry.color }}
              />
              <div className="flex-1 flex items-center justify-between min-w-0">
                <div className="min-w-0">
                  <span
                    className="text-xs font-semibold block"
                    style={{ color: "var(--text-primary)" }}
                  >
                    {entry.name}
                  </span>
                </div>
                <span
                  className="text-xs font-semibold ml-2 flex-shrink-0"
                  style={{ color: entry.color }}
                >
                  {entry.value}%
                </span>
              </div>
            </div>
          ))}
        </div>
      </div>
      )}
    </div>
  );
}
