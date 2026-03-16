"use client";

import { useState } from "react";
import useSWR from "swr";
import {
  AreaChart,
  Area,
  XAxis,
  YAxis,
  CartesianGrid,
  Tooltip,
  ResponsiveContainer,
  Legend,
} from "recharts";
import { fetcher, API_ROUTES } from "@/lib/api";
import type { ProjectionsResponse } from "@/types/api";
import { ChartSkeleton } from "@/components/ui/Skeleton";
import { useTheme } from "@/context/ThemeContext";

const PERIODS = [
  { label: "1M", months: 1 },
  { label: "3M", months: 3 },
  { label: "6M", months: 6 },
  { label: "1Y", months: 12 },
] as const;

const CustomTooltip = ({
  active,
  payload,
  label,
}: {
  active?: boolean;
  payload?: { color: string; name: string; value: number }[];
  label?: string;
}) => {
  if (active && payload && payload.length) {
    return (
      <div
        className="px-4 py-3 rounded-xl text-sm"
        style={{
          background: "var(--bg-card)",
          border: "1px solid var(--border-color)",
          boxShadow: "0 8px 32px rgba(0,0,0,0.25)",
        }}
      >
        <p
          className="font-semibold mb-2 text-xs tracking-widest uppercase"
          style={{ color: "var(--text-muted)" }}
        >
          {label} 2025
        </p>
        {payload.map((entry) => (
          <div key={entry.name} className="flex items-center gap-2 py-0.5">
            <div
              className="w-2 h-2 rounded-full flex-shrink-0"
              style={{ background: entry.color }}
            />
            <span className="text-xs capitalize" style={{ color: "var(--text-secondary)" }}>
              {entry.name}:
            </span>
            <span className="text-xs font-semibold" style={{ color: "var(--text-primary)" }}>
              ${(entry.value / 1000000).toFixed(2)}M
            </span>
          </div>
        ))}
      </div>
    );
  }
  return null;
};

export default function CashFlowChart() {
  const [activePeriod, setActivePeriod] = useState<number>(3);
  const [scenario, setScenario] = useState<"base" | "stress">("base");
  const { theme } = useTheme();
  const gridColor = theme === "dark" ? "#162438" : "#d1dded";
  const tickColor = theme === "dark" ? "#6b8aad" : "#64748b";
  const dotStroke = theme === "dark" ? "#0e1a2e" : "#f0f5fb";

  const year = new Date().getFullYear();
  const { data: projections, isLoading } = useSWR<ProjectionsResponse>(
    API_ROUTES.projections({ year, scenario }),
    fetcher,
    { revalidateOnFocus: false }
  );

  if (isLoading) return <ChartSkeleton height={260} />;

  const allData = projections?.data ?? [];
  const months = PERIODS[activePeriod].months;
  const chartData = months < 12 ? allData.slice(-months) : allData;

  return (
    <div className="glass-card p-5 fade-in" style={{ animationDelay: "200ms" }}>
      <div className="flex items-start justify-between mb-6">
        <div>
          <h3 className="text-sm font-semibold" style={{ color: "var(--text-primary)" }}>
            Cash Flow Projections
          </h3>
          <p className="text-xs mt-0.5" style={{ color: "var(--text-muted)" }}>
            Principal · Interest · Fees — FY {projections?.year ?? year}
            {" · "}
            <button
              className="font-medium transition-colors"
              style={{ color: scenario === "stress" ? "#f59e0b" : "#06b6d4" }}
              onClick={() => setScenario(s => s === "base" ? "stress" : "base")}
            >
              {projections?.scenario}
            </button>
          </p>
        </div>
        <div className="flex items-center gap-2">
          {PERIODS.map((period, i) => (
            <button
              key={period.label}
              onClick={() => setActivePeriod(i)}
              className="text-xs px-2.5 py-1 rounded-md transition-all duration-150"
              style={{
                background: activePeriod === i ? "rgba(6, 182, 212, 0.15)" : "transparent",
                color: activePeriod === i ? "#06b6d4" : "var(--text-muted)",
                border: `1px solid ${activePeriod === i ? "rgba(6, 182, 212, 0.3)" : "transparent"}`,
              }}
            >
              {period.label}
            </button>
          ))}
        </div>
      </div>

      <ResponsiveContainer width="100%" height={260}>
        <AreaChart data={chartData} margin={{ top: 0, right: 0, left: -10, bottom: 0 }}>
          <defs>
            <linearGradient id="principalGrad" x1="0" y1="0" x2="0" y2="1">
              <stop offset="5%" stopColor="#06b6d4" stopOpacity={0.3} />
              <stop offset="95%" stopColor="#06b6d4" stopOpacity={0.02} />
            </linearGradient>
            <linearGradient id="interestGrad" x1="0" y1="0" x2="0" y2="1">
              <stop offset="5%" stopColor="#3b82f6" stopOpacity={0.3} />
              <stop offset="95%" stopColor="#3b82f6" stopOpacity={0.02} />
            </linearGradient>
            <linearGradient id="feesGrad" x1="0" y1="0" x2="0" y2="1">
              <stop offset="5%" stopColor="#10b981" stopOpacity={0.3} />
              <stop offset="95%" stopColor="#10b981" stopOpacity={0.02} />
            </linearGradient>
          </defs>
          <CartesianGrid strokeDasharray="3 3" stroke={gridColor} vertical={false} />
          <XAxis
            dataKey="month"
            tick={{ fill: tickColor, fontSize: 11 }}
            axisLine={false}
            tickLine={false}
          />
          <YAxis
            tick={{ fill: tickColor, fontSize: 11 }}
            axisLine={false}
            tickLine={false}
            tickFormatter={(v) => `$${(v / 1000000).toFixed(1)}M`}
          />
          <Tooltip content={<CustomTooltip />} />
          <Legend
            wrapperStyle={{ paddingTop: "16px" }}
            formatter={(value) => (
              <span style={{ color: "var(--text-secondary)", fontSize: "11px", textTransform: "capitalize" }}>
                {value}
              </span>
            )}
          />
          <Area
            type="monotone"
            dataKey="principal"
            stroke="#06b6d4"
            strokeWidth={2}
            fill="url(#principalGrad)"
            dot={false}
            activeDot={{ r: 4, fill: "#06b6d4", stroke: dotStroke, strokeWidth: 2 }}
          />
          <Area
            type="monotone"
            dataKey="interest"
            stroke="#3b82f6"
            strokeWidth={2}
            fill="url(#interestGrad)"
            dot={false}
            activeDot={{ r: 4, fill: "#3b82f6", stroke: dotStroke, strokeWidth: 2 }}
          />
          <Area
            type="monotone"
            dataKey="fees"
            stroke="#10b981"
            strokeWidth={2}
            fill="url(#feesGrad)"
            dot={false}
            activeDot={{ r: 4, fill: "#10b981", stroke: dotStroke, strokeWidth: 2 }}
          />
        </AreaChart>
      </ResponsiveContainer>
    </div>
  );
}
