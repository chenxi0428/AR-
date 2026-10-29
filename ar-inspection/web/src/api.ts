import type { Issue, IssueStatus } from './types';

/**
 * 后端地址：优先进取 VITE_API_BASE，缺省 http://127.0.0.1:8080。
 * 接口契约见 ar-inspection/docs/API.md。
 */
const DEFAULT_API_BASE = 'http://127.0.0.1:8080';
const REQUEST_TIMEOUT_MS = 10_000;

function normalizeBase(raw: string | undefined): string {
  const value = (raw ?? '').trim();
  if (!value) return DEFAULT_API_BASE;
  return value.replace(/\/+$/, '');
}

export const API_BASE = normalizeBase(import.meta.env.VITE_API_BASE);

/** 统一错误类型：区分网络错误 / HTTP 错误 / 响应格式错误，UI 直接展示 message（中文）。 */
export class ApiError extends Error {
  readonly status: number | null;
  readonly code: string;
  readonly fields?: Record<string, string>;

  constructor(
    message: string,
    options: { status?: number | null; code?: string; fields?: Record<string, string> } = {},
  ) {
    super(message);
    this.name = 'ApiError';
    this.status = options.status ?? null;
    this.code = options.code ?? 'UNKNOWN_ERROR';
    this.fields = options.fields;
  }
}

/** 把任意异常转成可展示的一句话（绝不把 [object Object] 漏到界面上）。 */
export function errorMessage(error: unknown): string {
  if (error instanceof ApiError) return error.message;
  if (error instanceof Error) return error.message || error.name;
  return typeof error === 'string' ? error : '未知错误';
}

function asRecord(value: unknown): Record<string, unknown> | null {
  return typeof value === 'object' && value !== null ? (value as Record<string, unknown>) : null;
}

function errorFields(value: unknown): Record<string, string> | undefined {
  const record = asRecord(value);
  if (!record) return undefined;
  const fields: Record<string, string> = {};
  for (const [key, raw] of Object.entries(record)) {
    if (typeof raw === 'string') fields[key] = raw;
  }
  return Object.keys(fields).length > 0 ? fields : undefined;
}

function defaultMessageForStatus(status: number): string {
  switch (status) {
    case 400:
      return '请求参数不合法（HTTP 400）';
    case 404:
      return '问题 id 不存在或接口路径错误（HTTP 404）';
    case 405:
      return '请求方法不被后端支持（HTTP 405）';
    case 500:
      return '后端内部错误（HTTP 500），请查看后端日志';
    default:
      return `请求失败（HTTP ${status}）`;
  }
}

/** 按 docs/API.md 第 3 节解析 { code, message, fields }。 */
function parseErrorPayload(payload: unknown, status: number): ApiError {
  const record = asRecord(payload);
  const code = typeof record?.code === 'string' ? record.code : 'HTTP_ERROR';
  const rawMessage = typeof record?.message === 'string' ? record.message.trim() : '';
  return new ApiError(rawMessage || defaultMessageForStatus(status), {
    status,
    code,
    fields: errorFields(record?.fields),
  });
}

async function readBody(response: Response): Promise<unknown> {
  const text = await response.text();
  if (!text) return null;
  try {
    return JSON.parse(text) as unknown;
  } catch {
    return text;
  }
}

interface RequestOptions {
  method?: 'GET' | 'PATCH';
  body?: unknown;
}

async function request(path: string, options: RequestOptions = {}): Promise<unknown> {
  const controller = new AbortController();
  const timer = window.setTimeout(() => controller.abort(), REQUEST_TIMEOUT_MS);
  const headers: Record<string, string> = { Accept: 'application/json' };
  if (options.body !== undefined) headers['Content-Type'] = 'application/json; charset=utf-8';

  let response: Response;
  try {
    response = await fetch(`${API_BASE}${path}`, {
      method: options.method ?? 'GET',
      headers,
      body: options.body === undefined ? undefined : JSON.stringify(options.body),
      signal: controller.signal,
    });
  } catch (error) {
    if (controller.signal.aborted) {
      throw new ApiError(`请求超时（${REQUEST_TIMEOUT_MS / 1000} 秒）：${API_BASE}${path}`, {
        code: 'TIMEOUT',
      });
    }
    const detail = error instanceof Error && error.message ? `（${error.message}）` : '';
    throw new ApiError(
      `无法连接后端 ${API_BASE}${detail}。请确认 Go 服务已启动，且网关/后端允许跨域（CORS）。`,
      { code: 'NETWORK_ERROR' },
    );
  } finally {
    window.clearTimeout(timer);
  }

  const payload = await readBody(response);
  if (!response.ok) throw parseErrorPayload(payload, response.status);
  if (typeof payload === 'string') {
    throw new ApiError(
      `后端返回了非 JSON 响应（HTTP ${response.status}）：${payload.slice(0, 120)}`,
      { status: response.status, code: 'BAD_RESPONSE' },
    );
  }
  return payload;
}

/** 最小运行时校验：避免后端字段缺失时 UI 渲染崩溃。 */
function isIssue(value: unknown): value is Issue {
  const record = asRecord(value);
  return (
    record !== null &&
    typeof record.id === 'string' &&
    typeof record.title === 'string' &&
    typeof record.status === 'string'
  );
}

/** GET /api/issues → 问题数组（后端已按 createdAt 倒序）。 */
export async function fetchIssues(): Promise<Issue[]> {
  const payload = await request('/api/issues');
  const items = asRecord(payload)?.items;
  if (!Array.isArray(items)) {
    throw new ApiError('后端响应格式异常：GET /api/issues 缺少 items 数组，请核对 docs/API.md。', {
      code: 'BAD_RESPONSE',
    });
  }
  const issues = items.filter(isIssue);
  if (issues.length !== items.length) {
    throw new ApiError('后端响应格式异常：items 中存在字段不完整的问题记录。', {
      code: 'BAD_RESPONSE',
    });
  }
  return issues;
}

/** PATCH /api/issues/{id} → 更新后的完整 Issue。 */
export async function patchIssueStatus(id: string, status: IssueStatus): Promise<Issue> {
  const payload = await request(`/api/issues/${encodeURIComponent(id)}`, {
    method: 'PATCH',
    body: { status },
  });
  if (!isIssue(payload)) {
    throw new ApiError('后端响应格式异常：PATCH 未返回完整的问题数据。', { code: 'BAD_RESPONSE' });
  }
  return payload;
}
