"use client";

import Link from "next/link";
import Image from "next/image";
import { usePathname } from "next/navigation";
import {
  LayoutDashboard,
  FileText,
  TrendingUp,
  ShieldAlert,
  BarChart3,
  Settings,
  ChevronRight,
  Layers,
  Zap,
  Calculator,
} from "lucide-react";
import clsx from "clsx";

const navItems = [
  {
    group: "Main",
    items: [
      { label: "Dashboard", href: "/", icon: LayoutDashboard },
      { label: "Contracts", href: "/contracts", icon: FileText },
      { label: "Projections", href: "/projections", icon: TrendingUp },
    ],
  },
  {
    group: "Analysis",
    items: [
      { label: "Risk Analysis", href: "/risk", icon: ShieldAlert },
      { label: "Monte Carlo", href: "/monte-carlo", icon: Calculator },
      { label: "Reports", href: "/reports", icon: BarChart3 },
      { label: "Scenarios", href: "/scenarios", icon: Layers },
    ],
  },
  {
    group: "System",
    items: [
      { label: "Events", href: "/events", icon: Zap },
      { label: "Settings", href: "/settings", icon: Settings },
    ],
  },
];

export default function Sidebar() {
  const pathname = usePathname();

  return (
    <aside
      className="fixed left-0 top-0 h-screen w-60 flex flex-col z-40"
      style={{
        background: "var(--sidebar-gradient)",
        borderRight: "1px solid var(--border-color)",
      }}
    >
      {/* Logo */}
      <div
        className="flex items-center gap-3 px-5 py-5"
        style={{ borderBottom: "1px solid var(--border-color)" }}
      >
        <Image
          src="/logo_A_dark.svg"
          alt="Actus Logo"
          width={42}
          height={49}
          priority
        />
        <div>
          <p className="text-sm font-bold tracking-widest text-white uppercase">
            Actus
          </p>
          <p style={{ color: "var(--text-secondary)", fontSize: "10px" }}>
            Designer v1.0
          </p>
        </div>
      </div>

      {/* Navigation */}
      <nav className="flex-1 overflow-y-auto py-4 px-3">
        {navItems.map((group) => (
          <div key={group.group} className="mb-6">
            <p
              className="px-3 mb-2 font-semibold tracking-widest uppercase"
              style={{ color: "var(--text-muted)", fontSize: "10px" }}
            >
              {group.group}
            </p>
            <ul className="space-y-0.5">
              {group.items.map(({ label, href, icon: Icon }) => {
                const isActive = pathname === href;
                return (
                  <li key={href}>
                    <Link
                      href={href}
                      className={clsx(
                        "flex items-center gap-3 px-3 py-2.5 rounded-lg text-sm font-medium transition-all duration-150 group",
                        isActive ? "sidebar-item-active" : ""
                      )}
                      style={{
                        paddingLeft: isActive ? "11px" : undefined,
                        color: isActive ? "var(--text-primary)" : "var(--text-secondary)",
                        ...(isActive ? {} : { "--hover-bg": "var(--nav-hover-bg)" } as React.CSSProperties),
                      }}
                      onMouseEnter={(e) => {
                        if (!isActive) (e.currentTarget as HTMLElement).style.background = "var(--nav-hover-bg)";
                      }}
                      onMouseLeave={(e) => {
                        if (!isActive) (e.currentTarget as HTMLElement).style.background = "";
                      }}
                    >
                      <Icon
                        size={16}
                        className="flex-shrink-0"
                        style={{ color: isActive ? "var(--accent-cyan)" : "var(--text-muted)" }}
                      />
                      <span className="flex-1">{label}</span>
                      {isActive && (
                        <ChevronRight
                          size={12}
                          style={{ color: "var(--accent-cyan)", opacity: 0.7 }}
                        />
                      )}
                    </Link>
                  </li>
                );
              })}
            </ul>
          </div>
        ))}
      </nav>

      {/* Footer status */}
      <div
        className="px-4 py-4"
        style={{ borderTop: "1px solid var(--border-color)" }}
      >
        <div className="flex items-center gap-2.5 px-3 py-2.5 rounded-lg" style={{ background: "var(--bg-card)" }}>
          <div className="relative flex-shrink-0">
            <div className="w-2 h-2 rounded-full bg-emerald-400" />
            <div className="absolute inset-0 w-2 h-2 rounded-full bg-emerald-400 animate-ping opacity-60" />
          </div>
          <div>
            <p className="text-xs font-medium" style={{ color: "var(--text-primary)" }}>API Connected</p>
            <p style={{ color: "var(--text-muted)", fontSize: "10px" }}>
              {process.env.NEXT_PUBLIC_API_BASE_URL ?? "localhost:5001"}
            </p>
          </div>
        </div>
      </div>
    </aside>
  );
}
