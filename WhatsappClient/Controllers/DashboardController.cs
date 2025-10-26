using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Globalization;
using WhatsappClient.Models;
using WhatsappClient.Services;

namespace WhatsappClient.Controllers
{
    [Authorize]
    public class DashboardController : Controller
    {
        private readonly ApiService _api;
        public DashboardController(ApiService api) => _api = api;

        public async Task<IActionResult> Index()
        {
            // ===== Cargar datos desde la API (listas ya compatibles con data.$values) =====
            var contactos = await _api.ObtenerContactosAsync();                // ContactoDto
            var conversaciones = await _api.ObtenerConversacionesAsync();           // ConversationSessionDto
            var mensajes = await _api.ObtenerMensajesAsync();                 // MessageDto

            contactos ??= new List<ContactoDto>();
            conversaciones ??= new List<ConversationSessionDto>();
            mensajes ??= new List<MessageDto>();

            var nowUtc = DateTime.UtcNow;
            var nuevosDesde = nowUtc.AddDays(-1);

            // ===== KPIs =====
            int totalConversaciones = conversaciones.Count;

            int clientesNuevos = contactos.Count(c =>
            {
                var createdAt = GetDate(c, "CreatedAt", "FechaCreacion", "Created", "CreatedDate");
                return createdAt.HasValue && NormalizeToUtc(createdAt.Value) >= nuevosDesde;
            });

            // La API actual no trae FirstResponseTime; quedará 0 si no existe
            var frts = conversaciones
                .Select(c => GetInt(c, "FirstResponseTime", "FirstReplySeconds", "TiempoPrimeraRespuesta"))
                .Where(v => v.HasValue && v.Value >= 0)
                .Select(v => v!.Value)
                .ToList();

            int avgFrtSeconds = frts.Count > 0 ? (int)Math.Round(frts.Average()) : 0;
            string frtDisplay = FormatearSegundos(avgFrtSeconds);

            // ===== Mensajes por mes (últimos 12) =====
            var culture = new CultureInfo("es-CR");
            var start12 = new DateTime(nowUtc.Year, nowUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(-11);
            var monthSeq = Enumerable.Range(0, 12).Select(i => start12.AddMonths(i)).ToList();

            // OJO: usar SentAt (tu JSON real: "sentAt")
            var fechasMensajes = mensajes
                .Select(m => GetDate(m, "SentAt", "Timestamp", "CreatedAt", "Fecha", "FechaCrea"))
                .Where(dt => dt.HasValue)
                .Select(dt => NormalizeToUtc(dt!.Value))
                .Where(dt => dt >= start12)
                .ToList();

            var countsByYm = fechasMensajes
                .GroupBy(d => new { d.Year, d.Month })
                .ToDictionary(g => (g.Key.Year, g.Key.Month), g => g.Count());

            var labelsMes = new List<string>();
            var valuesMes = new List<int>();
            foreach (var dt in monthSeq)
            {
                labelsMes.Add(dt.ToString("MMM", culture));
                valuesMes.Add(countsByYm.TryGetValue((dt.Year, dt.Month), out var cnt) ? cnt : 0);
            }

            int totalMensajes = mensajes.Count;

            // ===== Actividad reciente (7 días) =====
            var actividades = new List<ActivityItem>();
            var desdeActividad = nowUtc.AddDays(-7);

            foreach (var c in contactos)
            {
                var createdAt = GetDate(c, "CreatedAt", "FechaCreacion", "Created", "CreatedDate");
                if (createdAt.HasValue && NormalizeToUtc(createdAt.Value) >= desdeActividad)
                {
                    var nombre = GetString(c, "Name", "Nombre", "FullName")
                                 ?? GetString(c, "PhoneNumber", "Telefono", "Celular")
                                 ?? "Cliente";
                    actividades.Add(new ActivityItem
                    {
                        Type = "new_client",
                        Title = $"Nuevo cliente: {nombre}",
                        Subtitle = "",
                        When = NormalizeToUtc(createdAt.Value)
                    });
                }
            }

            foreach (var cv in conversaciones)
            {
                var ended = GetDate(cv, "EndedAt", "FechaCierre", "ClosedAt");
                var lastActivity = GetDate(cv, "LastActivityAt", "UpdatedAt", "ModifiedAt");
                var when = ended ?? lastActivity;
                if (!when.HasValue) continue;

                var whenUtc = NormalizeToUtc(when.Value);
                if (whenUtc < desdeActividad) continue;

                if (!IsConversationClosed(cv)) continue;

                var idConv = GetInt(cv, "Id", "ConversationId", "IdConversacion") ?? 0;
                actividades.Add(new ActivityItem
                {
                    Type = "conv_closed",
                    Title = $"Conversación #{idConv} cerrada",
                    Subtitle = "",
                    When = whenUtc
                });
            }

            actividades = actividades
                .OrderByDescending(a => a.When)
                .Take(10)
                .ToList();

            // ===== ViewModel =====
            var vm = new DashboardDto
            {
                TotalConversaciones = totalConversaciones,
                ClientesNuevos = clientesNuevos,
                AvgFirstResponseSeconds = avgFrtSeconds,
                AvgFirstResponseDisplay = frtDisplay,
                MensajesMesLabels = labelsMes,
                MensajesMesValues = valuesMes,
                TotalMensajes = totalMensajes,
                Actividad = actividades
            };

            return View(vm);
        }

        // ---------------- helpers ----------------
        private static string FormatearSegundos(int s)
        {
            if (s <= 0) return "0s";
            if (s < 60) return $"{s}s";
            var ts = TimeSpan.FromSeconds(s);
            if (ts.TotalHours >= 1) return $"{(int)ts.TotalHours}h {ts.Minutes}m";
            return $"{ts.Minutes}m {ts.Seconds}s";
        }

        private static DateTime? GetDate(object obj, params string[] names)
        {
            var t = obj.GetType();
            foreach (var n in names)
            {
                var p = t.GetProperty(n, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase);
                if (p == null) continue;
                var v = p.GetValue(obj);
                if (v == null) continue;

                if (v is DateTime dt)
                    return DateTime.SpecifyKind(dt, DateTimeKind.Utc);

                if (DateTime.TryParse(v.ToString(), out var parsed))
                    return DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
            }
            return null;
        }

        private static int? GetInt(object obj, params string[] names)
        {
            var t = obj.GetType();
            foreach (var n in names)
            {
                var p = t.GetProperty(n, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase);
                if (p == null) continue;
                var v = p.GetValue(obj);
                if (v == null) continue;

                if (v is int i) return i;
                if (v is long l) return (int)l;
                if (v is short s) return (int)s;
                if (int.TryParse(v.ToString(), out var parsed)) return parsed;
            }
            return null;
        }

        private static string? GetString(object obj, params string[] names)
        {
            var t = obj.GetType();
            foreach (var n in names)
            {
                var p = t.GetProperty(n, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase);
                if (p == null) continue;
                var v = p.GetValue(obj);
                if (v is string s && !string.IsNullOrWhiteSpace(s)) return s;
            }
            return null;
        }

        private static bool? GetBool(object obj, params string[] names)
        {
            var t = obj.GetType();
            foreach (var n in names)
            {
                var p = t.GetProperty(n, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase);
                if (p == null) continue;
                var v = p.GetValue(obj);
                if (v == null) continue;

                if (v is bool b) return b;
                var s = v.ToString()?.Trim().ToLowerInvariant();
                if (s == "true" || s == "1") return true;
                if (s == "false" || s == "0") return false;
            }
            return null;
        }

        private static bool IsConversationClosed(object cv)
        {
            var status = (GetString(cv, "Status", "Estado", "State") ?? "").Trim().ToLowerInvariant();
            var closedWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "closed","cerrada","cerrado","finalizada","finalizado","terminada","terminated","ended","finalized"
            };
            if (closedWords.Contains(status)) return true;

            var isClosed = GetBool(cv, "IsClosed", "Closed");
            if (isClosed == true) return true;

            var statusId = GetInt(cv, "StatusId", "EstadoId", "StateId");
            if (statusId.HasValue && statusId.Value >= 2) return true;

            var ended = GetDate(cv, "EndedAt", "FechaCierre", "ClosedAt");
            if (ended.HasValue) return true;

            return false;
        }

        private static DateTime NormalizeToUtc(DateTime dt)
        {
            if (dt.Kind == DateTimeKind.Utc) return dt;
            if (dt.Kind == DateTimeKind.Local) return dt.ToUniversalTime();
            return DateTime.SpecifyKind(dt, DateTimeKind.Local).ToUniversalTime();
        }
    }
}
