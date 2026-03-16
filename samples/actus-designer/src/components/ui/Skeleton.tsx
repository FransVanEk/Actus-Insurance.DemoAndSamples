import clsx from "clsx";

interface SkeletonProps {
  className?: string;
  height?: string | number;
  width?: string | number;
  rounded?: "sm" | "md" | "lg" | "full";
}

export function Skeleton({ className, height, width, rounded = "md" }: SkeletonProps) {
  const roundedMap = {
    sm: "rounded",
    md: "rounded-lg",
    lg: "rounded-xl",
    full: "rounded-full",
  };

  return (
    <div
      className={clsx("animate-pulse", roundedMap[rounded], className)}
      style={{
        height,
        width,
        background: "linear-gradient(90deg, var(--skeleton-start) 0%, var(--skeleton-mid) 50%, var(--skeleton-start) 100%)",
        backgroundSize: "200% 100%",
        animation: "shimmer 1.6s ease-in-out infinite",
      }}
    />
  );
}

export function KpiCardSkeleton() {
  return (
    <div className="glass-card p-5 flex flex-col gap-4">
      <div className="flex items-start justify-between">
        <Skeleton height={10} width={80} />
        <Skeleton height={36} width={36} rounded="lg" />
      </div>
      <div className="space-y-2">
        <Skeleton height={28} width={120} />
        <Skeleton height={10} width={160} />
      </div>
      <Skeleton height={20} width={100} rounded="full" />
    </div>
  );
}

export function ChartSkeleton({ height = 260 }: { height?: number }) {
  return (
    <div className="glass-card p-5">
      <div className="flex items-start justify-between mb-6">
        <div className="space-y-2">
          <Skeleton height={14} width={160} />
          <Skeleton height={10} width={220} />
        </div>
        <div className="flex gap-2">
          {[1, 2, 3, 4].map((i) => (
            <Skeleton key={i} height={28} width={36} rounded="md" />
          ))}
        </div>
      </div>
      <Skeleton height={height} rounded="lg" />
    </div>
  );
}

export function TableSkeleton({ rows = 6 }: { rows?: number }) {
  return (
    <div className="glass-card">
      <div
        className="flex items-center justify-between px-5 py-4"
        style={{ borderBottom: "1px solid var(--border-subtle)" }}
      >
        <div className="space-y-2">
          <Skeleton height={14} width={140} />
          <Skeleton height={10} width={200} />
        </div>
        <Skeleton height={30} width={80} rounded="lg" />
      </div>
      <div className="p-4 space-y-2">
        {Array.from({ length: rows }).map((_, i) => (
          <Skeleton key={i} height={44} rounded="md" />
        ))}
      </div>
    </div>
  );
}
