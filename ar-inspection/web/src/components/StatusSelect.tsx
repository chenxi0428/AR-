import { STATUSES, STATUS_LABEL } from '../types';
import type { IssueStatus } from '../types';

interface StatusSelectProps {
  value: IssueStatus;
  disabled?: boolean;
  onChange: (next: IssueStatus) => void;
}

/** 状态下拉：open / in_progress / resolved，选中即触发 PATCH。 */
export default function StatusSelect({ value, disabled = false, onChange }: StatusSelectProps) {
  return (
    <select
      className={`status-select status-select--${value}`}
      value={value}
      disabled={disabled}
      aria-label="处理状态"
      onChange={(event) => onChange(event.target.value as IssueStatus)}
    >
      {STATUSES.map((status) => (
        <option key={status} value={status}>
          {STATUS_LABEL[status]}
        </option>
      ))}
    </select>
  );
}
