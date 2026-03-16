"use client";

import { TrendingUp, TrendingDown, Minus } from "lucide-react";
import clsx from "clsx";
import type { LucideIcon } from "lucide-react";

interface KpiCardProps {
  title: string;
  value: string;
  subtitle?: string;
  change?: number;
  changeLabel?: string;
  icon: LucideIcon;
  accentColor?: string;
  delay?: number;
}

export default function KpiCard({
  title,
  value,
  subtitle,
  change,
  changeLabel,
  icon: Icon,
  accentColor = "#06b6d4",
  delay = 0,
}: KpiCardProps) {
  const isPositive = change !== undefined && change > 0;
  const isNegative = change !== undefined && change < 0;
  const isNeutral = change === 0;

  return (
    <div
      className="glass-card glass-card-hover fade-in p-5 flex flex-col gap-4"
      style={{ animationDelay: `${delay}ms` }}
    >
      {/* Header row */}
      <div className="flex items-start justify-between">
        <p
          className="text-xs font-semibold tracking-widest uppercase"
          style={{ color: "var(--text-muted)" }}
        >
          {title}
        </p>
        <div
          className="w-9 h-9 rounded-lg flex items-center justify-center flex-shrink-0"
          style={{ background: `${accentColor}18` }}
        >
          <Icon size={17} style={{ color: accentColor }} />
        </div>
      </div>

      {/* Value */}
      <div>
        <p className="text-2xl font-bold tracking-tight" style={{ color: "var(--text-primary)" }}>{value}</p>
        {subtitle && (
          <p className="text-xs mt-1" style={{ color: "var(--text-secondary)" }}>
            {subtitle}
          </p>
        )}
      </div>

      {/* Change indicator */}
      {change !== undefined && (
        <div className="flex items-center gap-1.5">
          <div
            className={clsx(
              "flex items-center gap-1 text-xs font-semibold px-2 py-0.5 rounded-full",
              isPositive && "bg-emerald-500/10 text-emerald-400",
              isNegative && "bg-rose-500/10 text-rose-400",
              isNeutral && "bg-slate-500/10 text-slate-400"
            )}
          >
            {isPositive && <TrendingUp size={11} />}
            {isNegative && <TrendingDown size={11} />}
            {isNeutral && <Minus size={11} />}
            <span>
              {isPositive ? "+" : ""}
              {change}%
            </span>
          </div>
          {changeLabel && (
            <p className="text-xs" style={{ color: "var(--text-muted)" }}>
              {changeLabel}
            </p>
          )}
        </div>
      )}
    </div>
  );
}
