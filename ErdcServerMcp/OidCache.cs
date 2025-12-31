using System.Collections.Concurrent;
using System.Text.Json.Nodes;

namespace ErdcServerMcp
{
    /// <summary>
    /// Oid 缓存类，用于管理 oid 到 JsonNode 的映射
    /// </summary>
    public class OidCache
    {
        private readonly ConcurrentDictionary<string, JsonNode> _cache = new();

        /// <summary>
        /// 尝试获取缓存的 JsonNode
        /// </summary>
        public bool TryGet(string oid, out JsonNode? node)
        {
            return _cache.TryGetValue(oid, out node);
        }

        /// <summary>
        /// 添加或更新缓存
        /// </summary>
        public void Set(string oid, JsonNode node)
        {
            if (string.IsNullOrWhiteSpace(oid) || node == null)
            {
                return;
            }
            _cache.AddOrUpdate(oid, node, (_, _) => node);
        }

        /// <summary>
        /// 批量添加缓存项
        /// </summary>
        public void SetRange(IEnumerable<KeyValuePair<string, JsonNode>> items)
        {
            if (items == null)
            {
                return;
            }

            foreach (var item in items)
            {
                Set(item.Key, item.Value);
            }
        }

        /// <summary>
        /// 检查缓存中是否存在指定的 oid
        /// </summary>
        public bool Contains(string oid)
        {
            return !string.IsNullOrWhiteSpace(oid) && _cache.ContainsKey(oid);
        }

        /// <summary>
        /// 获取缓存中的所有 oid
        /// </summary>
        public IEnumerable<string> GetAllOids()
        {
            return _cache.Keys.ToList();
        }

        /// <summary>
        /// 清空缓存
        /// </summary>
        public void Clear()
        {
            _cache.Clear();
        }

        /// <summary>
        /// 获取缓存项数量
        /// </summary>
        public int Count => _cache.Count;
    }
}
