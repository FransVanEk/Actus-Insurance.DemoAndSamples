"use client";

import useSWR from "swr";
import {
  RadarChart,
  PolarGrid,
  PolarAngleAxis,
  Radar,
  ResponsiveContainer,
  Tooltip,
} from "recharts";
import { fetcher, API_ROUTES } from "@/lib/api";
import type { RiskOverviewResponse } from "@/types/api";
import { Skeleton } from "@/components/ui/Skeleton";
import { useTheme } from "@/context/ThemeContext";

const CustomTooltip = ({
  active,
  payload,
}: {
  active?: boolean;
  payload?: { payload: { metric: string; value: number } }[];
}) => {
  if (active && payload && payload.length) {
    return (
      <div
        className="px-3 py-2 rounded-lg text-xs"
        style={{
          background: "var(--bg-card)",
          border: "1px solid var(--border-color)",
        }}
      >
        <p className="font-semibold" style={{ color: "var(--text-primary)" }}>{payload[0].payload.metric} Risk</p>
        <p style={{ color: "var(--accent-cyan)" }}>{payload[0].payload.value} / 100</p>
      </div>
    );
  }
  return null;
};

export default function RiskMetrics() {
  const { data: risk, isLoading } = useSWR<RiskOverviewResponse>(
    API_ROUTES.riskOverview(),
    fetcher,
    { refreshInterval: 60000, revalidateOnFocus: false }
  );

  const { theme } = useTheme();
  const gridStroke = theme === "dark" ? "#162438" : "#d1dded";
  const tickFill   = theme === "dark" ? "#6b8aad" : "#64748b";

  const radarData = risk?.radar ?? [];
  const metrics   = risk?.metrics ?? [];

  return (
    <div className="glass-card p-5 fade-in" style={{ animationDelay: "250ms" }}>
      <div className="mb-4">
        <h3 className="text-sm font-semibold" style={{ color: "var(--text-primary)" }}>Risk Overview</h3>
        <p className="text-xs mt-0.5" style={{ color: "var(--text-muted)" }}>
          {risk?.asOf
            ? `As of ${new Date(risk.asOf).toLocaleTimeString()}`
            : "Multi-dimensional risk scoring"}
        </p>
      </div>

      {/* Radar chart */}
      {isLoading ? (
        <Skeleton height={180} rounded="lg" />
      ) : (
      <ResponsiveContainer width="100%" height={180}>
        <RadarChart data={radarData}>
          <PolarGrid stroke={gridStroke} />
          <PolarAngleAxis
            dataKey="metric"
            tick={{ fill: tickFill, fontSize: 10 }}
          />
          <Radar
            name="Risk"
            dataKey="value"
            stroke="#06b6d4"
            fill="#06b6d4"
            fillOpacity={0.15}
            strokeWidth={1.5}
            dot={{ fill: "#06b6d4", r: 2 }}
          />
          <Tooltip content={<CustomTooltip />} />
        </RadarChart>
      </ResponsiveContainer>
      )}

      {/* Metrics grid */}
      <div className="mt-4 grid grid-cols-2 gap-2">
        {isLoading
          ? Array.from({ length: 4 }).map((_, i) => (
              <Skeleton key={i} height={72} rounded="lg" />
            ))
          : metrics.map((m) => (
          <div
            key={m.label}
            className="p-3 rounded-lg"
            style={{ background: "var(--bg-elevated)", border: "1px solid var(--border-subtle)" }}
          >
            <p
              className="text-xs font-medium"
              style={{ color: "var(--text-muted)" }}
            >
              {m.label}
            </p>
            <p className="text-sm font-bold mt-1" style={{ color: "var(--text-primary)" }}>{m.value}</p>
            <p
              className="text-xs mt-0.5"
              style={{
                color:
                  m.trend === "up" ? "#10b981"
                  : m.trend === "down" ? "#f59e0b"
                  : "#6b8aad",
              }}
            >
              {m.change}
            </p>
          </div>
        ))}
      </div>
    </div>
  );
}
