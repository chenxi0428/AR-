interface ErrorBannerProps {
  title: string;
  message: string;
  hint?: string;
  tone?: 'error' | 'warning';
  retrying?: boolean;
  onRetry?: () => void;
  onDismiss?: () => void;
}

/** 醒目的错误横幅：包含 message、可选重试 / 关闭按钮，页面其余部分保持可用。 */
export default function ErrorBanner({
  title,
  message,
  hint,
  tone = 'error',
  retrying = false,
  onRetry,
  onDismiss,
}: ErrorBannerProps) {
  return (
    <div className={`error-banner error-banner--${tone}`} role="alert" aria-live="assertive">
      <div className="error-banner__icon" aria-hidden="true">
        {tone === 'error' ? '⚠' : '!'}
      </div>
      <div className="error-banner__body">
        <p className="error-banner__title">{title}</p>
        <p className="error-banner__message">{message}</p>
        {hint ? <p className="error-banner__hint">{hint}</p> : null}
      </div>
      <div className="error-banner__actions">
        {onRetry ? (
          <button type="button" className="btn btn--danger-ghost" onClick={onRetry} disabled={retrying}>
            {retrying ? '重试中…' : '重试'}
          </button>
        ) : null}
        {onDismiss ? (
          <button type="button" className="btn btn--ghost" onClick={onDismiss}>
            关闭
          </button>
        ) : null}
      </div>
    </div>
  );
}
