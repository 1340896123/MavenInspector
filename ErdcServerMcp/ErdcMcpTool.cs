using ErdcServerMcp.Dto;
using ModelContextProtocol.Server;
using System;
using System.ComponentModel;
using System.Reflection.Metadata;
using System.Text.Json;

namespace ErdcServerMcp
{
    [McpServerToolType]
    public class ErdcMcpTool(ErdcRequestService erdcRequestService)
    {
        /// <summary>
        /// 获取ERDC服务器中所有类型定义的信息
        /// </summary>
        /// <returns>包含所有类型定义的JSON字符串</returns>
        [McpServerTool(Name = "get_server_infos")]
        [Description("获取ERDC服务器中所有类型定义的信息")]
        public async Task<string> GetServerInfosAsync()
        {
            try
            {
                var result = await erdcRequestService.GetServerInfosAsync().ConfigureAwait(false);
                return FormatResult(result, typeof(List<TypeTreeNodeVo>));
            }
            catch (Exception ex)
            {
                return FormatError(ex, "获取服务器信息失败");
            }
        }

        /// <summary>
        /// 根据对象ID获取指定的类型定义
        /// </summary>
        /// <param name="oid">类型定义的对象ID</param>
        /// <returns>包含类型定义的JSON字符串</returns>
        [McpServerTool(Name = "get_type_def_by_id")]
        [Description("根据对象ID获取ERDC服务器中指定的类型定义")]
        public async Task<string> GetTypeDefByIdAsync(
            [Description("类型定义的对象ID，示例:OR:erd.cloud.foundation.type.entity.TypeDefinition:1505102889691983874")] string oid)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(oid))
                {
                    return FormatError(new ArgumentException("oid不能为空"), "参数验证失败");
                }

                var result = await erdcRequestService.GetTypeDefByIdAsync(oid).ConfigureAwait(false);
                return FormatResult(result);
            }
            catch (Exception ex)
            {
                return FormatError(ex, $"获取类型定义失败 (oid: {oid})");
            }
        }

        /// <summary>
        /// 根据oid获取对象，先从缓存查找，找不到则调用接口获取
        /// </summary>
        /// <param name="oid">对象ID</param>
        /// <returns>包含对象数据的JSON字符串</returns>
        [McpServerTool(Name = "get_by_oid")]
        [Description("根据oid获取ERDC服务器中的对象，先从缓存查找，找不到则调用/fam/attr接口获取")]
        public async Task<string> GetByOidAsync(
            [Description("对象OID，示例:OR:erd.cloud.pdm.part.entity.EtPart:197765688268681216")] string oid)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(oid))
                {
                    return FormatError(new ArgumentException("oid不能为空"), "参数验证失败");
                }

                var result = await erdcRequestService.GetByOidAsync(oid).ConfigureAwait(false);

                if (result == null)
                {
                    return JsonSerializer.Serialize(new
                    {
                        success = false,
                        message = "未找到指定的对象"
                    }, JsonOptions.Default);
                }

                return JsonSerializer.Serialize(result, JsonOptions.Default);
            }
            catch (Exception ex)
            {
                return FormatError(ex, $"根据oid获取对象失败 (oid: {oid})");
            }
        }

        /// <summary>
        /// 根据类型定义ID获取属性列表
        /// </summary>
        /// <param name="typeDefinitionId">类型定义ID</param>
        /// <returns>包含属性列表的JSON字符串</returns>
        [McpServerTool(Name = "list_type_attributes")]
        [Description("根据类型定义ID获取ERDC服务器中的属性列表")]
        public async Task<string> ListTypeAttributesAsync(
            [Description("类型定义ID，示例:OR:erd.cloud.foundation.type.entity.TypeDefinition:1689235656964952065")] string typeDefinitionId)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(typeDefinitionId))
                {
                    return FormatError(new ArgumentException("typeDefinitionId不能为空"), "参数验证失败");
                }

                var result = await erdcRequestService.ListTypeAttributeByTypeDefinitionIdsAsync(typeDefinitionId).ConfigureAwait(false);

                if (result == null || result.Data == null)
                {
                    return FormatError(new InvalidOperationException("响应缺少 data 字段"), "获取属性列表失败");
                }

                // 将 result.Data 规范化为 JSON 并解析为 JsonDocument，以便稳定地遍历和访问
                var rawJson = JsonSerializer.Serialize(result.Data);
                using var doc = JsonDocument.Parse(rawJson);
                var root = doc.RootElement;

                if (root.ValueKind != JsonValueKind.Array)
                {
                    return FormatError(new InvalidOperationException("预期 data 为数组"), "获取属性列表失败");
                }

                var list = new List<AttributeDefine>();
                foreach (var item in root.EnumerateArray())
                {
                    // 安全读取各个字段
                    var attrName = GetJsonString(item, "attrName") ?? string.Empty;
                    var attrCategory = GetJsonString(item, "attrCategory") ?? string.Empty;
                    var dateType = GetJsonString(item, "dataTypeDto", "name") ?? GetJsonString(item, "dataTypeDto") ?? string.Empty;

                    // dbColumnName: flexAttrColumnMap[attrName][0].columnName
                    string dbColumnName = string.Empty;
                    if (!string.IsNullOrEmpty(attrName)
                        && item.TryGetProperty("flexAttrColumnMap", out var flexMap)
                        && flexMap.ValueKind == JsonValueKind.Object
                        && flexMap.TryGetProperty(attrName, out var flexArr)
                        && flexArr.ValueKind == JsonValueKind.Array
                        && flexArr.GetArrayLength() > 0)
                    {
                        var first = flexArr[0];
                        var colName = GetJsonString(first, "columnName");
                        if (!string.IsNullOrEmpty(colName))
                        {
                            dbColumnName = colName;
                        }
                    }

                    // displayName: propertyValueMap.displayName.languageJson.value
                    var displayName = GetJsonString(item, "propertyValueMap", "displayName", "languageJson", "value") ?? string.Empty;
                    var typeReference = GetJsonString(item, "typeReference") ?? string.Empty;

                    var attrdefine = new AttributeDefine
                    {
                        attrCategory = attrCategory,
                        attrName = attrName,
                        dateType = dateType,
                        dbColumnName = dbColumnName,
                        displayName = displayName,

                        typeReference = typeReference
                    };

                    list.Add(attrdefine);
                }

                return JsonSerializer.Serialize(list, JsonOptions.Default);
            }
            catch (Exception ex)
            {
                return FormatError(ex, $"获取属性列表失败 (typeDefinitionId: {typeDefinitionId})");
            }
        }

        #region Private Helper Methods

        /// <summary>
        /// 格式化成功的结果（直接序列化 data）
        /// </summary>
        private static string FormatResult(ResultBean result)
        {
            if (result == null)
            {
                return JsonSerializer.Serialize(new
                {
                    success = false,
                    message = "响应结果为空"
                }, JsonOptions.Default);
            }

            return JsonSerializer.Serialize(result.Data, JsonOptions.Default);
        }

        /// <summary>
        /// 将 result.Data 先反序列化为指定的目标类型，再序列化为 JSON（用于移除多余字段）
        /// </summary>
        private static string FormatResult(ResultBean result, Type targetType)
        {
            if (result == null)
            {
                return JsonSerializer.Serialize(new
                {
                    success = false,
                    message = "响应结果为空"
                }, JsonOptions.Default);
            }

            if (targetType == null)
            {
                return FormatResult(result);
            }

            try
            {
                // 先把 result.Data 序列化为字符串 JSON（以便统一处理不同数据结构）
                var rawJson = JsonSerializer.Serialize(result.Data);

                // 再将该 JSON 反序列化为目标类型实例，从而去掉非目标类型的多余字段
                // 使用大小写不敏感的属性名匹配，确保 JSON 中小写字段（如 "id"）能映射到目标类型的 PascalCase 属性（如 "Id"）
                var typedObj = JsonSerializer.Deserialize(rawJson, targetType, JsonOptions.Deserialize);

                string? result2 = JsonSerializer.Serialize(typedObj, JsonOptions.Default);
                // 最后将目标类型实例按项目的序列化选项输出为最终 JSON
                return result2;
            }
            catch (Exception ex)
            {
                // 反序列化失败时，返回详细错误信息（保持与其他错误格式一致）
                return FormatError(ex, "将 data 转换为指定类型时发生错误");
            }
        }

        /// <summary>
        /// 格式化错误信息
        /// </summary>
        private static string FormatError(Exception ex, string contextMessage)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                message = contextMessage,
                error = new
                {
                    type = ex.GetType().Name,
                    message = ex.Message,
                    innerMessage = ex.InnerException?.Message
                },
                timestamp = DateTime.UtcNow.ToString("o")
            }, JsonOptions.Default);
        }

        /// <summary>
        /// 从 JsonElement 按路径安全读取字符串表示。
        /// 若路径任一段不存在，返回 null。
        /// 对 string/number/bool 等类型做合理的字符串返回。
        /// </summary>
        private static string? GetJsonString(JsonElement element, params string[] path)
        {
            JsonElement current = element;
            foreach (var p in path)
            {
                if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(p, out var next))
                {
                    return null;
                }

                current = next;
            }

            return current.ValueKind switch
            {
                JsonValueKind.String => current.GetString(),
                JsonValueKind.Number => current.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                JsonValueKind.Null => null,
                _ => current.GetRawText()
            };
        }

        #endregion
    }
}
