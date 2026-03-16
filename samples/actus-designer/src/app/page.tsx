"use client";

import useSWR from "swr";
import KpiCard from "@/components/dashboard/KpiCard";
import CashFlowChart from "@/components/dashboard/CashFlowChart";
import ContractTypeChart from "@/components/dashboard/ContractTypeChart";
import RecentContracts from "@/components/dashboard/RecentContracts";
import EventFeed from "@/components/dashboard/EventFeed";
import RiskMetrics from "@/components/dashboard/RiskMetrics";
import ApiConnectionStatus from "@/components/dashboard/ApiConnectionStatus";
import Header from "@/components/layout/Header";
import { KpiCardSkeleton } from "@/components/ui/Skeleton";
import { fetcher, API_ROUTES } from "@/lib/api";
import type { DashboardSummary } from "@/types/api";
import {
  FileText,
  TrendingUp,
  DollarSign,
  Activity,
  Plus,
  Play,
  Download,
  Upload,
} from "lucide-react";

const quickActions = [
  { label: "New Contract",  icon: Plus,     color: "#06b6d4" },
  { label: "Run Projection",icon: Play,     color: "#10b981" },
  { label: "Export Report", icon: Download, color: "#3b82f6" },
  { label: "Import Data",   icon: Upload,   color: "#f59e0b" },
];

export default function DashboardPage() {
  const { data: summary, isLoading } = useSWR<DashboardSummary>(
    API_ROUTES.dashboardSummary(),
    fetcher,
    { refreshInterval: 30000 }
  );

  const kpiData = summary
    ? [
        {
          title: "Active Contracts",
          value: summary.activeContracts.formatted,
          subtitle: "Across 6 ACTUS contract types",
          change: summary.activeContracts.changePercent,
          changeLabel: "vs last month",
          icon: FileText,
          accentColor: "#06b6d4",
          delay: 0,
        },
        {
          title: "Portfolio Notional",
          value: summary.portfolioNotional.formatted,
          subtitle: "Total outstanding principal",
          change: summary.portfolioNotional.changePercent,
          changeLabel: "vs last quarter",
          icon: DollarSign,
          accentColor: "#3b82f6",
          delay: 80,
        },
        {
          title: "Projected Cash Flow",
          value: summary.projectedCashFlow.formatted,
          subtitle: "Next 12 months",
          change: summary.projectedCashFlow.changePercent,
          changeLabel: "vs forecast",
          icon: TrendingUp,
          accentColor: "#10b981",
          delay: 160,
        },
        {
          title: "Events This Month",
          value: summary.eventsThisMonth.formatted,
          subtitle: "IP, PR, MD, OPTEX scheduled",
          change: summary.eventsThisMonth.changePercent,
          changeLabel: "vs last month",
          icon: Activity,
          accentColor: "#6366f1",
          delay: 240,
        },
      ]
    : [];

  return (
    <div className="flex flex-col min-h-screen">
      <Header
        title="Dashboard"
        subtitle="ACTUS Portfolio Overview · Last updated: just now"
      />

      <main className="flex-1 p-6 space-y-6">
        {/* Quick Actions */}
        <div className="flex items-center gap-3 fade-in">
          {quickActions.map(({ label, icon: Icon, color }) => (
            <button
              key={label}
              className="flex items-center gap-2 px-4 py-2 rounded-lg text-sm font-medium transition-all duration-150"
              style={{
                background: `${color}12`,
                color: color,
                border: `1px solid ${color}30`,
              }}
              onMouseEnter={(e) => {
                (e.currentTarget as HTMLButtonElement).style.background = `${color}20`;
                (e.currentTarget as HTMLButtonElement).style.borderColor = `${color}60`;
              }}
              onMouseLeave={(e) => {
                (e.currentTarget as HTMLButtonElement).style.background = `${color}12`;
                (e.currentTarget as HTMLButtonElement).style.borderColor = `${color}30`;
              }}
            >
              <Icon size={14} />
              {label}
            </button>
          ))}

          <div className="flex-1" />

          <ApiConnectionStatus />

          <div
            className="flex items-center gap-2 text-xs px-3 py-2 rounded-lg"
            style={{
              background: "var(--bg-card)",
              border: "1px solid var(--border-color)",
              color: "var(--text-muted)",
            }}
          >
            <span className="w-1.5 h-1.5 rounded-full bg-emerald-400 animate-pulse" />
            Scenario:{" "}
            <span className="font-medium ml-1" style={{ color: "var(--text-primary)" }}>Base Case</span>
          </div>
        </div>

        {/* KPI Cards */}
        <div className="grid grid-cols-4 gap-4">
          {isLoading
            ? Array.from({ length: 4 }).map((_, i) => <KpiCardSkeleton key={i} />)
            : kpiData.map((kpi) => <KpiCard key={kpi.title} {...kpi} />)}
        </div>

        {/* Charts row */}
        <div className="grid grid-cols-12 gap-4">
          <div className="col-span-7">
            <CashFlowChart />
          </div>
          <div className="col-span-5 grid grid-rows-2 gap-4">
            <ContractTypeChart />
            <RiskMetrics />
          </div>
        </div>

        {/* Bottom row: contracts table + event feed */}
        <div className="grid grid-cols-12 gap-4">
          <div className="col-span-8">
            <RecentContracts />
          </div>
          <div className="col-span-4">
            <EventFeed />
          </div>
        </div>
      </main>
    </div>
  );
}
