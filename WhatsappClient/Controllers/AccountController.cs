using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using System.Text;
using System.Text.Json;

namespace WhatsappClient.Controllers
{
    public class AccountController : Controller
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IHttpContextAccessor _accessor;
        private readonly IConfiguration _cfg;

        public AccountController(IHttpClientFactory httpClientFactory, IHttpContextAccessor accessor, IConfiguration cfg)
        {
            _httpClientFactory = httpClientFactory;
            _accessor = accessor;
            _cfg = cfg;
        }

        [AllowAnonymous]
        [HttpGet]
        public IActionResult Login(string? returnUrl = null)
        {
            ViewData["ReturnUrl"] = returnUrl;
            return View(new LoginVm("", ""));
        }

        public record LoginVm(string Email, string Password);

        [AllowAnonymous]
        [HttpPost]
        public async Task<IActionResult> Login(LoginVm vm, string? returnUrl = null)
        {
            if (string.IsNullOrWhiteSpace(vm.Email) || string.IsNullOrWhiteSpace(vm.Password))
            {
                ModelState.AddModelError("", "Usuario y contraseña son obligatorios.");
                return View(vm);
            }

            var client = _httpClientFactory.CreateClient(nameof(AccountController));
            var baseUrl = _cfg["Api:BaseUrl"] ?? "https://localhost:7097/";
            if (client.BaseAddress == null) client.BaseAddress = new Uri(baseUrl);

            // sin headers de empresa/token en el login
            client.DefaultRequestHeaders.Remove("X-Empresa-Id");

            var body = new { nombreUsuario = vm.Email, contrasenia = vm.Password, loginApp = true };
            var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

            HttpResponseMessage resp;
            try
            {
                resp = await client.PostAsync("api/auth/login", content);
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", $"No se pudo contactar la API: {ex.Message}");
                return View(vm);
            }

            if (!resp.IsSuccessStatusCode)
            {
                var reason = await resp.Content.ReadAsStringAsync();
                ModelState.AddModelError("", $"Credenciales inválidas o error en API. ({(int)resp.StatusCode}) {reason}");
                return View(vm);
            }

            var json = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var tokenRaw = FindToken(root);
            var token = CleanupToken(tokenRaw);
            if (string.IsNullOrWhiteSpace(token))
            {
                ModelState.AddModelError("", "La API no devolvió un token válido.");
                return View(vm);
            }

            var userEl = FindUser(root);

            string id = GetStringCI(userEl, "id", "Id", "usuarioId", "userId") ?? "";
            string nombre = GetStringCI(userEl, "nombre", "Nombre", "name", "Name", "nombreUsuario", "usuario") ?? "";
            string correo = GetStringCI(userEl, "correo", "Correo", "email", "Email") ?? "";

            string rawRole = GetStringCI(userEl, "role", "Role", "perfil", "Perfil") ?? "";
            int? idPerfil = GetIntCI(userEl, "idPerfil", "IdPerfil", "perfilId", "PerfilId");
            if (string.IsNullOrWhiteSpace(rawRole) && idPerfil.HasValue)
                rawRole = idPerfil.Value switch { 3 => "SuperAdmin", 2 => "Admin", 1 => "Agente", _ => "" };
            var role = NormalizarRol(rawRole);

            // empresa_id: primero del JSON; si no viene, lo sacamos del JWT (claim "empresa_id")
            int empresaId = GetIntCI(userEl, "empresaId", "EmpresaId", "empresa_id", "EmpresaID") ?? 0;
            if (empresaId <= 0) empresaId = TryGetEmpresaIdFromJwt(token) ?? 0;

            string empresaNombre = GetStringCI(userEl, "empresa", "Empresa") ?? "";

            // guardar en Session
            _accessor.HttpContext!.Session.SetString("JWT_TOKEN", token);
            _accessor.HttpContext!.Session.SetString("EMPRESA_ID", empresaId > 0 ? empresaId.ToString() : "");
            _accessor.HttpContext!.Session.SetString("EMPRESA_NOMBRE", empresaNombre ?? "");

            // cookie auth para la Web App
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, id),
                new Claim(ClaimTypes.Name, nombre),
                new Claim(ClaimTypes.Email, correo),
                new Claim(ClaimTypes.Role, role),
                new Claim("role", role),
                new Claim("empresa_id", empresaId.ToString()),
                new Claim("empresa", empresaNombre ?? ""),
                new Claim("jwt", token)
            };

            var identity = new ClaimsIdentity(
                claims,
                CookieAuthenticationDefaults.AuthenticationScheme,
                ClaimTypes.Name,
                ClaimTypes.Role
            );

            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(identity),
                new AuthenticationProperties
                {
                    IsPersistent = true,
                    AllowRefresh = true,
                    ExpiresUtc = DateTimeOffset.UtcNow.AddHours(2)
                });

            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                return Redirect(returnUrl);

            return RedirectToAction("Index", "Dashboard");
        }

        private static string NormalizarRol(string? raw)
        {
            var s = (raw ?? "").Trim();
            var flat = s.Replace(" ", "", StringComparison.OrdinalIgnoreCase);
            if (flat.Equals("superadmin", StringComparison.OrdinalIgnoreCase)) return "SuperAdmin";
            if (flat.Equals("admin", StringComparison.OrdinalIgnoreCase) || flat.Equals("administrador", StringComparison.OrdinalIgnoreCase)) return "Admin";
            if (flat.Equals("agente", StringComparison.OrdinalIgnoreCase) || flat.Equals("agent", StringComparison.OrdinalIgnoreCase)) return "Agente";
            return "Usuario";
        }

        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            _accessor.HttpContext!.Session.Remove("JWT_TOKEN");
            _accessor.HttpContext!.Session.Remove("EMPRESA_ID");
            _accessor.HttpContext!.Session.Remove("EMPRESA_NOMBRE");
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("Login");
        }

        [AllowAnonymous]
        public IActionResult Denied() => Content("Acceso denegado.");

        private static string CleanupToken(string? t)
        {
            if (string.IsNullOrWhiteSpace(t)) return "";
            var s = t.Trim();
            if (s.StartsWith("\"") && s.EndsWith("\"")) s = s.Trim('"');
            if (s.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) s = s.Substring(7).Trim();
            return s;
        }

        private static string? FindToken(JsonElement root)
        {
            if (TryGetCI(root, "token", out var v) && v.ValueKind == JsonValueKind.String) return v.GetString();

            foreach (var key in new[] { "data", "Data", "objeto", "Objeto", "result", "Result" })
                if (TryGetCI(root, key, out var node) && node.ValueKind == JsonValueKind.Object)
                    if (TryGetCI(node, "token", out var t) && t.ValueKind == JsonValueKind.String) return t.GetString();

            foreach (var p in root.EnumerateObject())
                if (p.Value.ValueKind == JsonValueKind.Object)
                    if (TryGetCI(p.Value, "token", out var t2) && t2.ValueKind == JsonValueKind.String) return t2.GetString();

            return null;
        }

        private static JsonElement FindUser(JsonElement root)
        {
            if (TryGetCI(root, "user", out var u) && u.ValueKind == JsonValueKind.Object) return u;
            if (TryGetCI(root, "usuario", out var u2) && u2.ValueKind == JsonValueKind.Object) return u2;

            foreach (var key in new[] { "data", "Data", "objeto", "Objeto", "result", "Result" })
                if (TryGetCI(root, key, out var node) && node.ValueKind == JsonValueKind.Object)
                {
                    if (TryGetCI(node, "user", out var d1) && d1.ValueKind == JsonValueKind.Object) return d1;
                    if (TryGetCI(node, "usuario", out var d2) && d2.ValueKind == JsonValueKind.Object) return d2;
                }

            return root; // campos planos
        }

        private static bool TryGetCI(JsonElement obj, string name, out JsonElement value)
        {
            if (obj.ValueKind == JsonValueKind.Object)
            {
                foreach (var p in obj.EnumerateObject())
                    if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
                    { value = p.Value; return true; }
            }
            value = default; return false;
        }

        private static string? GetStringCI(JsonElement obj, params string[] names)
        {
            foreach (var n in names)
                if (TryGetCI(obj, n, out var v) && v.ValueKind == JsonValueKind.String) return v.GetString();
            return null;
        }

        private static int? GetIntCI(JsonElement obj, params string[] names)
        {
            foreach (var n in names)
            {
                if (TryGetCI(obj, n, out var v))
                {
                    if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i)) return i;
                    if (v.ValueKind == JsonValueKind.String && int.TryParse(v.GetString(), out var i2)) return i2;
                }
            }
            return null;
        }

        // === Fallback: extraer empresa_id del JWT (claim "empresa_id") ===
        private static int? TryGetEmpresaIdFromJwt(string jwt)
        {
            try
            {
                var parts = jwt.Split('.');
                if (parts.Length < 2) return null;
                string payload = parts[1]
                    .Replace('-', '+')
                    .Replace('_', '/');
                switch (payload.Length % 4)
                {
                    case 2: payload += "=="; break;
                    case 3: payload += "="; break;
                }
                var json = Encoding.UTF8.GetString(Convert.FromBase64String(payload));
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                // nombres usuales para el claim
                foreach (var k in new[] { "empresa_id", "EmpresaId", "empresaId" })
                {
                    if (root.TryGetProperty(k, out var v))
                    {
                        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i)) return i;
                        if (v.ValueKind == JsonValueKind.String && int.TryParse(v.GetString(), out var s)) return s;
                    }
                }
            }
            catch { }
            return null;
        }
    }
}
