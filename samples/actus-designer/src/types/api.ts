// ─── Dashboard Summary ────────────────────────────────────────────────────────

export interface KpiMetric {
  value: number;
  formatted: string;
  changePercent: number;
}

export interface DashboardSummary {
  activeContracts: KpiMetric;
  portfolioNotional: KpiMetric;
  projectedCashFlow: KpiMetric;
  eventsThisMonth: KpiMetric;
}

// ─── Contracts ────────────────────────────────────────────────────────────────

export type ContractStatus = "active" | "monitoring" | "expiring" | "review";

export type ContractType = "PAM" | "ANN" | "LAM" | "NAM" | "CLM" | "UMP" | "CSH" | "STK";

export interface Contract {
  id: string;
  type: ContractType;
  counterparty: string;
  notional: string;
  currency: string;
  maturity: string;
  status: ContractStatus;
  nextEvent: string;
}

export interface ContractsResponse {
  data: Contract[];
  total: number;
  page: number;
  pageSize: number;
}

// ─── Contract Type Summary ────────────────────────────────────────────────────

export interface ContractTypeSummary {
  name: ContractType | "Other";
  fullName: string;
  value: number; // percentage
  count: number;
}

export interface ContractsSummaryResponse {
  distribution: ContractTypeSummary[];
  totalContracts: number;
}

// ─── Cash Flow Projections ────────────────────────────────────────────────────

export interface CashFlowDataPoint {
  month: string;
  principal: number;
  interest: number;
  fees: number;
}

export interface ProjectionsResponse {
  data: CashFlowDataPoint[];
  year: number;
  scenario: string;
  currency: string;
}

// ─── Risk ─────────────────────────────────────────────────────────────────────

export interface RiskRadarPoint {
  metric: string;
  value: number; // 0–100 score
}

export interface RiskKpiMetric {
  label: string;
  value: string;
  change: string;
  trend: "up" | "down" | "stable";
}

export interface RiskOverviewResponse {
  radar: RiskRadarPoint[];
  metrics: RiskKpiMetric[];
  asOf: string;
}

// ─── Events ───────────────────────────────────────────────────────────────────

export type EventSeverity = "info" | "warning" | "success" | "error";

export interface ActusEvent {
  id: string;
  type: EventSeverity;
  title: string;
  contractId: string;
  description: string;
  relativeTime: string;
  timestamp: string;
}

export interface EventsResponse {
  data: ActusEvent[];
  total: number;
}

// ─── PAM Monte Carlo ──────────────────────────────────────────────────────────

export interface PamMonteCarloRequest {
  numContracts?: number;
  numScenarios?: number;
  monthsToMaturity?: number;
  calcDateIndex?: number;
  seed?: number;  
  baseDate?: string;
  preferGpu?: boolean;
  description?: string;
  // File upload mode (alternative to synthetic generation)
  portfolioCsv?: string;
  metadataCsv?: string;
  scenarioJson?: string;
}

export interface PamMonteCarloResponse {
  runId: string;
  statusUrl: string;
  resultUrl: string;
  state: string;
  description: string;
}

export type RunState = "Queued" | "Running" | "Completed" | "Failed" | "Cancelled";

export interface RunStatus {
  runId: string;
  state: RunState;
  progress0To100: number;
  message?: string;
  createdAt: string;
  startedAt?: string;
  updatedAt: string;
  engine?: string;
  metrics?: Record<string, any>;
}

export interface RunResult {
  runId: string;
  state: string;
  engine: string;
  result: {
    EngineLabel: string;
    PortfolioPvByScenario: number[];
    MeanPv: number;
    StdPv: number;
    P05: number;
    P95: number;
    DurationMs: number;
    Metrics: Record<string, any>;
  };
  completedAt: string;
}

// ─── Generic API wrapper ──────────────────────────────────────────────────────

export interface ApiResponse<T> {
  success: boolean;
  data?: T;
  error?: string;
  timestamp: string;
}
