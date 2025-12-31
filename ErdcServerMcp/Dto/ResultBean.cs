using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using ErdcServerMcp;

namespace ErdcServerMcp.Dto
{
	public class ResultBean
	{
		[JsonPropertyName("success")]
		public bool Success { get; set; }

		[JsonPropertyName("code")]
		public string? Code { get; set; }

		[JsonPropertyName("message")]
		public string? Message { get; set; }

		// 使用 JsonNode 以支持对象或数组两种结构
		[JsonPropertyName("data")]
		public JsonNode? Data { get; set; }

		[JsonPropertyName("traceId")]
		public string? TraceId { get; set; }

		// 使用 JsonObject 表示 key-value 映射
		[JsonPropertyName("customMap")]
		public JsonObject? CustomMap { get; set; }

		public override string ToString()
		{
			return JsonSerializer.Serialize(this, JsonOptions.Compact);
		}

		// 详细计划（伪代码）：
		// - 提供静态方法 `FromJson(string json, JsonSerializerOptions? options = null)`：
		//   - 如果输入为空，返回 null
		//   - 使用 JsonSerializer.Deserialize<ResultBean>(json, options) 并捕获异常，异常时返回 null
		// - 提供静态方法 `TryParse(string json, out ResultBean? result, JsonSerializerOptions? options = null)`：
		//   - 调用 Deserialize，并在成功时返回 true，失败时返回 false 并将 result 设为 null
		// - 提供实例方法 `GetDataAs<T>(JsonSerializerOptions? options = null)`：
		//   - 如果 Data 为 null，返回 default(T)
		//   - 否则调用 `Data.Deserialize<T>(options)` 并返回结果（捕获异常并返回 default(T)）
		// - 提供实例方法 `TryGetCustomValue<T>(string key, out T? value, JsonSerializerOptions? options = null)`：
		//   - 如果 CustomMap 或指定 key 不存在，返回 false
		//   - 否则将对应 JsonNode 反序列化为 T 并返回 true（异常时返回 false）
		// - 所有方法尽量保持简单、可复用且对异常进行处理以避免抛出未捕获异常

		public static ResultBean? FromJson(string json, JsonSerializerOptions? options = null)
		{
			if (string.IsNullOrWhiteSpace(json))
			{
				return null;
			}

			var opts = options ?? JsonOptions.Deserialize;

			try
			{
				return JsonSerializer.Deserialize<ResultBean>(json, opts);
			}
			catch (JsonException)
			{
				return null;
			}
			catch (Exception)
			{
				return null;
			}
		}

		public static bool TryParse(string json, out ResultBean? result, JsonSerializerOptions? options = null)
		{
			result = null;
			if (string.IsNullOrWhiteSpace(json))
			{
				return false;
			}

			var opts = options ?? JsonOptions.Deserialize;

			try
			{
				result = JsonSerializer.Deserialize<ResultBean>(json, opts);
				return result != null;
			}
			catch (JsonException)
			{
				result = null;
				return false;
			}
			catch (Exception)
			{
				result = null;
				return false;
			}
		}

		public T? GetDataAs<T>(JsonSerializerOptions? options = null)
		{
			if (Data == null)
			{
				return default;
			}

			try
			{
				return Data.Deserialize<T>(options);
			}
			catch (JsonException)
			{
				return default;
			}
			catch (Exception)
			{
				return default;
			}
		}

		public bool TryGetCustomValue<T>(string key, out T? value, JsonSerializerOptions? options = null)
		{
			value = default;

			if (string.IsNullOrEmpty(key) || CustomMap == null)
			{
				return false;
			}

			if (!CustomMap.ContainsKey(key))
			{
				return false;
			}

			var node = CustomMap[key];
			if (node == null)
			{
				return false;
			}

			try
			{
				value = node.Deserialize<T>(options);
				return true;
			}
			catch (JsonException)
			{
				value = default;
				return false;
			}
			catch (Exception)
			{
				value = default;
				return false;
			}
		}
	}
}
