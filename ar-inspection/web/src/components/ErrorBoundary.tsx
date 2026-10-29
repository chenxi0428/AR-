import { Component } from 'react';
import type { ErrorInfo, ReactNode } from 'react';

interface ErrorBoundaryProps {
  children: ReactNode;
}

interface ErrorBoundaryState {
  error: Error | null;
}

/** React 渲染异常兜底：任何子组件抛错都显示可重试的错误页，而不是白屏。 */
export default class ErrorBoundary extends Component<ErrorBoundaryProps, ErrorBoundaryState> {
  state: ErrorBoundaryState = { error: null };

  static getDerivedStateFromError(error: Error): ErrorBoundaryState {
    return { error };
  }

  componentDidCatch(error: Error, info: ErrorInfo): void {
    console.error('[ErrorBoundary] 页面渲染异常：', error, info.componentStack);
  }

  private handleReload = (): void => {
    this.setState({ error: null });
    window.location.reload();
  };

  render(): ReactNode {
    const { error } = this.state;
    if (!error) return this.props.children;

    return (
      <div className="fatal-screen" role="alert">
        <h1 className="fatal-screen__title">页面渲染出错（已捕获，未白屏）</h1>
        <p className="fatal-screen__desc">
          前端发生了未预期的渲染异常。可以点击下方按钮重新加载；若持续出现，请打开浏览器控制台查看
          ErrorBoundary 日志。
        </p>
        <pre className="fatal-screen__detail">
          {error.name}: {error.message}
        </pre>
        <button type="button" className="btn btn--primary" onClick={this.handleReload}>
          重新加载页面
        </button>
      </div>
    );
  }
}
