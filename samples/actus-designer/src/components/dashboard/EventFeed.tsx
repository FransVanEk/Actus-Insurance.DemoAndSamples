"use client";

import useSWR from "swr";
import { AlertTriangle, CheckCircle, Info, Zap, type LucideIcon } from "lucide-react";
import { fetcher, API_ROUTES } from "@/lib/api";
import type { EventsResponse, EventSeverity } from "@/types/api";
import { Skeleton } from "@/components/ui/Skeleton";

const EVENT_CONFIG: Record<
  EventSeverity,
  { icon: LucideIcon; color: string }
> = {
  info:    { icon: Info,          color: "#06b6d4" },
  warning: { icon: AlertTriangle, color: "#f59e0b" },
  success: { icon: CheckCircle,   color: "#10b981" },
  error:   { icon: Zap,           color: "#f43f5e" },
};

export default function EventFeed() {
  const { data: response, isLoading } = useSWR<EventsResponse>(
    API_ROUTES.events({ limit: 5 }),
    fetcher,
    { refreshInterval: 15000 }
  );

  const events = response?.data ?? [];

  return (
    <div className="glass-card p-5 fade-in" style={{ animationDelay: "350ms" }}>
      <div className="flex items-center justify-between mb-4">
        <div>
          <h3 className="text-sm font-semibold" style={{ color: "var(--text-primary)" }}>Event Feed</h3>
          <p className="text-xs mt-0.5" style={{ color: "var(--text-secondary)" }}>
            Upcoming & recent ACTUS events
          </p>
        </div>
        <span
          className="text-xs px-2 py-0.5 rounded-full font-semibold"
          style={{
            background: "rgba(6, 182, 212, 0.1)",
            color: "#06b6d4",
            border: "1px solid rgba(6, 182, 212, 0.2)",
          }}
        >
          {isLoading ? "..." : `${response?.total ?? 0} events`}
        </span>
      </div>

      <div className="space-y-3">
        {isLoading
          ? Array.from({ length: 4 }).map((_, i) => (
              <Skeleton key={i} height={60} rounded="lg" />
            ))
          : events.map((event) => {
          const cfg = EVENT_CONFIG[event.type] ?? EVENT_CONFIG.info;
          const Icon = cfg.icon;
          return (
            <div
              key={event.id}
              className="flex items-start gap-3 p-3 rounded-lg transition-all duration-150 cursor-pointer"
              style={{ background: "var(--bg-elevated)" }}
              onMouseEnter={(e) => {
                (e.currentTarget as HTMLDivElement).style.background = "var(--bg-card)";
                (e.currentTarget as HTMLDivElement).style.outline = `1px solid ${cfg.color}30`;
              }}
              onMouseLeave={(e) => {
                (e.currentTarget as HTMLDivElement).style.background = "var(--bg-elevated)";
                (e.currentTarget as HTMLDivElement).style.outline = "none";
              }}
            >
              <div
                className="w-7 h-7 rounded-lg flex items-center justify-center flex-shrink-0 mt-0.5"
                style={{ background: `${cfg.color}18` }}
              >
                <Icon size={13} style={{ color: cfg.color }} />
              </div>
              <div className="flex-1 min-w-0">
                <p className="text-xs font-semibold" style={{ color: "var(--text-primary)" }}>{event.title}</p>
                <p
                  className="text-xs mt-0.5 truncate"
                  style={{ color: "var(--text-secondary)" }}
                >
                  {event.description}
                </p>
              </div>
              <span
                className="text-xs flex-shrink-0 mt-0.5"
                style={{ color: "var(--text-secondary)" }}
              >
                {event.relativeTime}
              </span>
            </div>
          );
        })}
      </div>
    </div>
  );
}
