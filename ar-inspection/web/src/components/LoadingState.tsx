export default function LoadingState() {
  return (
    <div className="loading" role="status" aria-live="polite">
      <div className="loading__text">
        <span className="spinner" aria-hidden="true" />
        正在从后端加载问题列表…
      </div>
      <div className="skeleton-list" aria-hidden="true">
        {[0, 1, 2].map((index) => (
          <div className="skeleton-card" key={index}>
            <div className="skeleton-line skeleton-line--title" />
            <div className="skeleton-line" />
            <div className="skeleton-line skeleton-line--short" />
          </div>
        ))}
      </div>
    </div>
  );
}
