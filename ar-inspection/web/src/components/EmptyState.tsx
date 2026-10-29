interface EmptyStateProps {
  onReload: () => void;
  loading: boolean;
}

export default function EmptyState({ onReload, loading }: EmptyStateProps) {
  return (
    <div className="empty-state" role="status">
      <div className="empty-state__icon" aria-hidden="true">
        🗂️
      </div>
      <h2 className="empty-state__title">暂无问题记录</h2>
      <p className="empty-state__desc">
        后端当前返回空列表。用 Unity AR 端在真实平面放置标记并上报后，记录会出现在这里。
      </p>
      <button type="button" className="btn btn--primary" onClick={onReload} disabled={loading}>
        {loading ? '重新加载中…' : '重新加载'}
      </button>
    </div>
  );
}
