using System;
using System.Buffers.Text;
using System.Collections.Generic;
using System.Net.Http.Headers;
using System.Text;
using static System.Net.Mime.MediaTypeNames;
using static System.Net.WebRequestMethods;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace ErdcServerMcp
{
    public class ErdcConfig
    {
        private string _identityId = "erdcadmin";
        private string _type = "NAME";

        private string _clientCode = "default";
        private string _key = "p2c4XMvAkkM9olirBn6RyQ==";

        private string _platformHost = "192.168.1.51";
        private string _platformPort = "8081";

        private string _platformPath = "/common/sso/auth/token";

        private string _tenantId = "10000";

        private string _basicAuthUser = "erdp";
        private string _basicAuthPass = "2059e28d-a083-4a58-927c-487358da3b52";

        public ErdcConfig()
        {
            LoadFromEnvironment();
        }

        /// <summary>
        /// 从环境变量加载配置，环境变量命名格式：ERDC_{属性名}（大写）
        /// 例如：ERDC_IDENTITYID, ERDC_PLATFORMHOST
        /// </summary>
        private void LoadFromEnvironment()
        {
            _identityId = GetEnvironmentValue("ERDC_IDENTITYID", _identityId);
            _type = GetEnvironmentValue("ERDC_TYPE", _type);
            _clientCode = GetEnvironmentValue("ERDC_CLIENTCODE", _clientCode);
            _key = GetEnvironmentValue("ERDC_KEY", _key);
            _platformHost = GetEnvironmentValue("ERDC_PLATFORMHOST", _platformHost);
            _platformPort = GetEnvironmentValue("ERDC_PLATFORMPORT", _platformPort);
            _platformPath = GetEnvironmentValue("ERDC_PLATFORMPATH", _platformPath);
            _tenantId = GetEnvironmentValue("ERDC_TENANTID", _tenantId);
            _basicAuthUser = GetEnvironmentValue("ERDC_BASICAUTHUSER", _basicAuthUser);
            _basicAuthPass = GetEnvironmentValue("ERDC_BASICAUTHPASS", _basicAuthPass);
        }

        /// <summary>
        /// 获取环境变量值，如果不存在则返回默认值
        /// </summary>
        /// <param name="envVarName">环境变量名称</param>
        /// <param name="defaultValue">默认值</param>
        /// <returns>环境变量值或默认值</returns>
        private string GetEnvironmentValue(string envVarName, string defaultValue)
        {
            string value = Environment.GetEnvironmentVariable(envVarName);
            return string.IsNullOrEmpty(value) ? defaultValue : value;
        }

        public string identityId => _identityId;
        public string type => _type;

        public string clientCode => _clientCode;
        public string key => _key;

        public string platformHost => _platformHost;
        public string platformPort => _platformPort;

        public string platformPath => _platformPath;

        public string tenantId => _tenantId;

        public string basicAuthUser => _basicAuthUser;
        public string basicAuthPass => _basicAuthPass;

        // 计划（伪代码）：
        // - 原代码试图将 HttpRequestHeaders 用集合初始化器直接构造，这是不合法的。
        // - 将 headers 类型改为 IDictionary<string,string>（或 Dictionary），并在 getter 中返回一个新的字典。
        // - 使用本实例的 token（this.token）作为 Authorization 值，避免引用未定义的 config。
        // - 保留其它实现不变，以最小改动修复编译错误。
        public IDictionary<string, string> headers =>
            new Dictionary<string, string>
            {
                ["Accept"] = "application/json, text/plain, */*",
                ["Accept-Language"] = "zh-CN,zh;q=0.9,en;q=0.8,en-GB;q=0.7,en-US;q=0.6,zh-TW;q=0.5",
                ["App-Name"] = "ALL",
                ["Authorization"] = this.token ?? string.Empty,
                ["Tenant-Id"] = tenantId,
            };

        public string token => GetErdcToken();

        public string baseurl => $"http://{platformHost}:{platformPort}";

        private string GetErdcToken()
        {
            /*
             * 根据文档《第三方系统免密登录基础开发.md》编写的 Token 生成及业务 Token 获取程序
             */

            // ================= 配置区域 =================
            // 1. 生成 Request Token 的配置
            //const string identityId = "erdcadmin";          // 后续请求平台接口的用户名
            //const string type = "NAME";                 // 固定为 NAME
            //const string clientCode = "default";             // 客户端代码 (nacos配置)
            //const string key = "p2c4XMvAkkM9olirBn6RyQ=="; // 密钥 (nacos配置)

            // 2. 获取 Business Token 的接口配置
            //const string platformHost = "192.168.1.51";
            //const int platformPort = 8081;
            //const string platformPath = "/common/sso/auth/token";

            // 3. 接口 Basic Auth 认证信息 (步骤二生成的客户端ID和密钥)
            // 请根据实际情况修改以下两个变量
            const string basicAuthUser = "erdp";
            const string basicAuthPass = "2059e28d-a083-4a58-927c-487358da3b52";
            // ===========================================

            try
            {
                // 生成免密登录 Request Token (步骤三)
                var payloadObj = new
                {
                    identityId = identityId,
                    timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    type = type,
                    clientCode = clientCode,
                };

                string jsonString = System.Text.Json.JsonSerializer.Serialize(payloadObj);
                string base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(jsonString));

                string signContent = key + base64;
                byte[] hash;
                using (var sha1 = System.Security.Cryptography.SHA1.Create())
                {
                    hash = sha1.ComputeHash(Encoding.UTF8.GetBytes(signContent));
                }

                var sb = new StringBuilder(hash.Length * 2);
                foreach (var b in hash)
                {
                    sb.Append(b.ToString("x2"));
                }
                string digestHex = sb.ToString();

                string tokenPayload = base64 + "." + digestHex;
                string reqToken = Convert.ToBase64String(Encoding.UTF8.GetBytes(tokenPayload));

                // 调用接口获取 Business Token (步骤四)
                // 使用 MultipartFormDataContent 构造请求体，使用 Basic Auth
                string boundary = "----WebKitFormBoundary" + Guid.NewGuid().ToString("N");
                using (var content = new System.Net.Http.MultipartFormDataContent(boundary))
                {
                    // 参数 type="token"
                    content.Add(new System.Net.Http.StringContent("token", Encoding.UTF8), "type");
                    // 参数 token=<reqToken>
                    content.Add(
                        new System.Net.Http.StringContent(reqToken, Encoding.UTF8),
                        "token"
                    );

                    using (
                        var client = new System.Net.Http.HttpClient()
                        {
                            Timeout = TimeSpan.FromSeconds(10),
                        }
                    )
                    {
                        var auth = Convert.ToBase64String(
                            Encoding.UTF8.GetBytes(basicAuthUser + ":" + basicAuthPass)
                        );
                        client.DefaultRequestHeaders.Authorization =
                            new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", auth);
                        string ssourl = $"http://{platformHost}:{platformPort}{platformPath}";
                        var response = client.PostAsync(ssourl, content).GetAwaiter().GetResult();
                        var responseText = response
                            .Content.ReadAsStringAsync()
                            .GetAwaiter()
                            .GetResult();

                        // 解析响应 JSON，优先返回 data 或 data.token 字段
                        try
                        {
                            using (var doc = System.Text.Json.JsonDocument.Parse(responseText))
                            {
                                var root = doc.RootElement;

                                if (root.TryGetProperty("data", out var dataElem))
                                {
                                    if (dataElem.ValueKind == System.Text.Json.JsonValueKind.String)
                                    {
                                        return dataElem.GetString();
                                    }

                                    if (dataElem.ValueKind == System.Text.Json.JsonValueKind.Object)
                                    {
                                        if (
                                            dataElem.TryGetProperty("token", out var tokenElem)
                                            && tokenElem.ValueKind
                                                == System.Text.Json.JsonValueKind.String
                                        )
                                        {
                                            return tokenElem.GetString();
                                        }

                                        // 返回 data 的 JSON 文本作为兜底
                                        return dataElem.GetRawText();
                                    }

                                    // 返回 data 的文本表示
                                    return dataElem.ToString();
                                }

                                // 兼容通过 code/success 判断的场景，返回整个响应作为兜底
                                if (
                                    root.TryGetProperty("success", out var successElem)
                                    && successElem.ValueKind == System.Text.Json.JsonValueKind.True
                                )
                                {
                                    return responseText;
                                }

                                if (root.TryGetProperty("code", out var codeElem))
                                {
                                    if (
                                        (
                                            codeElem.ValueKind
                                                == System.Text.Json.JsonValueKind.Number
                                            && codeElem.GetInt32() == 200
                                        )
                                        || (
                                            codeElem.ValueKind
                                                == System.Text.Json.JsonValueKind.String
                                            && codeElem.GetString() == "200"
                                        )
                                    )
                                    {
                                        return responseText;
                                    }
                                }
                            }
                        }
                        catch (System.Text.Json.JsonException)
                        {
                            // 响应不是合法 JSON，直接返回原始文本
                            return responseText;
                        }

                        // 非成功或无法解析时返回原始响应文本
                        return responseText;
                    }
                }
            }
            catch (Exception)
            {
                // 在生产代码应记录异常与 correlation id 以便排查，这里保持简单返回 null
                return null;
            }
        }
    }
}
