import type { IssueStatus, Priority } from '../types';
import { priorityLabel, statusLabel } from '../types';

interface PriorityBadgeProps {
  priority: Priority;
}

export function PriorityBadge({ priority }: PriorityBadgeProps) {
  return <span className={`badge badge--priority-${priority}`}>优先级 · {priorityLabel(priority)}</span>;
}

interface StatusBadgeProps {
  status: IssueStatus;
}

export function StatusBadge({ status }: StatusBadgeProps) {
  return <span className={`badge badge--status-${status}`}>状态 · {statusLabel(status)}</span>;
}
