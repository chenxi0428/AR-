/** 与 ar-inspection/docs/API.md 严格一致的数据类型。 */

export type Priority = 'low' | 'medium' | 'high';
export type IssueStatus = 'open' | 'in_progress' | 'resolved';

export interface IssuePosition {
  x: number;
  y: number;
  z: number;
}

export interface Issue {
  id: string;
  title: string;
  description: string;
  priority: Priority;
  status: IssueStatus;
  position: IssuePosition;
  createdAt: string;
  updatedAt: string;
}

/** GET /api/issues 的响应体 */
export interface IssueListResponse {
  items: Issue[];
}

export const PRIORITIES: readonly Priority[] = ['low', 'medium', 'high'];
export const STATUSES: readonly IssueStatus[] = ['open', 'in_progress', 'resolved'];

export const PRIORITY_LABEL: Record<Priority, string> = {
  low: '低',
  medium: '中',
  high: '高',
};

export const STATUS_LABEL: Record<IssueStatus, string> = {
  open: '待处理',
  in_progress: '处理中',
  resolved: '已解决',
};

/** 运行时校验：后端返回的未知值也走兜底文案，不让 UI 崩。 */
export function priorityLabel(value: string): string {
  return (PRIORITY_LABEL as Record<string, string>)[value] ?? value;
}

export function statusLabel(value: string): string {
  return (STATUS_LABEL as Record<string, string>)[value] ?? value;
}
