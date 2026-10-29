import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { API_BASE, errorMessage, fetchIssues, patchIssueStatus } from './api';
import type { Issue, IssueStatus } from './types';
import { statusLabel } from './types';
import { formatDateTime } from './utils/format';
import EmptyState from './components/EmptyState';
import ErrorBanner from './components/ErrorBanner';
import IssueCard from './components/IssueCard';
import LoadingState from './components/LoadingState';

interface ActionError {
  id: string;
  message: string;
}

type LoadMode = 'initial' | 'refresh';

export default function App() {
  const [issues, setIssues] = useState<Issue[]>([]);
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [actionError, setActionError] = useState<ActionError | null>(null);
  const [pendingIds, setPendingIds] = useState<string[]>([]);
  const [lastLoadedAt, setLastLoadedAt] = useState<Date | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const requestSeq = useRef(0);

  /** 拉取列表：只有最后一次请求的结果会落盘，避免快速连点刷新导致的竞态。 */
  const load = useCallback(async (mode: LoadMode) => {
    const seq = requestSeq.current + 1;
    requestSeq.current = seq;
    if (mode === 'initial') setLoading(true);
    else setRefreshing(true);
    setLoadError(null);
    setActionError(null);
    try {
      const items = await fetchIssues();
      if (seq !== requestSeq.current) return;
      setIssues(items);
      setLastLoadedAt(new Date());
    } catch (error) {
      if (seq !== requestSeq.current) return;
      setLoadError(errorMessage(error));
    } finally {
      if (seq === requestSeq.current) {
        setLoading(false);
        setRefreshing(false);
      }
    }
  }, []);

  useEffect(() => {
    void load('initial');
  }, [load]);

  useEffect(() => {
    if (!notice) return;
    const timer = window.setTimeout(() => setNotice(null), 3200);
    return () => window.clearTimeout(timer);
  }, [notice]);

  const handleChangeStatus = useCallback(async (issue: Issue, next: IssueStatus) => {
    if (next === issue.status) return;
    setActionError(null);
    setPendingIds((prev) => (prev.includes(issue.id) ? prev : [...prev, issue.id]));
    try {
      // 不做乐观更新：只有后端确认成功后才替换该行，失败时保留原数据。
      const updated = await patchIssueStatus(issue.id, next);
      setIssues((prev) => prev.map((item) => (item.id === issue.id ? updated : item)));
      setNotice(`已将「${issue.title}」的状态改为「${statusLabel(updated.status)}」`);
    } catch (error) {
      setActionError({
        id: issue.id,
        message: `修改「${issue.title}」状态失败：${errorMessage(error)}`,
      });
    } finally {
      setPendingIds((prev) => prev.filter((id) => id !== issue.id));
    }
  }, []);

  const handleRefresh = useCallback(() => {
    void load('refresh');
  }, [load]);

  const dismissActionError = useCallback(() => setActionError(null), []);

  const stats = useMemo(() => {
    const counts = { open: 0, in_progress: 0, resolved: 0 };
    for (const issue of issues) {
      if (issue.status === 'open') counts.open += 1;
      else if (issue.status === 'in_progress') counts.in_progress += 1;
      else if (issue.status === 'resolved') counts.resolved += 1;
    }
    return { total: issues.length, ...counts };
  }, [issues]);

  const showSkeleton = loading || (refreshing && issues.length === 0);

  return (
    <div className="app">
      <header className="app-header">
        <div className="app-header__brand">
          <div className="app-header__logo" aria-hidden="true">
            AR
          </div>
          <div>
            <h1 className="app-header__title">AR 巡检 · 问题管理端</h1>
            <p className="app-header__subtitle">
              Unity AR 上报 → Go 后端存储 → 在此查看并更新处理状态
            </p>
          </div>
        </div>
        <div className="app-header__side">
          <div className="api-chip" title="来自环境变量 VITE_API_BASE">
            <span className="api-chip__dot" aria-hidden="true" />
            <span>
              后端地址：<code>{API_BASE}</code>
            </span>
          </div>
          <button
            type="button"
            className="btn btn--primary"
            onClick={handleRefresh}
            disabled={loading || refreshing}
          >
            {refreshing ? '刷新中…' : '刷新列表'}
          </button>
        </div>
      </header>

      <main className="app-main">
        <section className="toolbar" aria-label="问题概览">
          <div className="stat-list">
            <div className="stat">
              <span className="stat__value">{stats.total}</span>
              <span className="stat__label">全部问题</span>
            </div>
            <div className="stat stat--open">
              <span className="stat__value">{stats.open}</span>
              <span className="stat__label">待处理</span>
            </div>
            <div className="stat stat--progress">
              <span className="stat__value">{stats.in_progress}</span>
              <span className="stat__label">处理中</span>
            </div>
            <div className="stat stat--resolved">
              <span className="stat__value">{stats.resolved}</span>
              <span className="stat__label">已解决</span>
            </div>
          </div>
          <p className="toolbar__meta">
            最近刷新：{lastLoadedAt ? formatDateTime(lastLoadedAt.toISOString()) : '—'}
          </p>
        </section>

        {loadError ? (
          <ErrorBanner
            title="加载问题列表失败"
            message={loadError}
            hint={`后端地址：${API_BASE}（可用环境变量 VITE_API_BASE 覆盖）`}
            retrying={refreshing}
            onRetry={handleRefresh}
          />
        ) : null}

        {actionError ? (
          <ErrorBanner
            tone="warning"
            title="状态修改失败"
            message={actionError.message}
            hint="本次修改未生效，列表中该问题的状态仍为修改前的值。"
            onDismiss={dismissActionError}
          />
        ) : null}

        {showSkeleton ? (
          <LoadingState />
        ) : issues.length === 0 ? (
          loadError ? null : <EmptyState onReload={handleRefresh} loading={refreshing} />
        ) : (
          <section className="issue-list" aria-label="问题列表">
            {issues.map((issue) => (
              <IssueCard
                key={issue.id}
                issue={issue}
                pending={pendingIds.includes(issue.id)}
                hasError={actionError?.id === issue.id}
                onChangeStatus={handleChangeStatus}
              />
            ))}
          </section>
        )}
      </main>

      <footer className="app-footer">
        接口契约见 <code>ar-inspection/docs/API.md</code> · 单次请求 10 秒超时 · 状态保存失败时保留原值
      </footer>

      {notice ? (
        <div className="toast" role="status" aria-live="polite">
          ✓ {notice}
        </div>
      ) : null}
    </div>
  );
}
