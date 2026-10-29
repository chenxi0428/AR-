import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import App from './App';
import ErrorBoundary from './components/ErrorBoundary';
import { installGlobalErrorGuard } from './globalError';
import './styles.css';

installGlobalErrorGuard();

const container = document.getElementById('root');

if (container) {
  createRoot(container).render(
    <StrictMode>
      <ErrorBoundary>
        <App />
      </ErrorBoundary>
    </StrictMode>,
  );
} else {
  // 极端兜底：index.html 结构被破坏时也不要白屏。
  const fallback = document.createElement('div');
  fallback.className = 'fatal-screen';
  fallback.setAttribute('role', 'alert');
  fallback.textContent = '页面初始化失败：index.html 中缺少 #root 容器。';
  document.body.appendChild(fallback);
}
