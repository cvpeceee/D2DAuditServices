import { useEffect, useState } from "react";
import { api } from "../../shared/api/client";
import type { SearchResult, SearchResponse } from "../insights/types";

export function useGlobalSearch(query: string, tenantId: string | null, enabled: boolean) {
  const [results, setResults] = useState<SearchResult[]>([]);
  const [loading, setLoading] = useState(false);

  useEffect(() => {
    const clean = query.trim();
    if (!enabled || !tenantId || clean.length < 2) {
      setResults([]);
      setLoading(false);
      return;
    }
    let cancelled = false;
    setLoading(true);
    const timer = window.setTimeout(() => {
      void api<SearchResponse>(`/api/v1/search?q=${encodeURIComponent(clean)}`, { tenantId })
        .then((value) => {
          if (!cancelled) setResults(value.results);
        })
        .catch(() => {
          if (!cancelled) setResults([]);
        })
        .finally(() => {
          if (!cancelled) setLoading(false);
        });
    }, 180);
    return () => {
      cancelled = true;
      window.clearTimeout(timer);
    };
  }, [query, tenantId, enabled]);

  return { results, loading };
}

export const searchKindIcon: Record<string, string> = {
  control: "controls",
  test: "tests",
  evidence: "evidence",
  policy: "policies",
  risk: "risks",
  vendor: "vendors",
  person: "people",
  framework: "frameworks",
};
