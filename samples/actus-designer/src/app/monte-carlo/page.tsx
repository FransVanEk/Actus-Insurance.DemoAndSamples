"use client";

import { useState, useEffect } from "react";
import { Play, Settings, Loader2, CheckCircle, AlertCircle, Upload, FileText, BarChart3, Download, X } from "lucide-react";
import type { PamMonteCarloRequest, PamMonteCarloResponse, RunStatus, RunResult } from "@/types/api";
import useSWR from "swr";
import { fetcher, API_ROUTES } from "@/lib/api"; 
import Header from "@/components/layout/Header";

// Check API connection from client-side
async function checkConnection(): Promise<boolean> {
  try {
    const response = await fetch('/api/health');
    const result = await response.json();
    return result.success && result.data?.backend;
  } catch {
    return false;
  }
}

export default function PamMonteCarloPage() {
  const [isRunning, setIsRunning] = useState(false);
  const [activeRunId, setActiveRunId] = useState<string | null>(null);
  const [showSettings, setShowSettings] = useState(false);
  const [isApiConnected, setIsApiConnected] = useState<boolean | null>(null);
  
  // Mode selection: 'parameters' or 'files'
  const [mode, setMode] = useState<'parameters' | 'files'>('parameters');
  
  // File upload state
  const [portfolioFile, setPortfolioFile] = useState<File | null>(null);
  const [metadataFile, setMetadataFile] = useState<File | null>(null);
  const [uploadProgress, setUploadProgress] = useState<{ portfolio?: number; metadata?: number }>({});

  const [parameters, setParameters] = useState<PamMonteCarloRequest>({
    numContracts: 1000,
    numScenarios: 100,
    monthsToMaturity: 600, // 50 years
    calcDateIndex: 0,
    seed: 12345,
    preferGpu: false,
    description: "PAM Monte Carlo Analysis"
  });

  useEffect(() => {
    const checkConnectionStatus = async () => {
      const connected = await checkConnection();
      setIsApiConnected(connected);
    };
    
    checkConnectionStatus();
    const interval = setInterval(checkConnectionStatus, 30000);
    return () => clearInterval(interval);
  }, []);

  // Force initial connection state for demo
  useEffect(() => {
    if (isApiConnected === null) {
      const timer = setTimeout(() => {
        if (isApiConnected === null) {
          setIsApiConnected(true);
        }
      }, 2000);
      return () => clearTimeout(timer);
    }
  }, [isApiConnected]);

  const readFileAsText = (file: File): Promise<string> => {
    return new Promise((resolve, reject) => {
      const reader = new FileReader();
      reader.onload = (event) => resolve(event.target?.result as string);
      reader.onerror = (error) => reject(error);
      reader.readAsText(file);
    });
  };

  // CSV validation dialog state
  const [csvErrorDialog, setCsvErrorDialog] = useState<{ fileName: string; errors: string[] } | null>(null);

  const REQUIRED_PORTFOLIO_COLUMNS = [
    'ContractId',
    'InitialExchangeDate',
    'MaturityDate',
    'NotionalPrincipal',
    'NominalInterestRate',
  ];

  // ── Cultural-aware CSV helpers ──────────────────────────────────────────

  /** Detect column delimiter by counting occurrences in the header row. */
  const detectDelimiter = (headerLine: string): string => {
    const candidates: Record<string, number> = {
      ';': (headerLine.match(/;/g) ?? []).length,
      '\t': (headerLine.match(/\t/g) ?? []).length,
      ',': (headerLine.match(/,/g) ?? []).length,
    };
    return Object.entries(candidates).sort((a, b) => b[1] - a[1])[0][0];
  };

  /** Split a single CSV line respecting quoted fields and the detected delimiter. */
  const splitCsvLine = (line: string, delimiter: string): string[] => {
    const result: string[] = [];
    let inQuotes = false;
    let current = '';
    for (let i = 0; i < line.length; i++) {
      const ch = line[i];
      if (ch === '"') {
        if (inQuotes && line[i + 1] === '"') { current += '"'; i++; }
        else inQuotes = !inQuotes;
      } else if (ch === delimiter && !inQuotes) {
        result.push(current.trim());
        current = '';
      } else {
        current += ch;
      }
    }
    result.push(current.trim());
    return result;
  };

  /**
   * Parse numbers tolerantly across locales:
   * 1.000.000,50  (DE/FR – period thousands, comma decimal)
   * 1,000,000.50  (US    – comma thousands, period decimal)
   * 1000,50       (EU    – comma decimal, no thousands sep)
   * 1000.50       (ISO   – period decimal, no thousands sep)
   * Also strips currency symbols (€ $ £ ¥) and whitespace.
   */
  const parseLocalizedNumber = (raw: string): number => {
    const s = raw.replace(/[€$£¥₹\s]/g, '');
    if (s === '') return NaN;

    const dotCount   = (s.match(/\./g) ?? []).length;
    const commaCount = (s.match(/,/g) ?? []).length;

    if (dotCount === 0 && commaCount === 0) return parseFloat(s);

    // Only dots present
    if (commaCount === 0) {
      // Multiple dots → thousands separator  e.g. 1.000.000
      if (dotCount > 1) return parseFloat(s.replace(/\./g, ''));
      // Single dot → decimal separator e.g. 1000.50
      return parseFloat(s);
    }

    // Only commas present
    if (dotCount === 0) {
      // Multiple commas → thousands separator e.g. 1,000,000
      if (commaCount > 1) return parseFloat(s.replace(/,/g, ''));
      // Single comma: 3 digits after → thousands (1,000), otherwise decimal (1000,50)
      const afterComma = s.split(',')[1] ?? '';
      if (/^\d{3}$/.test(afterComma)) return parseFloat(s.replace(/,/g, ''));
      return parseFloat(s.replace(',', '.'));
    }

    // Both present – whichever appears last is the decimal separator
    const lastDot   = s.lastIndexOf('.');
    const lastComma = s.lastIndexOf(',');
    if (lastDot > lastComma) {
      // e.g. 1,000.50  → comma = thousands
      return parseFloat(s.replace(/,/g, ''));
    } else {
      // e.g. 1.000,50  → dot = thousands, comma = decimal
      return parseFloat(s.replace(/\./g, '').replace(',', '.'));
    }
  };

  /**
   * Parse dates across common regional formats:
   * YYYY-MM-DD / YYYY/MM/DD  (ISO)
   * DD/MM/YYYY, DD.MM.YYYY, DD-MM-YYYY  (European/ISO-like)
   * MM/DD/YYYY  (US – only used when day unambiguously > 12 in month position)
   * Falls back to native Date.parse for locale strings (e.g. "15 Jan 2024").
   */
  const parseLocalizedDate = (raw: string): Date | null => {
    const s = raw.trim();
    if (!s) return null;

    // ISO  YYYY-MM-DD or YYYY/MM/DD
    const iso = s.match(/^(\d{4})[-\/](\d{1,2})[-\/](\d{1,2})$/);
    if (iso) {
      const d = new Date(+iso[1], +iso[2] - 1, +iso[3]);
      if (!isNaN(d.getTime()) && d.getDate() === +iso[3]) return d;
    }

    // D(D)/M(M)/YYYY or D(D).M(M).YYYY  — European first, US fallback
    const dmy = s.match(/^(\d{1,2})[\/.\-](\d{1,2})[\/.\-](\d{4})$/);
    if (dmy) {
      const [, p1, p2, yearStr] = dmy;
      const year = +yearStr;
      // If first part > 12, it must be the day (DD/MM/YYYY)
      if (+p1 > 12) {
        const d = new Date(year, +p2 - 1, +p1);
        if (!isNaN(d.getTime()) && d.getDate() === +p1) return d;
      }
      // If second part > 12, it must be the day → MM/DD/YYYY (US)
      if (+p2 > 12) {
        const d = new Date(year, +p1 - 1, +p2);
        if (!isNaN(d.getTime()) && d.getDate() === +p2) return d;
      }
      // Ambiguous – prefer DD/MM/YYYY (European default)
      const ddmm = new Date(year, +p2 - 1, +p1);
      if (!isNaN(ddmm.getTime())) return ddmm;
    }

    // M(M)/D(D)/YYYY  US short form (already covered above, redundant safety net)
    const mdy = s.match(/^(\d{1,2})\/(\d{1,2})\/(\d{4})$/);
    if (mdy) {
      const d = new Date(+mdy[3], +mdy[1] - 1, +mdy[2]);
      if (!isNaN(d.getTime()) && d.getDate() === +mdy[2]) return d;
    }

    // Native fallback (handles "15 Jan 2024", RFC strings, etc.)
    const native = Date.parse(s);
    if (!isNaN(native)) return new Date(native);

    return null;
  };

  const validatePortfolioCsv = (content: string, _fileName: string): string[] => {
    const errors: string[] = [];
    const lines = content.split(/\r?\n/).filter(l => l.trim().length > 0);

    if (lines.length === 0) {
      errors.push('File is empty.');
      return errors;
    }

    // Auto-detect delimiter
    const delimiter = detectDelimiter(lines[0]);
    const delimLabel = delimiter === ';' ? 'semicolon (;)' : delimiter === '\t' ? 'tab' : 'comma (,)';

    const headerCols = splitCsvLine(lines[0], delimiter).map(h => h.replace(/^"|"$/g, ''));

    // Check for required columns (case-insensitive)
    const lowerCols = headerCols.map(c => c.toLowerCase());
    const missingCols = REQUIRED_PORTFOLIO_COLUMNS.filter(
      req => !lowerCols.includes(req.toLowerCase())
    );
    if (missingCols.length > 0) {
      errors.push(`Missing required column(s): ${missingCols.join(', ')}`);
    }

    if (lines.length < 2) {
      errors.push('File has a header but no data rows.');
      return errors;
    }

    // Report detected delimiter for transparency
    errors.push(`ℹ️ Detected delimiter: ${delimLabel}`);

    // Only validate first 10 data rows to keep messages concise
    const sampleRows = lines.slice(1, 11);
    const iId   = lowerCols.indexOf('contractid');
    const iIed  = lowerCols.indexOf('initialexchangedate');
    const iMat  = lowerCols.indexOf('maturitydate');
    const iNp   = lowerCols.indexOf('notionalprincipal');
    const iRate = lowerCols.indexOf('nominalinterestrate');

    sampleRows.forEach((line, idx) => {
      const rowNum = idx + 2;
      const vals = splitCsvLine(line, delimiter).map(v => v.replace(/^"|"$/g, ''));

      if (iId >= 0 && (!vals[iId] || vals[iId].trim() === '')) {
        errors.push(`Row ${rowNum}: ContractId is empty.`);
      }

      if (iIed >= 0) {
        const d = parseLocalizedDate(vals[iIed]);
        if (!d) errors.push(`Row ${rowNum}: InitialExchangeDate '${vals[iIed]}' is not a recognised date (try YYYY-MM-DD, DD/MM/YYYY or DD.MM.YYYY).`);
      }

      if (iMat >= 0) {
        const d = parseLocalizedDate(vals[iMat]);
        if (!d) errors.push(`Row ${rowNum}: MaturityDate '${vals[iMat]}' is not a recognised date (try YYYY-MM-DD, DD/MM/YYYY or DD.MM.YYYY).`);
      }

      if (iNp >= 0) {
        const n = parseLocalizedNumber(vals[iNp]);
        if (isNaN(n) || n <= 0) errors.push(`Row ${rowNum}: NotionalPrincipal '${vals[iNp]}' must be a positive number (both 1000.50 and 1.000,50 are accepted).`);
      }

      if (iRate >= 0) {
        const r = parseLocalizedNumber(vals[iRate]);
        if (isNaN(r)) errors.push(`Row ${rowNum}: NominalInterestRate '${vals[iRate]}' is not a valid number (both 0.05 and 0,05 are accepted).`);
      }
    });

    // Remove the info line if it's the only entry (file is valid)
    const infoIdx = errors.findIndex(e => e.startsWith('ℹ️'));
    const hasRealErrors = errors.some(e => !e.startsWith('ℹ️'));
    if (!hasRealErrors && infoIdx >= 0) errors.splice(infoIdx, 1);

    if (lines.length - 1 > 10 && hasRealErrors) {
      errors.push(`(Only first 10 rows checked — fix these issues and re-upload.)`);
    }

    return errors;
  };

  // Poll run status when we have an active run
  const { data: runStatus } = useSWR<RunStatus>(
    activeRunId ? API_ROUTES.runStatus(activeRunId) : null,
    fetcher,
    { 
      refreshInterval: 1000,
      revalidateOnFocus: false
    }
  );

  // Get results when run is completed
  const { data: runResult } = useSWR<RunResult>(
    activeRunId && runStatus?.state === "Completed" ? API_ROUTES.runResult(activeRunId) : null,
    fetcher,
    { revalidateOnFocus: false }
  );

  // Stop polling when run is complete
  if (runStatus?.state === "Completed" || runStatus?.state === "Failed") {
    if (isRunning) {
      setIsRunning(false);
    }
  }

  const handleFileUpload = async (file: File, type: 'portfolio' | 'metadata') => {
    // For portfolio CSV, validate before accepting
    if (type === 'portfolio') {
      const content = await readFileAsText(file);
      const errors = validatePortfolioCsv(content, file.name);
      if (errors.length > 0) {
        setCsvErrorDialog({ fileName: file.name, errors });
        return; // reject the file — do NOT set it
      }
    }

    // Simulate upload progress
    setUploadProgress(prev => ({ ...prev, [type]: 0 }));
    
    for (let i = 0; i <= 100; i += 10) {
      await new Promise(resolve => setTimeout(resolve, 50));
      setUploadProgress(prev => ({ ...prev, [type]: i }));
    }

    if (type === 'portfolio') {
      setPortfolioFile(file);
    } else {
      setMetadataFile(file);
    }

    setTimeout(() => {
      setUploadProgress(prev => ({ ...prev, [type]: undefined }));
    }, 1000);
  };

  const handleStartRun = async () => {
    if (!isApiConnected) {
      alert("Cannot start run: API is not connected");
      return;
    }
    
    try {
      setIsRunning(true);
      setActiveRunId(null);

      let requestBody = { ...parameters };
      
      // Only read and send file contents if in 'files' mode
      if (mode === 'files') {
        if (portfolioFile) {
          const portfolioCsv = await readFileAsText(portfolioFile);
          requestBody.portfolioCsv = portfolioCsv;
        }
        
        if (metadataFile) {
          const metadataCsv = await readFileAsText(metadataFile);
          requestBody.metadataCsv = metadataCsv;
        }
        
        // Update description for file mode
        requestBody.description = `${parameters.description} - Using ${portfolioFile ? 'custom portfolio' : 'default portfolio'} & ${metadataFile ? 'custom metadata' : 'default metadata'}`;
      } else {
        // Parameters mode - ensure no file data is sent
        requestBody.description = `${parameters.description} - Synthetic generation`;
      }

      const response = await fetch(API_ROUTES.pamMonteCarlo(), {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
        },
        body: JSON.stringify(requestBody),
      });

      if (!response.ok) {
        throw new Error(`HTTP ${response.status}: ${response.statusText}`);
      }

      const result = await response.json();
      
      if (result.success && result.data) {
        const runResponse: PamMonteCarloResponse = result.data;
        setActiveRunId(runResponse.runId);
      } else {
        throw new Error(result.error || "Failed to start run");
      }
    } catch (error) {
      console.error("Error starting PAM Monte Carlo run:", error);
      setIsRunning(false);
      setIsApiConnected(false);
      alert("Failed to start run: " + (error instanceof Error ? error.message : "Unknown error"));
    }
  };

  const getStatusIcon = () => {
    if (!runStatus) return <Play className="w-5 h-5" />;
    
    switch (runStatus.state) {
      case "Queued":
      case "Running":
        return <Loader2 className="w-5 h-5 animate-spin" />;
      case "Completed":
        return <CheckCircle className="w-5 h-5 text-green-500" />;
      case "Failed":
        return <AlertCircle className="w-5 h-5 text-red-500" />;
      default:
        return <Play className="w-5 h-5" />;
    }
  };

  const getStatusText = () => {
    if (!runStatus) return "Ready to run";
    
    switch (runStatus.state) {
      case "Queued":
        return "Queued for execution...";
      case "Running":
        return `Running... ${runStatus.progress0To100}%`;
      case "Completed":
        return "Completed successfully";
      case "Failed":
        return `Failed: ${runStatus.message || "Unknown error"}`;
      default:
        return "Unknown state";
    }
  };

  const getProgressPercentage = () => {
    if (!runStatus) return 0;
    
    // Show 100% when completed, regardless of last reported progress
    if (runStatus.state === "Completed") return 100;
    
    return runStatus.progress0To100 || 0;
  };

  const formatNumber = (num: number) => {
    return num.toLocaleString();
  };

  const formatCurrency = (amount: number) => {
    return new Intl.NumberFormat("en-US", {
      style: "currency",
      currency: "USD",
      minimumFractionDigits: 0,
      maximumFractionDigits: 0,
    }).format(amount);
  };

  const FileUploadZone = ({ 
    type, 
    file, 
    onFileSelect, 
    accept = ".csv" 
  }: { 
    type: 'portfolio' | 'metadata';
    file: File | null;
    onFileSelect: (file: File) => void;
    accept?: string;
  }) => {
    const progress = uploadProgress[type];

    return (
      <div className="border-2 border-dashed border-gray-300 rounded-lg p-6 hover:border-gray-400 transition-colors">
        <div className="text-center">
          <Upload className="mx-auto h-12 w-12 text-gray-400" />
          <div className="mt-2">
            <h3 className="text-sm font-medium text-gray-900 capitalize">
              {type} Data
            </h3>
            {file ? (
              <div className="mt-2">
                <div className="flex items-center justify-center gap-2 text-sm text-green-600">
                  <FileText className="w-4 h-4" />
                  {file.name}
                </div>
                {progress !== undefined ? (
                  <div className="mt-2 w-full bg-gray-200 rounded-full h-2">
                    <div 
                      className="bg-blue-600 h-2 rounded-full transition-all duration-300"
                      style={{ width: `${progress}%` }}
                    />
                  </div>
                ) : (
                  <button
                    onClick={() => {
                      if (type === 'portfolio') setPortfolioFile(null);
                      else setMetadataFile(null);
                    }}
                    className="mt-1 text-xs text-red-600 hover:text-red-800"
                  >
                    Remove
                  </button>
                )}
              </div>
            ) : (
              <div className="mt-2">
                <label className="cursor-pointer">
                  <span className="text-sm text-gray-500">
                    Upload {type}.csv or use defaults
                  </span>
                  <input
                    type="file"
                    className="hidden"
                    accept={accept}
                    onChange={(e) => {
                      const selectedFile = e.target.files?.[0];
                      if (selectedFile) {
                        onFileSelect(selectedFile);
                        handleFileUpload(selectedFile, type);
                      }
                    }}
                  />
                </label>
              </div>
            )}
          </div>
        </div>
      </div>
    );
  };

  return (
    <div className="flex flex-col min-h-screen">
      <Header title="PAM Monte Carlo" />

      {/* CSV Validation Error Dialog */}
      {csvErrorDialog && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40">
          <div className="bg-white rounded-xl shadow-2xl border border-red-200 max-w-lg w-full mx-4 overflow-hidden">
            <div className="flex items-center justify-between px-6 py-4 bg-red-50 border-b border-red-200">
              <div className="flex items-center gap-2">
                <AlertCircle className="w-5 h-5 text-red-600" />
                <h3 className="text-base font-semibold text-red-800">Invalid Portfolio CSV</h3>
              </div>
              <button
                onClick={() => setCsvErrorDialog(null)}
                className="text-gray-400 hover:text-gray-600 transition-colors"
              >
                <X className="w-5 h-5" />
              </button>
            </div>
            <div className="px-6 py-4">
              <p className="text-sm text-gray-600 mb-3">
                <span className="font-medium">{csvErrorDialog.fileName}</span> could not be accepted because of the following errors:
              </p>
              <ul className="space-y-1.5 max-h-64 overflow-y-auto">
                {csvErrorDialog.errors.map((err, i) =>
                  err.startsWith('ℹ️') ? (
                    <li key={i} className="flex items-start gap-2 text-sm text-blue-600 bg-blue-50 rounded px-2 py-1">
                      <span>{err}</span>
                    </li>
                  ) : (
                    <li key={i} className="flex items-start gap-2 text-sm text-red-700">
                      <span className="mt-0.5 shrink-0">•</span>
                      <span>{err}</span>
                    </li>
                  )
                )}
              </ul>
              <p className="mt-4 text-xs text-gray-500">
                Required columns: <code className="bg-gray-100 px-1 rounded">ContractId</code>,{' '}
                <code className="bg-gray-100 px-1 rounded">InitialExchangeDate</code>,{' '}
                <code className="bg-gray-100 px-1 rounded">MaturityDate</code>,{' '}
                <code className="bg-gray-100 px-1 rounded">NotionalPrincipal</code>,{' '}
                <code className="bg-gray-100 px-1 rounded">NominalInterestRate</code>
              </p>
            </div>
            <div className="px-6 py-3 bg-gray-50 border-t border-gray-200 flex justify-end">
              <button
                onClick={() => setCsvErrorDialog(null)}
                className="px-4 py-2 text-sm font-medium text-white bg-red-600 hover:bg-red-700 rounded-lg transition-colors"
              >
                Fix & Re-upload
              </button>
            </div>
          </div>
        </div>
      )}
      <main className="flex-1 p-6 space-y-6">
        <div className="max-w-6xl mx-auto space-y-6">
            {/* Page Header */}
            <div>
              <h1 className="text-3xl font-bold text-gray-900">PAM Monte Carlo</h1>
              <p className="mt-2 text-gray-600">
                Principal-at-Maturity portfolio risk analysis with Monte Carlo simulation
              </p>
            </div>

            {/* Mode Selection */}
            <div className="bg-white rounded-xl shadow-lg border border-gray-200 p-6">
              <h2 className="text-xl font-semibold text-gray-900 mb-4">Data Source Mode</h2>
              <div className="flex gap-4">
                <button
                  onClick={() => setMode('parameters')}
                  className={`flex-1 p-4 rounded-lg border-2 transition-all ${
                    mode === 'parameters'
                      ? 'border-blue-500 bg-blue-50 text-blue-700'
                      : 'border-gray-300 bg-white text-gray-700 hover:border-gray-400'
                  }`}
                >
                  <div className="text-center">
                    <div className="text-lg font-medium">Parameter Mode</div>
                    <div className="text-sm mt-1 opacity-75">
                      Generate synthetic portfolio using configuration parameters
                    </div>
                  </div>
                </button>
                <button
                  onClick={() => setMode('files')}
                  className={`flex-1 p-4 rounded-lg border-2 transition-all ${
                    mode === 'files'
                      ? 'border-blue-500 bg-blue-50 text-blue-700'
                      : 'border-gray-300 bg-white text-gray-700 hover:border-gray-400'
                  }`}
                >
                  <div className="text-center">
                    <div className="text-lg font-medium">File Upload Mode</div>
                    <div className="text-sm mt-1 opacity-75">
                      Upload portfolio and metadata CSV files
                    </div>
                  </div>
                </button>
              </div>
            </div>

            {/* File Upload Section - Only show in 'files' mode */}
            {mode === 'files' && (
              <div className="bg-white rounded-xl shadow-lg border border-gray-200 p-6">
                <h2 className="text-xl font-semibold text-gray-900 mb-4">Data Sources</h2>
                <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
                  <FileUploadZone
                    type="portfolio"
                    file={portfolioFile}
                    onFileSelect={(file) => handleFileUpload(file, 'portfolio')}
                  />
                  <FileUploadZone
                    type="metadata"
                    file={metadataFile}
                    onFileSelect={(file) => handleFileUpload(file, 'metadata')}
                  />
                </div>
                <div className="mt-4 text-sm text-gray-500">
                  <p>
                    <strong>Portfolio CSV:</strong> Should contain ContractId, InitialExchangeDate, MaturityDate, NotionalPrincipal, NominalInterestRate, etc.
                  </p>
                  <p className="mt-1">
                    <strong>Metadata CSV:</strong> Should contain ContractId, Segment, Region, ProductLine, Currency, Broker, etc.
                  </p>
                </div>
              </div>
            )}

            {/* Configuration & Run Section */}
            <div className="bg-white rounded-xl shadow-lg border border-gray-200 p-6">
              <div className="flex items-center justify-between mb-6">
                <div>
                  <h2 className="text-xl font-semibold text-gray-900">Simulation Configuration</h2>
                  <div className="flex items-center gap-2 mt-2">
                    <div className={`w-2 h-2 rounded-full ${isApiConnected ? 'bg-green-500' : 'bg-red-500'}`} />
                    <span className="text-sm text-gray-600">
                      API {isApiConnected ? 'Connected' : 'Disconnected'}
                    </span>
                  </div>
                </div>
                <button
                  onClick={() => setShowSettings(!showSettings)}
                  className="p-2 text-gray-400 hover:text-gray-600 transition-colors"
                >
                  <Settings className="w-5 h-5" />
                </button>
              </div>

              {/* Settings Panel */}
              {showSettings && (
                <div className="bg-gray-50 rounded-lg p-4 mb-6 space-y-4">
                  <h4 className="font-medium text-gray-900 mb-3">
                    {mode === 'parameters' ? 'Generation Parameters' : 'Simulation Parameters'}
                  </h4>
                  <div className="grid grid-cols-2 lg:grid-cols-3 gap-4">
                    {/* Portfolio size - only for parameter mode */}
                    {mode === 'parameters' && (
                      <div>
                        <label className="block text-sm font-medium text-gray-700 mb-1">
                          Contracts
                        </label>
                        <input
                          type="number"
                          value={parameters.numContracts}
                          onChange={(e) => 
                            setParameters(p => ({ ...p, numContracts: parseInt(e.target.value) || 0 }))
                          }
                          className="w-full px-3 py-2 border border-gray-300 rounded-md text-sm focus:outline-none focus:ring-2 focus:ring-blue-500/20"
                          min="1"
                          max="100000"
                        />
                      </div>
                    )}
                    
                    {/* Monte Carlo scenarios - for both modes */}
                    <div>
                      <label className="block text-sm font-medium text-gray-700 mb-1">
                        Scenarios
                      </label>
                      <input
                        type="number"
                        value={parameters.numScenarios}
                        onChange={(e) => 
                          setParameters(p => ({ ...p, numScenarios: parseInt(e.target.value) || 0 }))
                        }
                        className="w-full px-3 py-2 border border-gray-300 rounded-md text-sm focus:outline-none focus:ring-2 focus:ring-blue-500/20"
                        min="10"
                        max="10000"
                      />
                    </div>
                    
                    {/* Maturity - only for parameter mode */}
                    {mode === 'parameters' && (
                      <div>
                        <label className="block text-sm font-medium text-gray-700 mb-1">
                          Maturity (months)
                        </label>
                        <input
                          type="number"
                          value={parameters.monthsToMaturity}
                          onChange={(e) => 
                            setParameters(p => ({ ...p, monthsToMaturity: parseInt(e.target.value) || 0 }))
                          }
                          className="w-full px-3 py-2 border border-gray-300 rounded-md text-sm focus:outline-none focus:ring-2 focus:ring-blue-500/20"
                          min="1"
                          max="1200"
                        />
                      </div>
                    )}
                    
                    {/* Random seed - for both modes */}
                    {/* Random seed - for both modes */}
                    <div>
                      <label className="block text-sm font-medium text-gray-700 mb-1">
                        Random Seed
                      </label>
                      <input
                        type="number"
                        value={parameters.seed}
                        onChange={(e) => 
                          setParameters(p => ({ ...p, seed: parseInt(e.target.value) || 0 }))
                        }
                        className="w-full px-3 py-2 border border-gray-300 rounded-md text-sm focus:outline-none focus:ring-2 focus:ring-blue-500/20"
                      />
                    </div>
                    
                    {/* Engine - for both modes */}
                    <div>
                      <label className="block text-sm font-medium text-gray-700 mb-1">
                        Engine  
                      </label>
                      <select
                        value={parameters.preferGpu ? 'GPU' : 'CPU'}
                        onChange={(e) => 
                          setParameters(p => ({ ...p, preferGpu: e.target.value === 'GPU' }))
                        }
                        className="w-full px-3 py-2 border border-gray-300 rounded-md text-sm focus:outline-none focus:ring-2 focus:ring-blue-500/20"
                      >
                        <option value="CPU">CPU</option>
                        <option value="GPU">GPU</option>
                      </select>
                    </div>
                  </div>
                  
                  {/* Mode-specific help text */}
                  <div className="text-sm text-gray-600 mt-3">
                    {mode === 'parameters' 
                      ? 'Portfolio contracts and maturity will be generated synthetically using the parameters above.'
                      : 'Portfolio size and maturity will be derived from your uploaded CSV files.'}
                  </div>
                </div>
              )}

              {/* Run Controls */}
              <div className="flex items-center justify-between mb-6">
                <div className="flex items-center gap-3">
                  {getStatusIcon()}
                  <div>
                    <p className="text-sm font-medium text-gray-900">{getStatusText()}</p>
                    {(runStatus?.progress0To100 !== undefined || runStatus?.state === "Completed") && (
                      <div className="w-48 bg-gray-200 rounded-full h-2 mt-1">
                        <div
                          className="bg-gradient-to-r from-blue-500 to-purple-600 h-2 rounded-full transition-all duration-300"
                          style={{ width: `${getProgressPercentage()}%` }}
                        />
                      </div>
                    )}
                  </div>
                </div>
                <button
                  onClick={handleStartRun}
                  disabled={isRunning || !isApiConnected}
                  className="flex items-center gap-2 px-6 py-3 bg-gradient-to-r from-blue-500 to-purple-600 text-white rounded-lg font-medium hover:from-blue-600 hover:to-purple-700 transition-all disabled:opacity-50 disabled:cursor-not-allowed"
                >
                  {isRunning ? (
                    <>
                      <Loader2 className="w-5 h-5 animate-spin" />
                      Running...
                    </>
                  ) : (
                    <>
                      <Play className="w-5 h-5" />
                      Start Simulation
                    </>
                  )}
                </button>
              </div>
            </div>

            {/* Results Section */}
            {runResult && (
              <div className="bg-white rounded-xl shadow-lg border border-gray-200 p-6">
                <div className="flex items-center gap-2 mb-6">
                  <BarChart3 className="w-6 h-6 text-blue-600" />
                  <h2 className="text-xl font-semibold text-gray-900">Results</h2>
                  <span className="text-sm text-gray-500">
                    ({((runResult.result?.DurationMs || 0) / 1000).toFixed(1)}s runtime)
                  </span>
                </div>

                <div className="grid grid-cols-2 lg:grid-cols-4 gap-6 mb-6">
                  <div className="text-center p-4 bg-gray-50 rounded-lg">
                    <p className="text-xs text-gray-500 uppercase tracking-wide mb-2">Mean PV</p>
                    <p className="text-2xl font-bold text-gray-900">
                      {formatCurrency(runResult.result?.MeanPv || 0)}
                    </p>
                  </div>
                  <div className="text-center p-4 bg-gray-50 rounded-lg">
                    <p className="text-xs text-gray-500 uppercase tracking-wide mb-2">Std Dev</p>
                    <p className="text-2xl font-bold text-gray-900">
                      {formatCurrency(runResult.result?.StdPv || 0)}
                    </p>
                  </div>
                  <div className="text-center p-4 bg-red-50 rounded-lg">
                    <p className="text-xs text-red-600 uppercase tracking-wide mb-2">5% VaR</p>
                    <p className="text-2xl font-bold text-red-600">
                      {formatCurrency(runResult.result?.P05 || 0)}
                    </p>
                  </div>
                  <div className="text-center p-4 bg-green-50 rounded-lg">
                    <p className="text-xs text-green-600 uppercase tracking-wide mb-2">95% VaR</p>
                    <p className="text-2xl font-bold text-green-600">
                      {formatCurrency(runResult.result?.P95 || 0)}
                    </p>
                  </div>
                </div>

                <div className="text-center text-sm text-gray-600">
                  {mode === 'parameters' 
                    ? `${formatNumber(parameters.numContracts || 0)} contracts (generated)` 
                    : `${formatNumber(runResult.result?.Metrics?.numContracts || 0)} contracts (from ${portfolioFile?.name || 'uploaded file'})`
                  } × {formatNumber(parameters.numScenarios || 0)} scenarios 
                  · Engine: {runResult.result?.EngineLabel || runResult.engine || 'Unknown'}
                  · Mode: {mode === 'parameters' ? 'Synthetic Generation' : 'File Upload'}
                  {mode === 'files' && !portfolioFile && (
                    <span> · Using default data</span>
                  )}
                </div>
              </div>
            )}
          </div>
        </main>
      </div>
    );
  }