"use client";

import { Bell, Search, User, RefreshCw, ChevronDown, Sun, Moon } from "lucide-react";
import { useState } from "react";
import { useTheme } from "@/context/ThemeContext";

interface HeaderProps {
  title: string;
  subtitle?: string;
}

export default function Header({ title, subtitle }: HeaderProps) {
  const [searchFocused, setSearchFocused] = useState(false);
  const { theme, toggleTheme } = useTheme();

  return (
    <header
      className="flex items-center justify-between px-7 py-4 sticky top-0 z-30"
      style={{
        background: "var(--header-bg)",
        backdropFilter: "blur(12px)",
        borderBottom: "1px solid var(--border-color)",
      }}
    >
      {/* Left: Page title */}
      <div>
        <h1 className="text-lg font-semibold" style={{ color: "var(--text-primary)" }}>{title}</h1>
        {subtitle && (
          <p className="text-xs mt-0.5" style={{ color: "var(--text-muted)" }}>
            {subtitle}
          </p>
        )}
      </div>

      {/* Center: Search */}
      <div
        className="relative flex items-center gap-2 px-3.5 py-2 rounded-lg transition-all duration-200"
        style={{
          background: searchFocused ? "var(--bg-input-focus)" : "var(--bg-elevated)",
          border: `1px solid ${searchFocused ? "var(--accent-cyan)" : "var(--border-color)"}`,
          color: "var(--text-primary)",
          width: "280px",
          boxShadow: searchFocused ? "0 0 0 3px rgba(6, 182, 212, 0.1)" : "none",
        }}
      >
        <Search size={14} style={{ color: "var(--text-muted)" }} />
        <input
          type="text"
          placeholder="Search contracts, events..."
          className="flex-1 bg-transparent text-sm outline-none"
          style={{ color: "var(--text-primary)" }}
          onFocus={() => setSearchFocused(true)}
          onBlur={() => setSearchFocused(false)}
        />
        <kbd
          className="text-xs px-1.5 py-0.5 rounded"
          style={{
            background: "var(--kbd-bg)",
            color: "var(--text-muted)",
            fontSize: "10px",
            border: "1px solid var(--border-color)",
          }}
        >
          ⌘K
        </kbd>
      </div>

      {/* Right: Actions */}
      <div className="flex items-center gap-3">
        {/* Last updated */}
        <button
          className="flex items-center gap-1.5 text-xs px-3 py-1.5 rounded-lg transition-colors duration-150"
          style={{
            color: "var(--text-secondary)",
            background: "var(--bg-elevated)",
            border: "1px solid var(--border-color)",
          }}
          onMouseEnter={(e) => {
            (e.currentTarget as HTMLButtonElement).style.borderColor = "var(--accent-cyan)";
            (e.currentTarget as HTMLButtonElement).style.color = "var(--accent-cyan)";
          }}
          onMouseLeave={(e) => {
            (e.currentTarget as HTMLButtonElement).style.borderColor = "var(--border-color)";
            (e.currentTarget as HTMLButtonElement).style.color = "var(--text-secondary)";
          }}
        >
          <RefreshCw size={12} />
          <span>Refresh</span>
        </button>

        {/* Theme toggle */}
        <button
          onClick={toggleTheme}
          className="p-2 rounded-lg transition-colors duration-150"
          title={theme === "dark" ? "Switch to light theme" : "Switch to dark theme"}
          style={{ background: "var(--bg-elevated)", border: "1px solid var(--border-color)" }}
          onMouseEnter={(e) => {
            (e.currentTarget as HTMLButtonElement).style.borderColor = "var(--accent-cyan)";
          }}
          onMouseLeave={(e) => {
            (e.currentTarget as HTMLButtonElement).style.borderColor = "var(--border-color)";
          }}
        >
          {theme === "dark" ? (
            <Sun size={15} style={{ color: "var(--accent-amber)" }} />
          ) : (
            <Moon size={15} style={{ color: "var(--accent-cyan)" }} />
          )}
        </button>

        {/* Notifications */}
        <button
          className="relative p-2 rounded-lg transition-colors duration-150"
          style={{ background: "var(--bg-elevated)", border: "1px solid var(--border-color)" }}
          onMouseEnter={(e) => {
            (e.currentTarget as HTMLButtonElement).style.borderColor = "var(--accent-cyan)";
          }}
          onMouseLeave={(e) => {
            (e.currentTarget as HTMLButtonElement).style.borderColor = "var(--border-color)";
          }}
        >
          <Bell size={15} style={{ color: "var(--text-secondary)" }} />
          <span
            className="absolute top-1.5 right-1.5 w-1.5 h-1.5 rounded-full bg-rose-500"
          />
        </button>

        {/* User */}
        <button
          className="flex items-center gap-2 px-3 py-1.5 rounded-lg transition-colors duration-150"
          style={{ background: "var(--bg-elevated)", border: "1px solid var(--border-color)" }}
          onMouseEnter={(e) => {
            (e.currentTarget as HTMLButtonElement).style.borderColor = "var(--accent-cyan)";
          }}
          onMouseLeave={(e) => {
            (e.currentTarget as HTMLButtonElement).style.borderColor = "var(--border-color)";
          }}
        >
          <div
            className="w-6 h-6 rounded-full flex items-center justify-center"
            style={{
              background: "linear-gradient(135deg, var(--accent-cyan), var(--accent-blue))",
            }}
          >
            <User size={12} className="text-white" />
          </div>
          <span className="text-sm font-medium" style={{ color: "var(--text-primary)" }}>Analyst</span>
          <ChevronDown size={12} style={{ color: "var(--text-muted)" }} />
        </button>
      </div>
    </header>
  );
}
