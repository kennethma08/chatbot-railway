using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;

namespace WhatsappClient.Controllers
{
    public class UpdateConversationStatusDto
    {
        public int ConversationId { get; set; }
        public string Status { get; set; } = "open";
        public int? ContactId { get; set; }
        public DateTime? StartedAt { get; set; }
    }

    [Authorize]
    [Route("Chat/[action]")]
    public class ChatController : Controller
    {
        private readonly IConfiguration _cfg;
        private readonly IHttpClientFactory _httpFactory;

        private static readonly ConcurrentDictionary<int, CancellationTokenSource> _autoClose = new();
        private static readonly TimeSpan AUTO_CLOSE_AFTER = TimeSpan.FromHours(23);

        public ChatController(IConfiguration cfg, IHttpClientFactory httpFactory)
        {
            _cfg = cfg;
            _httpFactory = httpFactory;
        }

        [HttpGet]
        [Route("", Name = "ChatRoot")]
        public IActionResult Index() => View();

        [HttpGet]
        public async Task<IActionResult> GetContactConversations(string phone)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(phone))
                    return Ok(new { conversations = Array.Empty<object>(), error = "phone requerido" });

                var (http, ok, reason) = CreateApiClient();
                if (!ok) return Ok(new { conversations = Array.Empty<object>(), error = reason });

                var phoneDigits = SoloDigitos(phone);

                var resContact = await http.GetAsync("api/general/contacto");
                var contactsRaw = await resContact.Content.ReadAsStringAsync();
                if (!resContact.IsSuccessStatusCode)
                    return Ok(new { conversations = Array.Empty<object>(), error = "No se pudo obtener contactos" });

                using var docC = JsonDocument.Parse(contactsRaw);
                var contactos = ExtraerItems(docC.RootElement)
                    .Select(e => new
                    {
                        Id = GetIntFlex(e, "id", "Id") ?? 0,
                        Name = GetStringFlex(e, "name", "Name", "nombre", "Nombre", "full_name", "FullName"),
                        Phone = GetStringFlex(e, "phone_number", "phoneNumber", "PhoneNumber")
                    })
                    .Select(x => new { x.Id, x.Name, x.Phone, PhoneDigits = SoloDigitos(x.Phone) })
                    .ToList();

                var contact = contactos.FirstOrDefault(c => c.PhoneDigits == phoneDigits);
                if (contact == null || contact.Id <= 0)
                    return Ok(new { conversations = Array.Empty<object>() });

                var resConv = await http.GetAsync("api/general/conversacion");
                var convRaw = await resConv.Content.ReadAsStringAsync();
                if (!resConv.IsSuccessStatusCode)
                    return Ok(new { conversations = Array.Empty<object>(), error = "No se pudo obtener conversaciones" });

                using var docConv = JsonDocument.Parse(convRaw);
                var convs = ExtraerItems(docConv.RootElement)
                    .Where(e => (GetIntFlex(e, "contact_id", "ContactId") ?? 0) == contact.Id)
                    .Select(e => new
                    {
                        id = GetIntFlex(e, "id", "Id") ?? 0,
                        contactId = contact.Id,
                        contactName = contact.Name,
                        contactPhone = contact.Phone,
                        status = GetStringFlex(e, "status", "Status") ?? "open",
                        startedAt = GetDateFlex(e, "started_at", "StartedAt"),
                        lastActivityAt = GetDateFlex(e, "last_activity_at", "LastActivityAt"),
                        totalMessages = GetIntFlex(e, "total_messages", "TotalMessages") ?? 0,
                        greetingSent = GetBoolFlex(e, "greeting_sent", "Greeting_Sent", "GreetingSent") ?? false,
                        agentRequestedAt = GetDateFlex(e, "agent_requested_at", "AgentRequestedAt")
                    })
                    .Where(x => x.agentRequestedAt != null)
                    .OrderByDescending(x => x.lastActivityAt ?? x.startedAt)
                    .ToList();

                return Ok(new { conversations = convs });
            }
            catch (Exception ex)
            {
                return Ok(new { conversations = Array.Empty<object>(), error = ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetConversationMessages(int conversationId)
        {
            try
            {
                if (conversationId <= 0)
                    return Ok(new { messages = Array.Empty<object>(), error = "conversationId inválido" });

                var (http, ok, reason) = CreateApiClient();
                if (!ok) return Ok(new { messages = Array.Empty<object>(), error = reason });

                var resMsg = await http.GetAsync("api/general/mensaje");
                var msgRaw = await resMsg.Content.ReadAsStringAsync();
                if (!resMsg.IsSuccessStatusCode)
                    return Ok(new { messages = Array.Empty<object>(), error = "No se pudo obtener mensajes" });

                using var doc = JsonDocument.Parse(msgRaw);
                var msgs = ExtraerItems(doc.RootElement)
                    .Where(e => (GetIntFlex(e, "conversation_id", "ConversationId") ?? 0) == conversationId)
                    .Select(e => new
                    {
                        id = GetIntFlex(e, "id", "Id") ?? 0,
                        sender = GetStringFlex(e, "sender", "Sender") ?? "contact",
                        message = GetStringFlex(e, "message", "Message") ?? "",
                        type = GetStringFlex(e, "type", "Type") ?? "text",
                        sentAt = GetDateFlex(e, "sent_at", "SentAt")
                    })
                    .OrderBy(x => x.sentAt)
                    .ToList();

                return Ok(new { messages = msgs });
            }
            catch (Exception ex)
            {
                return Ok(new { messages = Array.Empty<object>(), error = ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetAllConversations()
        {
            try
            {
                var (http, ok, reason) = CreateApiClient();
                if (!ok) return Ok(new { conversations = Array.Empty<object>(), error = reason });

                var resConv = await http.GetAsync("api/general/conversacion");
                var convRaw = await resConv.Content.ReadAsStringAsync();
                if (!resConv.IsSuccessStatusCode)
                    return Ok(new { conversations = Array.Empty<object>(), error = "No se pudo obtener conversaciones" });

                using var docConv = JsonDocument.Parse(convRaw);
                var convsRaw = ExtraerItems(docConv.RootElement)
                    .Select(e => new
                    {
                        id = GetIntFlex(e, "id", "Id") ?? 0,
                        contactId = GetIntFlex(e, "contact_id", "ContactId") ?? 0,
                        status = GetStringFlex(e, "status", "Status") ?? "open",
                        startedAt = GetDateFlex(e, "started_at", "StartedAt"),
                        lastActivityAt = GetDateFlex(e, "last_activity_at", "LastActivityAt"),
                        totalMessages = GetIntFlex(e, "total_messages", "TotalMessages") ?? 0,
                        greetingSent = GetBoolFlex(e, "greeting_sent", "Greeting_Sent", "GreetingSent") ?? false,
                        agentRequestedAt = GetDateFlex(e, "agent_requested_at", "AgentRequestedAt")
                    })
                    .ToList();

                var infoByContact = new Dictionary<int, (string? Name, string? Phone)>();
                var resContact = await http.GetAsync("api/general/contacto");
                if (resContact.IsSuccessStatusCode)
                {
                    using var docC = JsonDocument.Parse(await resContact.Content.ReadAsStringAsync());
                    foreach (var c in ExtraerItems(docC.RootElement))
                    {
                        var id = GetIntFlex(c, "id", "Id") ?? 0;
                        if (id <= 0) continue;
                        var name = GetStringFlex(c, "name", "Name", "nombre", "Nombre", "full_name", "FullName");
                        var phone = GetStringFlex(c, "phone_number", "phoneNumber", "PhoneNumber");
                        infoByContact[id] = (name, phone);
                    }
                }

                var convs = convsRaw
                    .Where(x => x.agentRequestedAt != null)
                    .Select(x =>
                    {
                        infoByContact.TryGetValue(x.contactId, out var info);
                        return new
                        {
                            id = x.id,
                            contactId = x.contactId,
                            contactName = info.Name,
                            contactPhone = info.Phone,
                            status = x.status,
                            startedAt = x.startedAt,
                            lastActivityAt = x.lastActivityAt,
                            totalMessages = x.totalMessages,
                            agentRequestedAt = x.agentRequestedAt
                        };
                    })
                    .OrderByDescending(x => x.lastActivityAt ?? x.startedAt)
                    .ToList();

                return Ok(new { conversations = convs });
            }
            catch (Exception ex)
            {
                return Ok(new { conversations = Array.Empty<object>(), error = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> SendMessage([FromBody] JsonElement body)
        {
            try
            {
                if (!body.TryGetProperty("conversationId", out var convEl) || convEl.ValueKind != JsonValueKind.Number)
                    return Ok(new { success = false, error = "conversationId requerido" });

                var conversationId = convEl.GetInt32();
                var contactId = body.TryGetProperty("contactId", out var cidEl) && cidEl.ValueKind == JsonValueKind.Number ? cidEl.GetInt32() : 0;
                var contactPhone = body.TryGetProperty("contactPhone", out var phEl) && phEl.ValueKind == JsonValueKind.String ? phEl.GetString() : null;
                var message = body.TryGetProperty("message", out var msgEl) && msgEl.ValueKind == JsonValueKind.String ? msgEl.GetString() : null;

                if (string.IsNullOrWhiteSpace(message))
                    return Ok(new { success = false, error = "message requerido" });

                var (http, ok, reason) = CreateApiClient();
                if (!ok) return Ok(new { success = false, error = reason });

                string? status = null;
                if (conversationId > 0)
                {
                    var resConv = await http.GetAsync("api/general/conversacion");
                    if (resConv.IsSuccessStatusCode)
                    {
                        using var jd = JsonDocument.Parse(await resConv.Content.ReadAsStringAsync());
                        foreach (var el in ExtraerItems(jd.RootElement))
                        {
                            var id = GetIntFlex(el, "id", "Id") ?? 0;
                            if (id == conversationId)
                            {
                                status = GetStringFlex(el, "status", "Status");
                                break;
                            }
                        }
                    }
                }
                if (!string.Equals(status, "open", StringComparison.OrdinalIgnoreCase))
                    return Ok(new { success = false, error = "La conversación está cerrada. No se puede enviar." });

                if (string.IsNullOrWhiteSpace(contactPhone) && contactId > 0)
                {
                    var resC = await http.GetAsync("api/general/contacto");
                    if (resC.IsSuccessStatusCode)
                    {
                        using var docC = JsonDocument.Parse(await resC.Content.ReadAsStringAsync());
                        foreach (var c in ExtraerItems(docC.RootElement))
                        {
                            var id = GetIntFlex(c, "id", "Id") ?? 0;
                            if (id == contactId)
                            {
                                contactPhone = GetStringFlex(c, "phone_number", "phoneNumber", "PhoneNumber");
                                break;
                            }
                        }
                    }
                }
                if (string.IsNullOrWhiteSpace(contactPhone))
                    return Ok(new { success = false, error = "No se pudo resolver el teléfono del contacto" });

                var apiRes = await SendTextViaApiAsync(http, new
                {
                    Contact_Id = contactId > 0 ? (int?)contactId : null,
                    Conversation_Id = conversationId,
                    To_Phone = contactPhone,
                    Text = message,
                    Create_If_Not_Exists = false,
                    Log = true
                });

                if (!apiRes.success)
                    return Ok(new { success = false, error = apiRes.error });

                return Ok(new { success = true, conversationId = apiRes.conversationId, justCreated = apiRes.justCreated });
            }
            catch (Exception ex)
            {
                return Ok(new { success = false, error = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> UpdateConversationStatus([FromBody] UpdateConversationStatusDto req)
        {
            try
            {
                if (req == null || req.ConversationId <= 0 || string.IsNullOrWhiteSpace(req.Status))
                    return Ok(new { success = false, error = "payload inválido" });

                if (req.Status.Equals("open", StringComparison.OrdinalIgnoreCase))
                    return Ok(new { success = false, error = "No se permite reabrir conversaciones cerradas." });

                var (http, ok, reason) = CreateApiClient();
                if (!ok) return Ok(new { success = false, error = reason });

                int? contactId = null;
                DateTime? startedAt = null;
                string? contactPhone = null;
                string? currentStatus = null;

                var resConv = await http.GetAsync("api/general/conversacion");
                if (resConv.IsSuccessStatusCode)
                {
                    var txt = await resConv.Content.ReadAsStringAsync();
                    using var jd = JsonDocument.Parse(txt);
                    foreach (var el in ExtraerItems(jd.RootElement))
                    {
                        var id = GetIntFlex(el, "id", "Id") ?? 0;
                        if (id == req.ConversationId)
                        {
                            contactId = GetIntFlex(el, "contact_id", "ContactId");
                            startedAt = GetDateFlex(el, "started_at", "StartedAt");
                            currentStatus = GetStringFlex(el, "status", "Status") ?? "open";
                            break;
                        }
                    }
                }
                if ((currentStatus ?? "open").Equals("closed", StringComparison.OrdinalIgnoreCase))
                    return Ok(new { success = false, error = "La conversación ya está cerrada." });

                if (contactId.HasValue && contactId.Value > 0)
                {
                    var resContact = await http.GetAsync("api/general/contacto");
                    if (resContact.IsSuccessStatusCode)
                    {
                        var txt = await resContact.Content.ReadAsStringAsync();
                        using var jd = JsonDocument.Parse(txt);
                        foreach (var el in ExtraerItems(jd.RootElement))
                        {
                            var id = GetIntFlex(el, "id", "Id") ?? 0;
                            if (id == contactId.Value)
                            {
                                contactPhone = GetStringFlex(el, "phone_number", "phoneNumber", "PhoneNumber");
                                break;
                            }
                        }
                    }
                }

                var payload = new
                {
                    Id = req.ConversationId,
                    Contact_Id = contactId ?? req.ContactId,
                    Started_At = startedAt ?? req.StartedAt,
                    Status = "closed",
                    Last_Activity_At = DateTime.UtcNow
                };
                await PostApiSnakeAsync(http, "api/general/conversacion/upsert", payload);

                if (!string.IsNullOrWhiteSpace(contactPhone))
                {
                    await SendTextViaApiAsync(http, new
                    {
                        Contact_Id = contactId,
                        Conversation_Id = req.ConversationId,
                        To_Phone = contactPhone,
                        Text = "Tu ticket ha sido cerrado. Si necesitas más ayuda, por favor crea un nuevo ticket respondiendo a este chat.",
                        Create_If_Not_Exists = false,
                        Log = true
                    });
                }

                if (_autoClose.TryRemove(req.ConversationId, out var cts))
                {
                    try { cts.Cancel(); cts.Dispose(); } catch { }
                }

                return Ok(new { success = true });
            }
            catch (Exception ex)
            {
                return Ok(new { success = false, error = ex.Message });
            }
        }

        private (HttpClient http, bool ok, string reason) CreateApiClient()
        {
            var apiBase = _cfg["Api:BaseUrl"]?.TrimEnd('/');
            if (string.IsNullOrWhiteSpace(apiBase)) return (null!, false, "Api:BaseUrl vacío");

            var http = _httpFactory.CreateClient();
            http.BaseAddress = new Uri(apiBase + "/");

            var empresaId = ResolveEmpresaId();
            http.DefaultRequestHeaders.Remove("X-Empresa-Id");
            http.DefaultRequestHeaders.Add("X-Empresa-Id", empresaId);

            var rawToken =
                   HttpContext.Session.GetString("JWT_TOKEN")
                ?? User?.FindFirst("jwt")?.Value
                ?? Request.Cookies["JWT_TOKEN"];

            var token = CleanupToken(rawToken);

            http.DefaultRequestHeaders.Authorization = string.IsNullOrWhiteSpace(token)
                ? null
                : new AuthenticationHeaderValue("Bearer", token);

            http.DefaultRequestHeaders.Accept.Clear();
            http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            var authShown = string.IsNullOrWhiteSpace(token)
                ? "(none)"
                : ("Bearer " + token.Substring(0, Math.Min(10, token.Length)) + "...");
            Console.WriteLine($"[Chat/CreateApiClient] Base={http.BaseAddress} X-Empresa-Id={empresaId} Auth={authShown}");
            return (http, true, "");
        }

        private static string CleanupToken(string? t)
        {
            if (string.IsNullOrWhiteSpace(t)) return string.Empty;
            var s = t.Trim();
            if (s.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) s = s.Substring(7).Trim();
            if (s.StartsWith("\"") && s.EndsWith("\"")) s = s.Trim('\"');
            return s;
        }

        private string ResolveEmpresaId()
        {
            var empSession = HttpContext.Session.GetString("EMPRESA_ID");
            if (!string.IsNullOrWhiteSpace(empSession) && empSession != "0")
                return empSession;

            var empClaim = User?.FindFirst("empresa_id")?.Value
                        ?? User?.FindFirst("EmpresaId")?.Value;
            if (!string.IsNullOrWhiteSpace(empClaim) && empClaim != "0")
                return empClaim;

            var rawJwt = HttpContext.Session.GetString("JWT_TOKEN");
            var empFromJwt = TryGetClaimFromJwt(rawJwt, "empresa_id");
            if (!string.IsNullOrWhiteSpace(empFromJwt) && empFromJwt != "0")
                return empFromJwt;

            return _cfg["Api:EmpresaId"] ?? "0";
        }

        private static string? TryGetClaimFromJwt(string? jwt, string claimName)
        {
            if (string.IsNullOrWhiteSpace(jwt)) return null;
            try
            {
                var parts = jwt.Split('.');
                if (parts.Length < 2) return null;
                var payload = parts[1];
                var json = Encoding.UTF8.GetString(Base64UrlDecode(payload));
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty(claimName, out var val) && val.ValueKind == JsonValueKind.String)
                    return val.GetString();
                return null;
            }
            catch { return null; }
        }

        private static byte[] Base64UrlDecode(string input)
        {
            string s = input.Replace('-', '+').Replace('_', '/');
            switch (s.Length % 4)
            {
                case 2: s += "=="; break;
                case 3: s += "="; break;
            }
            return Convert.FromBase64String(s);
        }

        private static async Task<(bool success, string? error, int? conversationId, bool justCreated)> SendTextViaApiAsync(HttpClient http, object payload)
        {
            var res = await http.PostAsJsonAsync("api/integraciones/whatsapp/send/text", payload);
            var body = await res.Content.ReadAsStringAsync();

            try
            {
                using var jd = JsonDocument.Parse(body);
                var root = jd.RootElement;
                bool ok = root.TryGetProperty("exitoso", out var ex) && ex.ValueKind == JsonValueKind.True;
                if (!res.IsSuccessStatusCode || !ok)
                {
                    var msg = root.TryGetProperty("mensaje", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() : body;
                    return (false, $"API {(int)res.StatusCode}: {msg}", null, false);
                }

                int? convId = null;
                bool just = false;
                if (root.TryGetProperty("conversacion_id", out var idEl) && idEl.ValueKind == JsonValueKind.Number) convId = idEl.GetInt32();
                if (root.TryGetProperty("just_created", out var jcEl) && (jcEl.ValueKind == JsonValueKind.True || jcEl.ValueKind == JsonValueKind.False)) just = jcEl.GetBoolean();

                return (true, null, convId, just);
            }
            catch
            {
                if (!res.IsSuccessStatusCode) return (false, $"API {(int)res.StatusCode}: {body}", null, false);
                return (true, null, null, false);
            }
        }

        private static async Task PostApiSnakeAsync(HttpClient http, string relativeEndpoint, object body)
        {
            var json = JsonSerializer.Serialize(body, new JsonSerializerOptions { PropertyNamingPolicy = null });
            await http.PostAsync(relativeEndpoint, new StringContent(json, Encoding.UTF8, "application/json"));
        }

        private static IEnumerable<JsonElement> ExtraerItems(JsonElement root)
        {
            if (TryGetCaseInsensitive(root, "data", out var data))
            {
                if (TryGetCaseInsensitive(data, "$values", out var values) && values.ValueKind == JsonValueKind.Array)
                    return values.EnumerateArray().ToArray();
                if (data.ValueKind == JsonValueKind.Array) return data.EnumerateArray().ToArray();
            }
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

        private static string SoloDigitos(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "";
            var sb = new StringBuilder(s.Length);
            foreach (var ch in s) if (char.IsDigit(ch)) sb.Append(ch);
            return sb.ToString();
        }
    }
}
