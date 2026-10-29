/** 展示层格式化工具：时间、坐标，全部做“值不合法也不崩”的兜底。 */

function pad2(value: number): string {
  return String(value).padStart(2, '0');
}

/**
 * 把 RFC 3339 时间（如 2026-10-29T05:01:02.123Z）转成本地时区的
 * `YYYY-MM-DD HH:mm:ss`；无法解析时原样返回，空值显示 —。
 */
export function formatDateTime(raw: string | null | undefined): string {
  if (!raw) return '—';
  const date = new Date(raw);
  if (Number.isNaN(date.getTime())) return raw;
  return (
    `${date.getFullYear()}-${pad2(date.getMonth() + 1)}-${pad2(date.getDate())} ` +
    `${pad2(date.getHours())}:${pad2(date.getMinutes())}:${pad2(date.getSeconds())}`
  );
}

/** Unity 世界坐标，保留两位小数。 */
export function formatCoord(value: unknown): string {
  if (typeof value === 'number' && Number.isFinite(value)) return value.toFixed(2);
  if (value === null || value === undefined || value === '') return '—';
  return String(value);
}

export function formatCount(value: number): string {
  return Number.isFinite(value) ? String(value) : '0';
}
