/**
 * 全局兜底：捕获 React 渲染之外的未捕获异常 / 未处理的 Promise rejection，
 * 在页面底部展示醒目提示，而不是让用户看到白屏或只在控制台报错。
 */

let installed = false;

function showFatalBanner(title: string, detail: string): void {
  if (document.getElementById('global-fatal-banner')) return;

  const banner = document.createElement('div');
  banner.id = 'global-fatal-banner';
  banner.className = 'global-fatal';
  banner.setAttribute('role', 'alert');

  const head = document.createElement('p');
  head.className = 'global-fatal__title';
  head.textContent = `⚠ ${title}`;

  const body = document.createElement('p');
  body.className = 'global-fatal__detail';
  body.textContent = detail;

  const actions = document.createElement('div');
  actions.className = 'global-fatal__actions';

  const reload = document.createElement('button');
  reload.type = 'button';
  reload.className = 'btn btn--primary btn--sm';
  reload.textContent = '重新加载页面';
  reload.addEventListener('click', () => window.location.reload());

  const close = document.createElement('button');
  close.type = 'button';
  close.className = 'btn btn--ghost btn--sm';
  close.textContent = '关闭提示';
  close.addEventListener('click', () => banner.remove());

  actions.append(reload, close);
  banner.append(head, body, actions);
  document.body.appendChild(banner);
}

export function installGlobalErrorGuard(): void {
  if (installed) return;
  installed = true;

  window.addEventListener('error', (event: ErrorEvent) => {
    const message = event.error instanceof Error ? event.error.message : event.message;
    // 忽略 ResizeObserver 的良性告警，避免兜底条误报。
    if (message && message.includes('ResizeObserver loop')) return;
    console.error('[global-error]', event.error ?? event.message);
    showFatalBanner('页面发生未捕获异常', message || '未知错误（详见浏览器控制台）');
  });

  window.addEventListener('unhandledrejection', (event: PromiseRejectionEvent) => {
    console.error('[unhandled-rejection]', event.reason);
    const reason = event.reason;
    const detail =
      reason instanceof Error ? `${reason.name}: ${reason.message}` : String(reason ?? '未知原因');
    showFatalBanner('页面发生未处理的异步错误', detail);
  });
}
