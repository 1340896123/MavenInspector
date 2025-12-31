using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using ErdcServerMcp.Dto;
using ModelContextProtocol.Server;

namespace ErdcServerMcp
{
    public class ErdcRequestService
    {
        private readonly ErdcConfig _config;
        private readonly OidCache _oidCache;

        public ErdcRequestService(ErdcConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _oidCache = new OidCache();
        }

        private HttpClient CreateHttpClient()
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            };
            return new HttpClient(handler, disposeHandler: true);
        }

        private void ApplyHeaders(HttpRequestMessage request)
        {
            if (_config is null || _config.headers is null)
            {
                return;
            }

            foreach (var header in _config.headers)
            {
                if (!request.Headers.TryAddWithoutValidation(header.Key, header.Value))
                {
                    if (request.Content == null)
                    {
                        request.Content = new StringContent(
                            string.Empty,
                            Encoding.UTF8,
                            "application/json"
                        );
                    }
                    request.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }
        }

        public async Task<ResultBean> GetServerInfosAsync()
        {
            var uri = new Uri(
                $"{_config.baseurl}/fam/type/typeDefinition/all?_t={DateTime.Now.Ticks}"
            );
            using var client = CreateHttpClient();
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);

            ApplyHeaders(request);

            using var response = await client.SendAsync(request).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            var result = ResultBean.FromJson(body);
            if (result is null)
            {
                throw new InvalidOperationException("响应内容无法解析为 ResultBean。");
            }

            // 自动更新缓存
            UpdateCacheFromResult(result);

            return result;
        }

        public async Task<ResultBean> GetTypeDefByIdAsync(string oid)
        {
            if (string.IsNullOrWhiteSpace(oid))
            {
                throw new ArgumentException("oid 不能为空。", nameof(oid));
            }

            var encodedOid = WebUtility.UrlEncode(oid);
            var uri = new Uri(
                $"{_config.baseurl}/fam/type/typeDefinition/getTypeDefById?_t={DateTime.Now.Ticks}&oid={encodedOid}"
            );

            using var client = CreateHttpClient();
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);

            ApplyHeaders(request);

            using var response = await client.SendAsync(request).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            var result = ResultBean.FromJson(body);
            if (result is null)
            {
                throw new InvalidOperationException("响应内容无法解析为 ResultBean。");
            }

            // 自动更新缓存
            UpdateCacheFromResult(result);

            return result;
        }

        public async Task<ResultBean> ListTypeAttributeByTypeDefinitionIdsAsync(string typeDefinitionId)
        {
            if (string.IsNullOrWhiteSpace(typeDefinitionId))
            {
                throw new ArgumentException("typeDefinitionId 不能为空。", nameof(typeDefinitionId));
            }

            var encodedId = WebUtility.UrlEncode(typeDefinitionId);
            var uri = new Uri(
                $"{_config.baseurl}/fam/type/attribute/listTypeAttributeDtoByTypeDefinitionIds?_t={DateTime.Now.Ticks}&typeDefinitionId={encodedId}"
            );

            using var client = CreateHttpClient();
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);

            ApplyHeaders(request);

            using var response = await client.SendAsync(request).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            var result = ResultBean.FromJson(body);
            if (result is null)
            {
                throw new InvalidOperationException("响应内容无法解析为 ResultBean。");
            }

            // 自动更新缓存
            UpdateCacheFromResult(result);

            return result;
        }

        /// <summary>
        /// 根据 oid 获取对象，首先从缓存中查找，如果找不到则调用 /fam/attr 接口获取
        /// </summary>
        public async Task<JsonNode?> GetByOidAsync(string oid)
        {
            if (string.IsNullOrWhiteSpace(oid))
            {
                throw new ArgumentException("oid 不能为空。", nameof(oid));
            }

            // 首先在缓存中查找
            if (_oidCache.TryGet(oid, out var cachedNode))
            {
                return cachedNode;
            }

            // 缓存中没有，调用接口获取
            var encodedOid = WebUtility.UrlEncode(oid);
            var uri = new Uri(
                $"{_config.baseurl}/fam/attr?_t={DateTime.Now.Ticks}&oid={encodedOid}"
            );

            using var client = CreateHttpClient();
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);

            ApplyHeaders(request);

            using var response = await client.SendAsync(request).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            var result = ResultBean.FromJson(body);

            if (result is null)
            {
                throw new InvalidOperationException("响应内容无法解析为 ResultBean。");
            }

            if (result.Data == null)
            {
                return null;
            }

            // 获取到的结果也要缓存到字典中
            _oidCache.Set(oid, result.Data);

            // 同时从返回的数据中提取所有包含 oid 的对象并缓存
            ExtractAndCacheOids(result.Data);

            return result.Data;
        }

        /// <summary>
        /// 从 ResultBean.Data 中递归提取所有包含 "oid" 属性的对象并缓存
        /// </summary>
        private void UpdateCacheFromResult(ResultBean result)
        {
            if (result?.Data == null)
            {
                return;
            }

            ExtractAndCacheOids(result.Data);
        }

        /// <summary>
        /// 递归提取 JsonNode 中所有包含 "oid" 属性的对象并缓存
        /// </summary>
        private void ExtractAndCacheOids(JsonNode node)
        {
            if (node == null)
            {
                return;
            }

            switch (node)
            {
                case JsonObject obj:
                    // 检查当前对象是否有 oid 属性
                    if (obj.TryGetPropertyValue("oid", out var oidNode) &&
                        oidNode != null &&
                        oidNode.GetValueKind() != JsonValueKind.Null)
                    {
                        var oidValue = oidNode.AsValue().ToString();
                        if (!string.IsNullOrWhiteSpace(oidValue))
                        {
                            _oidCache.Set(oidValue, obj);
                        }
                    }

                    // 递归处理所有属性值
                    foreach (var property in obj)
                    {
                        ExtractAndCacheOids(property.Value);
                    }
                    break;

                case JsonArray array:
                    // 递归处理数组中的每个元素
                    foreach (var item in array)
                    {
                        ExtractAndCacheOids(item);
                    }
                    break;

                case JsonValue:
                    // 值类型不需要处理
                    break;
            }
        }

        /// <summary>
        /// 获取当前的 oid 缓存实例
        /// </summary>
        public OidCache GetOidCache()
        {
            return _oidCache;
        }
    }
}
