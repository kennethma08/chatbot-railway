using System;
using System.Linq;
using System.Collections.Generic;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using WhatsappClient.Models;

namespace WhatsappClient.Services
{
    public class ApiService
    {
        private readonly HttpClient _http;
        private readonly string _empresaIdFallback;
        private readonly IHttpContextAccessor _accessor;

        private const string HDR_EMPRESA = "X-Empresa-Id";

        private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true
        };

        public ApiService(HttpClient http, string empresaId, IHttpContextAccessor accessor)
        {
            _http = http;
            _empresaIdFallback = string.IsNullOrWhiteSpace(empresaId) ? "1" : empresaId.Trim();
            _accessor = accessor;
        }

        // ========= Cabeceras =========
        private string CurrentEmpresaId()
        {
            var emp = _accessor.HttpContext?.Session?.GetString("EMPRESA_ID");
            if (string.IsNullOrWhiteSpace(emp)) emp = _empresaIdFallback;
            if (string.IsNullOrWhiteSpace(emp)) emp = "1";
            return emp!;
        }

        private static string CleanupToken(string t)
        {
            if (string.IsNullOrWhiteSpace(t)) return string.Empty;
            var s = t.Trim();
            if (s.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) s = s.Substring(7).Trim();
            if (s.StartsWith("\"") && s.EndsWith("\"")) s = s.Trim('\"');
            return s;
        }

        private static DateTimeOffset? GetJwtExpiry(string token)
        {
            try
            {
                // JWT base64url payload
                var parts = token.Split('.');
                if (parts.Length < 2) return null;
                string Base64UrlToBase64(string i)
                {
                    i = i.Replace('-', '+').Replace('_', '/');
                    return i.PadRight(i.Length + (4 - i.Length % 4) % 4, '=');
                }
                var payloadJson = Encoding.UTF8.GetString(Convert.FromBase64String(Base64UrlToBase64(parts[1])));
                using var doc = JsonDocument.Parse(payloadJson);
                if (doc.RootElement.TryGetProperty("exp", out var expEl))
                {
                    long exp = expEl.ValueKind == JsonValueKind.String
                        ? long.Parse(expEl.GetString()!)
                        : expEl.GetInt64();
                    return DateTimeOffset.FromUnixTimeSeconds(exp);
                }
            }
            catch { }
            return null;
        }

        private static bool IsExpiredOrNear(DateTimeOffset? exp, int skewSeconds = 60)
        {
            if (exp == null) return false;
            return DateTimeOffset.UtcNow >= exp.Value.AddSeconds(-skewSeconds);
        }

        private void ApplyHeaders()
        {
            // Empresa (mandamos ambas por compat)
            if (_http.DefaultRequestHeaders.Contains(HDR_EMPRESA))
                _http.DefaultRequestHeaders.Remove(HDR_EMPRESA);

            var empresa = CurrentEmpresaId();
            _http.DefaultRequestHeaders.Add(HDR_EMPRESA, empresa);

            // Token
            var raw = _accessor.HttpContext?.Session?.GetString("JWT_TOKEN")
                      ?? _accessor.HttpContext?.User?.FindFirst("jwt")?.Value;

            var token = CleanupToken(raw ?? string.Empty);
            var exp = GetJwtExpiry(token);
            if (IsExpiredOrNear(exp))
            {
                // limpiar si está vencido o por vencer
                token = string.Empty;
                try { _accessor.HttpContext?.Session?.Remove("JWT_TOKEN"); } catch { }
            }

            _http.DefaultRequestHeaders.Authorization = null;
            if (!string.IsNullOrWhiteSpace(token))
                _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            // Accept
            _http.DefaultRequestHeaders.Accept.Clear();
            _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }

        private async Task<HttpResponseMessage> GetWithRetryAsync(string url)
        {
            // Primer intento
            ApplyHeaders();
            var resp = await _http.GetAsync(url);

            // Si 401, re-aplicamos headers (por si cambió sesión/empresa/token) y reintentamos 1 vez
            if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                // Fuerza refresco de Authorization leyendo de Session de nuevo
                _http.DefaultRequestHeaders.Authorization = null;
                ApplyHeaders();
                resp = await _http.GetAsync(url);
            }

            return resp;
        }

        // ========= Helpers de lista =========
        private async Task<List<T>> GetListAsync<T>(string url)
        {
            var resp = await GetWithRetryAsync(url);
            if (!resp.IsSuccessStatusCode) return new();
            var body = await resp.Content.ReadAsStringAsync();
            return DeserializeFlexibleList<T>(body);
        }

        // ========= Endpoints usados por el cliente =========
        public Task<List<ContactoDto>> ObtenerContactosAsync()
            => GetListAsync<ContactoDto>("api/general/contacto");

        public Task<List<ConversationSessionDto>> ObtenerConversacionesAsync()
            => GetListAsync<ConversationSessionDto>("api/general/conversacion");

        public Task<List<MessageDto>> ObtenerMensajesAsync()
            => GetListAsync<MessageDto>("api/general/mensaje");

        public async Task<List<UsuarioDto>> GetUsuariosAsync()
        {
            var resp = await GetWithRetryAsync("api/seguridad/usuario");
            if (!resp.IsSuccessStatusCode) return new List<UsuarioDto>();

            var body = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            var items = ExtraerItems(doc.RootElement);
            var list = new List<UsuarioDto>();

            foreach (var e in items)
            {
                var u = new UsuarioDto
                {
                    Id = GetIntFlex(e, "id", "Id", "usuarioId", "userId") ?? 0,
                    Nombre = GetStringFlex(e, "nombre", "Nombre", "name", "Name", "nombreUsuario", "usuario"),
                    Correo = GetStringFlex(e, "correo", "Correo", "email", "Email"),
                    Cedula = GetStringFlex(e, "cedula", "Cedula", "dni"),
                    Telefono = GetStringFlex(e, "telefono", "Telefono", "phone", "Phone"),
                    Estado = GetBoolFlex(e, "estado", "Estado", "activo", "isActive"),
                    IdPerfil = GetIntFlex(e, "idPerfil", "IdPerfil", "perfilId", "PerfilId"),
                    Empresa = GetStringFlex(e, "empresa", "Empresa"),
                    AgentId = GetIntFlex(e, "agentId", "AgentId"),
                    ContactId = GetIntFlex(e, "contactId", "ContactId"),
                    LastLogin = GetDateFlex(e, "lastLogin", "LastLogin", "ultimoAcceso", "UltimoAcceso"),
                    LastActivity = GetDateFlex(e, "lastActivity", "LastActivity", "ultimoMovimiento", "UltimoMovimiento"),
                    IsOnline = GetBoolFlex(e, "isOnline", "online", "Online", "conectado", "Conectado") ?? false,
                    ConversationCount = GetIntFlex(e, "conversationCount", "ConversationCount", "totalConversaciones") ?? 0
                };

                var empId = GetIntFlex(e, "empresaId", "EmpresaId", "empresaID", "EmpresaID");
                if (empId.HasValue) u.EmpresaID = empId.Value;

                if (u.Id != 0 || !string.IsNullOrEmpty(u.Correo) || !string.IsNullOrEmpty(u.Nombre))
                    list.Add(u);
            }

            return list;
        }

        public async Task<UsuarioDto?> GetUsuarioByIdAsync(int id)
        {
            if (id <= 0) return null;
            var resp = await GetWithRetryAsync($"api/seguridad/usuario/{id}");
            if (!resp.IsSuccessStatusCode) return null;
            var body = await resp.Content.ReadAsStringAsync();
            var u = DeserializeFlexibleSingle<UsuarioDto>(body);
            if (u == null)
            {
                using var doc = JsonDocument.Parse(body);
                var item = ExtraerItems(doc.RootElement).FirstOrDefault();
                if (item.ValueKind != JsonValueKind.Undefined)
                    u = MapUsuario(item);
            }
            return u;
        }

        public async Task<List<UsuarioDto>> GetAgentesAsync()
        {
            var resp = await GetWithRetryAsync("api/seguridad/usuario/by-perfil-id/1");
            if (!resp.IsSuccessStatusCode)
            {
                var all = await GetUsuariosAsync();
                return all.Where(u => (u.IdPerfil ?? 0) == 1).ToList();
            }

            var body = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            var items = ExtraerItems(doc.RootElement);
            var list = new List<UsuarioDto>();
            foreach (var e in items)
            {
                var u = MapUsuario(e);
                if (u != null) list.Add(u);
            }
            return list;
        }

        public async Task<ApiResponse<T>?> GetAsync<T>(string url)
        {
            var resp = await GetWithRetryAsync(url);
            if (!resp.IsSuccessStatusCode) return null;
            try
            {
                return await resp.Content.ReadFromJsonAsync<ApiResponse<T>>(_json);
            }
            catch
            {
                return null;
            }
        }

        public async Task<string> GetRawAsync(string url)
        {
            var resp = await GetWithRetryAsync(url);
            var body = await resp.Content.ReadAsStringAsync();
            return $"[{(int)resp.StatusCode}] {url}\n{body}";
        }

        // ========= JSON utils =========
        private static List<T> DeserializeFlexibleList<T>(string body)
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;

                if (root.ValueKind == JsonValueKind.Array)
                    return JsonSerializer.Deserialize<List<T>>(root.GetRawText(), _json) ?? new();

                if (root.ValueKind == JsonValueKind.Object)
                {
                    if (root.TryGetProperty("$values", out var rValues) && rValues.ValueKind == JsonValueKind.Array)
                        return JsonSerializer.Deserialize<List<T>>(rValues.GetRawText(), _json) ?? new();

                    if (root.TryGetProperty("data", out var data))
                    {
                        if (data.ValueKind == JsonValueKind.Array)
                            return JsonSerializer.Deserialize<List<T>>(data.GetRawText(), _json) ?? new();

                        if (data.ValueKind == JsonValueKind.Object)
                        {
                            if (data.TryGetProperty("$values", out var dValues) && dValues.ValueKind == JsonValueKind.Array)
                                return JsonSerializer.Deserialize<List<T>>(dValues.GetRawText(), _json) ?? new();

                            foreach (var key in new[] { "values", "Values", "items", "Items", "list", "List" })
                                if (data.TryGetProperty(key, out var node) && node.ValueKind == JsonValueKind.Array)
                                    return JsonSerializer.Deserialize<List<T>>(node.GetRawText(), _json) ?? new();
                        }
                    }

                    foreach (var key in new[] { "values", "Values", "items", "Items", "list", "List" })
                        if (root.TryGetProperty(key, out var node) && node.ValueKind == JsonValueKind.Array)
                            return JsonSerializer.Deserialize<List<T>>(node.GetRawText(), _json) ?? new();
                }
            }
            catch { }
            return new();
        }

        private static T? DeserializeFlexibleSingle<T>(string body)
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;

                if (root.ValueKind == JsonValueKind.Object)
                {
                    if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
                        return JsonSerializer.Deserialize<T>(data.GetRawText(), _json);

                    if (root.TryGetProperty("objeto", out var obj) && obj.ValueKind == JsonValueKind.Object)
                        return JsonSerializer.Deserialize<T>(obj.GetRawText(), _json);

                    return JsonSerializer.Deserialize<T>(root.GetRawText(), _json);
                }
            }
            catch { }
            return default;
        }

        private static IEnumerable<JsonElement> ExtraerItems(JsonElement root)
        {
            if (TryGetCaseInsensitive(root, "data", out var data))
            {
                if (TryGetCaseInsensitive(data, "$values", out var values) && values.ValueKind == JsonValueKind.Array)
                    return values.EnumerateArray().ToArray();
                if (data.ValueKind == JsonValueKind.Array) return data.EnumerateArray().ToArray();
            }
            if (TryGetCaseInsensitive(root, "$values", out var rvalues) && rvalues.ValueKind == JsonValueKind.Array)
                return rvalues.EnumerateArray().ToArray();
            if (root.ValueKind == JsonValueKind.Array) return root.EnumerateArray().ToArray();
            foreach (var p in root.EnumerateObject())
                if (p.Value.ValueKind == JsonValueKind.Array) return p.Value.EnumerateArray().ToArray();
            return Array.Empty<JsonElement>();
        }

        private static bool TryGetCaseInsensitive(JsonElement obj, string name, out JsonElement value)
        {
            if (obj.ValueKind != JsonValueKind.Object) { value = default; return false; }
            foreach (var p in obj.EnumerateObject())
                if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) { value = p.Value; return true; }
            value = default; return false;
        }

        private static string? GetStringFlex(JsonElement obj, params string[] names)
        {
            foreach (var n in names)
                if (TryGetCaseInsensitive(obj, n, out var v) && v.ValueKind == JsonValueKind.String) return v.GetString();
            return null;
        }

        private static int? GetIntFlex(JsonElement obj, params string[] names)
        {
            foreach (var n in names)
            {
                if (TryGetCaseInsensitive(obj, n, out var v))
                {
                    if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i)) return i;
                    if (v.ValueKind == JsonValueKind.String && int.TryParse(v.GetString(), out i)) return i;
                }
            }
            return null;
        }

        private static bool? GetBoolFlex(JsonElement obj, params string[] names)
        {
            foreach (var n in names)
            {
                if (TryGetCaseInsensitive(obj, n, out var v))
                {
                    if (v.ValueKind == JsonValueKind.True) return true;
                    if (v.ValueKind == JsonValueKind.False) return false;
                    if (v.ValueKind == JsonValueKind.String && bool.TryParse(v.GetString(), out var b)) return b;
                    if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i)) return i != 0;
                }
            }
            return null;
        }

        private static DateTime? GetDateFlex(JsonElement obj, params string[] names)
        {
            foreach (var n in names)
            {
                if (TryGetCaseInsensitive(obj, n, out var v))
                {
                    if (v.ValueKind == JsonValueKind.String && DateTime.TryParse(v.GetString(), out var dt)) return dt;
                    if (v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var epoch))
                        return DateTimeOffset.FromUnixTimeSeconds(epoch).UtcDateTime;
                }
            }
            return null;
        }

        private static UsuarioDto? MapUsuario(JsonElement e)
        {
            var u = new UsuarioDto
            {
                Id = GetIntFlex(e, "id", "Id", "usuarioId", "userId") ?? 0,
                Nombre = GetStringFlex(e, "nombre", "Nombre", "name", "Name", "nombreUsuario", "usuario"),
                Correo = GetStringFlex(e, "correo", "Correo", "email", "Email"),
                Cedula = GetStringFlex(e, "cedula", "Cedula", "dni"),
                Telefono = GetStringFlex(e, "telefono", "Telefono", "phone", "Phone"),
                Estado = GetBoolFlex(e, "estado", "Estado", "activo", "isActive"),
                IdPerfil = GetIntFlex(e, "idPerfil", "IdPerfil", "perfilId", "PerfilId"),
                Empresa = GetStringFlex(e, "empresa", "Empresa"),
                AgentId = GetIntFlex(e, "agentId", "AgentId"),
                ContactId = GetIntFlex(e, "contactId", "ContactId"),
                LastLogin = GetDateFlex(e, "lastLogin", "LastLogin", "ultimoAcceso", "UltimoAcceso"),
                LastActivity = GetDateFlex(e, "lastActivity", "LastActivity", "ultimoMovimiento", "UltimoMovimiento"),
                IsOnline = GetBoolFlex(e, "isOnline", "online", "Online", "conectado", "Conectado") ?? false,
                ConversationCount = GetIntFlex(e, "conversationCount", "ConversationCount", "totalConversaciones") ?? 0
            };
            var empId = GetIntFlex(e, "empresaId", "EmpresaId", "empresaID", "EmpresaID");
            if (empId.HasValue) u.EmpresaID = empId.Value;
            if (u.Id == 0 && string.IsNullOrWhiteSpace(u.Correo) && string.IsNullOrWhiteSpace(u.Nombre)) return null;
            return u;
        }

        public async Task<bool> UpdateNombreAgenteAsync(int idUsuario, string nombre)
        {
            HttpRequestMessage BuildReq() => new HttpRequestMessage(HttpMethod.Patch, $"api/seguridad/usuario/{idUsuario}/nombre")
            {
                Content = JsonContent.Create(new { Nombre = nombre })
            };

            ApplyHeaders();

            using var req = BuildReq();
            using var resp = await _http.SendAsync(req);

            if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                _http.DefaultRequestHeaders.Authorization = null;
                ApplyHeaders();

                using var req2 = BuildReq(); 
                using var resp2 = await _http.SendAsync(req2);
                return resp2.IsSuccessStatusCode;
            }

            return resp.IsSuccessStatusCode;
        }

        // WebApp: Services/ApiService.cs
        public async Task<bool> UpdateNombreContactoAsync(int idContacto, string nombre)
        {
            HttpRequestMessage BuildReq() => new HttpRequestMessage(
                HttpMethod.Patch, $"api/general/contacto/{idContacto}/nombre")
            {
                Content = JsonContent.Create(new { Name = nombre })
            };

            ApplyHeaders();

            using var req = BuildReq();
            using var resp = await _http.SendAsync(req);

            if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                _http.DefaultRequestHeaders.Authorization = null;
                ApplyHeaders();
                using var req2 = BuildReq();
                using var resp2 = await _http.SendAsync(req2);
                return resp2.IsSuccessStatusCode;
            }

            return resp.IsSuccessStatusCode;
        }


    }
}
