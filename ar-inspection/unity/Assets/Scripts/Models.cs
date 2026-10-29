using System;
using System.Collections.Generic;
using UnityEngine;

namespace ArInspection
{
    /// <summary>
    /// 与 docs/API.md 第 1 节的 Issue 数据模型完全一致。
    ///
    /// 重要：Unity 的 JsonUtility 只序列化 public 字段，且字段名必须与 JSON key 完全一致，
    /// 所以这里刻意使用 camelCase 字段名（虽然不符合 C# 命名规范，但由接口契约决定）。
    /// 不要把这些字段改成属性（property），JsonUtility 不会序列化属性。
    /// </summary>
    [Serializable]
    public class Issue
    {
        /// <summary>后端生成，唯一，只读。例如 iss_20261029_7f3a2c</summary>
        public string id;

        /// <summary>必填，1–200 字符。</summary>
        public string title;

        /// <summary>可选，≤2000 字符，默认空字符串。</summary>
        public string description;

        /// <summary>枚举 low / medium / high，缺省 medium。</summary>
        public string priority;

        /// <summary>枚举 open / in_progress / resolved，新建时后端固定为 open。</summary>
        public string status;

        /// <summary>Unity 世界坐标。</summary>
        public Position position;

        /// <summary>后端生成，RFC 3339（UTC，带毫秒）。</summary>
        public string createdAt;

        /// <summary>后端生成，任何修改后刷新。</summary>
        public string updatedAt;

        public override string ToString()
        {
            return string.Format("Issue(id={0}, title={1}, priority={2}, status={3})",
                id, title, priority, status);
        }
    }

    /// <summary>
    /// Issue.position 对应的 { x, y, z }。字段名必须与 JSON key 完全一致。
    /// </summary>
    [Serializable]
    public class Position
    {
        public float x;
        public float y;
        public float z;

        public Position()
        {
        }

        public Position(float x, float y, float z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }

        /// <summary>由 Unity 世界坐标构造请求体坐标。</summary>
        public static Position FromVector3(Vector3 value)
        {
            return new Position(value.x, value.y, value.z);
        }

        /// <summary>转回 Unity 世界坐标（查看历史数据时用得上）。</summary>
        public Vector3 ToVector3()
        {
            return new Vector3(x, y, z);
        }

        public override string ToString()
        {
            return string.Format("({0:0.00}, {1:0.00}, {2:0.00})", x, y, z);
        }
    }

    /// <summary>
    /// POST /api/issues 的请求体（API.md 2.3）。
    /// 客户端不传 id / status / createdAt / updatedAt，这些由后端生成。
    /// </summary>
    [Serializable]
    public class IssueCreateRequest
    {
        public string title;
        public string description;
        public string priority;
        public Position position;
    }

    /// <summary>
    /// GET /api/issues 的响应体：{ "items": [ Issue, ... ] }
    /// 当前 Unity 端只负责上报，这个模型用于接口契约完整性 / 后续扩展历史列表。
    /// </summary>
    [Serializable]
    public class IssueListResponse
    {
        public List<Issue> items = new List<Issue>();
    }

    /// <summary>
    /// PATCH /api/issues/{id} 的请求体（API.md 2.4，由 React 管理端使用，
    /// Unity 端暂不修改状态，但按契约保留模型）。
    /// </summary>
    [Serializable]
    public class IssueStatusPatch
    {
        public string status;
    }

    /// <summary>
    /// 错误响应中的 fields（API.md 第 3 节）。后端目前只会带 title/description/priority/position/status
    /// 这几个 key；JsonUtility 不支持 Dictionary，所以用固定字段的类来接收，未知 key 会被忽略。
    /// </summary>
    [Serializable]
    public class ApiErrorFields
    {
        public string title;
        public string description;
        public string priority;
        public string position;
        public string status;

        /// <summary>把字段错误拼成一行可读文本，例如 "title: 不能为空"。</summary>
        public string ToReadableString()
        {
            List<string> parts = new List<string>();
            if (!string.IsNullOrEmpty(title)) parts.Add("title: " + title);
            if (!string.IsNullOrEmpty(description)) parts.Add("description: " + description);
            if (!string.IsNullOrEmpty(priority)) parts.Add("priority: " + priority);
            if (!string.IsNullOrEmpty(position)) parts.Add("position: " + position);
            if (!string.IsNullOrEmpty(status)) parts.Add("status: " + status);
            return string.Join("；", parts.ToArray());
        }
    }

    /// <summary>
    /// 统一的错误响应格式（API.md 第 3 节）：{ "code": "...", "message": "...", "fields": {...} }
    /// message 是中文，可直接展示给用户；fields 可能省略。
    /// </summary>
    [Serializable]
    public class ApiError
    {
        public string code;
        public string message;
        public ApiErrorFields fields;

        public string ToDisplayMessage()
        {
            string text = string.IsNullOrEmpty(message) ? "未知错误" : message;
            if (fields != null)
            {
                string fieldsText = fields.ToReadableString();
                if (!string.IsNullOrEmpty(fieldsText))
                {
                    text += "（" + fieldsText + "）";
                }
            }
            return text;
        }
    }

    /// <summary>GET /api/health 的响应体（API.md 2.1）。</summary>
    [Serializable]
    public class HealthResponse
    {
        public string status;
        public string service;
        public string version;
        public string time;
    }

    /// <summary>priority 枚举值，避免手写字符串拼错。</summary>
    public static class IssuePriority
    {
        public const string Low = "low";
        public const string Medium = "medium";
        public const string High = "high";

        /// <summary>优先级下拉框的顺序，与 UI 一致。</summary>
        public static readonly string[] All = { Low, Medium, High };

        /// <summary>返回该优先级的展示色（标记 / 列表用）。</summary>
        public static Color GetColor(string priority)
        {
            switch (priority)
            {
                case High:
                    return new Color(0.95f, 0.32f, 0.28f);
                case Low:
                    return new Color(0.45f, 0.72f, 0.95f);
                default:
                    return new Color(0.98f, 0.72f, 0.25f);
            }
        }
    }

    /// <summary>status 枚举值（Unity 端不修改状态，仅用于展示）。</summary>
    public static class IssueStatus
    {
        public const string Open = "open";
        public const string InProgress = "in_progress";
        public const string Resolved = "resolved";
    }
}
