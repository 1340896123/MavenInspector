using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace ErdcServerMcp
{
    public class ErdcConfig
    {
        private readonly IConfiguration _configuration;

        public ErdcConfig(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public string identityId => _configuration["Erdc:IdentityId"] ?? "erdcadmin";
        public string type => _configuration["Erdc:Type"] ?? "NAME";
        public string clientCode => _configuration["Erdc:ClientCode"] ?? "default";
        public string key => _configuration["Erdc:Key"] ?? "p2c4XMvAkkM9olirBn6RyQ==";
        public string platformHost => _configuration["Erdc:PlatformHost"] ?? "192.168.1.51";
        public string platformPort => _configuration["Erdc:PlatformPort"] ?? "8081";
        public string platformPath => _configuration["Erdc:PlatformPath"] ?? "/common/sso/auth/token";
        public string tenantId => _configuration["Erdc:TenantId"] ?? "10000";
        public string basicAuthUser => _configuration["Erdc:BasicAuthUser"] ?? "erdp";
        public string basicAuthPass => _configuration["Erdc:BasicAuthPass"] ?? "2059e28d-a083-4a58-927c-487358da3b52";

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
            try
            {
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

                string boundary = "----WebKitFormBoundary" + Guid.NewGuid().ToString("N");
                using (var content = new System.Net.Http.MultipartFormDataContent(boundary))
                {
                    content.Add(new System.Net.Http.StringContent("token", Encoding.UTF8), "type");
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

                                        return dataElem.GetRawText();
                                    }

                                    return dataElem.ToString();
                                }

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
                            return responseText;
                        }

                        return responseText;
                    }
                }
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
