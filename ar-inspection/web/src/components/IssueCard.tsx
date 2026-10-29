import type { Issue, IssueStatus } from '../types';
import { formatCoord, formatDateTime } from '../utils/format';
import { PriorityBadge, StatusBadge } from './Badges';
import StatusSelect from './StatusSelect';

interface IssueCardProps {
  issue: Issue;
  pending: boolean;
  hasError: boolean;
  onChangeStatus: (issue: Issue, next: IssueStatus) => void;
}

export default function IssueCard({ issue, pending, hasError, onChangeStatus }: IssueCardProps) {
  const description = typeof issue.description === 'string' ? issue.description.trim() : '';
  const position = issue.position;

  return (
    <article
      className={`issue-card${hasError ? ' issue-card--error' : ''}${pending ? ' issue-card--pending' : ''}`}
      aria-busy={pending}
    >
      <div className="issue-card__body">
        <header className="issue-card__head">
          <h2 className="issue-card__title">{issue.title || '（无标题）'}</h2>
          <div className="issue-card__badges">
            <PriorityBadge priority={issue.priority} />
            <StatusBadge status={issue.status} />
          </div>
        </header>

        <p className={`issue-card__desc${description ? '' : ' issue-card__desc--muted'}`}>
          {description || '暂无描述'}
        </p>

        <dl className="issue-meta">
          <div className="issue-meta__item">
            <dt>AR 坐标</dt>
            <dd className="mono">
              x {formatCoord(position?.x)} · y {formatCoord(position?.y)} · z{' '}
              {formatCoord(position?.z)}
            </dd>
          </div>
          <div className="issue-meta__item">
            <dt>创建时间</dt>
            <dd>{formatDateTime(issue.createdAt)}</dd>
          </div>
          <div className="issue-meta__item">
            <dt>更新时间</dt>
            <dd>{formatDateTime(issue.updatedAt)}</dd>
          </div>
          <div className="issue-meta__item issue-meta__item--id">
            <dt>问题 ID</dt>
            <dd className="mono" title={issue.id}>
              {issue.id}
            </dd>
          </div>
        </dl>
      </div>

      <aside className="issue-card__side">
        <label className="field">
          <span className="field__label">处理状态</span>
          <StatusSelect
            value={issue.status}
            disabled={pending}
            onChange={(next) => onChangeStatus(issue, next)}
          />
        </label>
        <p className={`field__hint${pending ? ' field__hint--busy' : ''}`}>
          {pending ? '正在保存…' : '选择后立即保存到后端'}
        </p>
      </aside>
    </article>
  );
}
