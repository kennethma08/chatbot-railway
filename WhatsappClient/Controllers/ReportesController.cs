using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using WhatsappClient.Services;
using WhatsappClient.Models;

namespace WhatsappClient.Controllers
{
    public class ReportesController : Controller
    {
        private readonly ApiService _api;

        public ReportesController(ApiService api)
        {
            _api = api;
        }

        public ActionResult Index()
        {
            var from = DateTime.UtcNow.Date.AddDays(-29);
            var to = DateTime.UtcNow.Date;

            ViewBag.From = from.ToString("yyyy-MM-dd");
            ViewBag.To = to.ToString("yyyy-MM-dd");
            return View();
        }

        public record SeriesPoint(string label, int value);
        public record SeriesResponse(string granularity, List<SeriesPoint> points);
        public record CountItem(string name, int count);
        public record KpisResponse(int totalMessages, int agentClosures, int newClients);

        private static DateTime ParseDate(string s) =>
            DateTime.SpecifyKind(DateTime.Parse(s, CultureInfo.InvariantCulture), DateTimeKind.Utc);

        private static DateTime StartOfIsoWeek(DateTime d)
        {
            var day = (int)d.DayOfWeek;
            if (day == 0) day = 7;
            var monday = d.Date.AddDays(1 - day);
            return DateTime.SpecifyKind(monday, DateTimeKind.Utc);
        }

        private static string NormalizeGroup(string? g)
        {
            g ??= "day";
            g = g.Trim().ToLowerInvariant();
            return g is "day" or "week" or "month" ? g : "day";
        }

        private static string LabelFor(DateTime d, string group) =>
            group switch
            {
                "day" => d.ToString("yyyy-MM-dd"),
                "week" => StartOfIsoWeek(d).ToString("yyyy-MM-dd"),
                "month" => d.ToString("yyyy-MM"),
                _ => d.ToString("yyyy-MM-dd")
            };

        private static object? Prop(object obj, params string[] names)
        {
            var t = obj.GetType();
            foreach (var n in names)
            {
                var p = t.GetProperty(n, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase);
                if (p != null) return p.GetValue(obj);
                var f = t.GetField(n, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase);
                if (f != null) return f.GetValue(obj);
            }
            return null;
        }
        private static DateTime? FlexDate(object obj, params string[] names)
        {
            var v = Prop(obj, names);
            if (v == null) return null;
            if (v is DateTime dt) return DateTime.SpecifyKind(dt, DateTimeKind.Utc);
            if (v is DateTimeOffset dto) return dto.UtcDateTime;
            if (v is long epoch) return DateTimeOffset.FromUnixTimeSeconds(epoch).UtcDateTime;
            if (v is string s && DateTime.TryParse(s, out var parsed)) return DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
            return null;
        }
        private static int? FlexInt(object obj, params string[] names)
        {
            var v = Prop(obj, names);
            if (v == null) return null;
            if (v is int i) return i;
            if (v is long l) return (int)l;
            if (v is string s && int.TryParse(s, out var si)) return si;
            return null;
        }
        private static bool FlexBool(object obj, params string[] names)
        {
            var v = Prop(obj, names);
            if (v == null) return false;
            if (v is bool b) return b;
            if (v is int i) return i != 0;
            if (v is string s && bool.TryParse(s, out var sb)) return sb;
            return false;
        }

        [HttpGet]
        public async Task<IActionResult> Series(string from, string to, string? groupBy = "day")
        {
            var f = ParseDate(from);
            var t = ParseDate(to);
            var g = NormalizeGroup(groupBy);

            var mensajes = await _api.ObtenerMensajesAsync();
            var dateNames = new[] { "Fecha", "CreatedAt", "SentAt", "Date", "Timestamp", "CreatedOn" };

            var points = mensajes
                .Select(m => FlexDate(m, dateNames))
                .Where(d => d != null && d!.Value.Date >= f.Date && d!.Value.Date <= t.Date)
                .GroupBy(d => LabelFor(d!.Value, g))
                .Select(gp => new SeriesPoint(gp.Key, gp.Count()))
                .OrderBy(p => p.label)
                .ToList();

            return Ok(new SeriesResponse(g, points));
        }

        // Cierres por agente
        [HttpGet]
        public async Task<IActionResult> AgentClosures(string from, string to)
        {
            var f = ParseDate(from);
            var t = ParseDate(to);

            var convs = await _api.ObtenerConversacionesAsync();
            var agentes = await _api.GetAgentesAsync();

            var closedFlag = new[] { "ClosedByAgent", "CerradoPorAgente", "Closed", "IsClosed" };
            var closedAtNames = new[] { "ClosedAt", "FechaCierre", "ClosedOn", "UpdatedAt", "Date" };
            var agentIdNames = new[] { "AgentId", "AgenteId", "UsuarioId", "UserId", "ClosedById" };

            var data = convs
                .Where(c =>
                {
                    if (!FlexBool(c, closedFlag)) return false;
                    var dt = FlexDate(c, closedAtNames) ?? FlexDate(c, "Fecha", "CreatedAt", "UpdatedAt");
                    return dt != null && dt.Value.Date >= f.Date && dt.Value.Date <= t.Date;
                })
                .GroupBy(c => FlexInt(c, agentIdNames) ?? -1)
                .Select(gp =>
                {
                    var agentId = gp.Key;
                    var name = agentId <= 0
                        ? "Sin agente"
                        : agentes.FirstOrDefault(a => a.Id == agentId)?.Nombre ?? $"Agente #{agentId}";
                    return new CountItem(name, gp.Count());
                })
                .OrderByDescending(x => x.count)
                .ToList();

            return Ok(data);
        }

        // Top clientes por mensajes
        [HttpGet]
        public async Task<IActionResult> TopClients(string from, string to, int take = 10)
        {
            var f = ParseDate(from);
            var t = ParseDate(to);

            var mensajes = await _api.ObtenerMensajesAsync();
            var contactos = await _api.ObtenerContactosAsync();

            var dateNames = new[] { "Fecha", "CreatedAt", "SentAt", "Date", "Timestamp" };
            var contactIdNames = new[] { "ContactId", "ContactoId", "ClienteId", "CustomerId", "ToContactId" };

            var data = mensajes
                .Select(m => new { dt = FlexDate(m, dateNames), contactId = FlexInt(m, contactIdNames) ?? -1 })
                .Where(x => x.dt != null && x.dt!.Value.Date >= f.Date && x.dt!.Value.Date <= t.Date)
                .GroupBy(x => x.contactId)
                .Select(gp =>
                {
                    var cid = gp.Key;
                    var name = cid <= 0
                        ? "Sin cliente"
                        : contactos.FirstOrDefault(c => c.Id == cid)?.Name ?? $"Cliente #{cid}";
                    return new CountItem(name, gp.Count());
                })
                .OrderByDescending(x => x.count)
                .Take(Math.Max(1, take))
                .ToList();

            return Ok(data);
        }

        //KPIs (con clientes NUEVOS)

        [HttpGet]
        public async Task<IActionResult> Kpis(string from, string to)
        {
            var f = ParseDate(from);
            var t = ParseDate(to);

            var mensajes = await _api.ObtenerMensajesAsync();
            var convs = await _api.ObtenerConversacionesAsync();

            var msgDateNames = new[] { "Fecha", "CreatedAt", "SentAt", "Date", "Timestamp" };
            var cnvDateNames = new[] { "ClosedAt", "FechaCierre", "UpdatedAt", "Fecha", "CreatedAt" };
            var closedFlag = new[] { "ClosedByAgent", "CerradoPorAgente", "Closed", "IsClosed" };
            var contactId = new[] { "ContactId", "ContactoId", "ClienteId", "CustomerId" };

            // mensajes con fecha y contactId válido
            var msgs = mensajes
                .Select(m => new { dt = FlexDate(m, msgDateNames), cid = FlexInt(m, contactId) ?? -1 })
                .Where(x => x.dt != null && x.cid > 0)
                .ToList();

            // total mensajes EN RANGO
            var totalMessages = msgs.Count(x => x.dt!.Value.Date >= f.Date && x.dt!.Value.Date <= t.Date);

            // cierres EN RANGO
            var agentClosures = convs.Count(c =>
            {
                if (!FlexBool(c, closedFlag)) return false;
                var d = FlexDate(c, cnvDateNames);
                return d != null && d.Value.Date >= f.Date && d.Value.Date <= t.Date;
            });

            // clientes nuev os
            var firstByClient = msgs
                .GroupBy(x => x.cid)
                .Select(g => new { cid = g.Key, first = g.Min(v => v.dt!.Value.Date) });

            var newClients = firstByClient.Count(x => x.first >= f.Date && x.first <= t.Date);

            return Ok(new KpisResponse(totalMessages, agentClosures, newClients));
        }

        public ActionResult Details(int id) => View();
        public ActionResult Create() => View();

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Create(IFormCollection collection)
        {
            try { return RedirectToAction(nameof(Index)); } catch { return View(); }
        }

        public ActionResult Edit(int id) => View();

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Edit(int id, IFormCollection collection)
        {
            try { return RedirectToAction(nameof(Index)); } catch { return View(); }
        }

        public ActionResult Delete(int id) => View();

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Delete(int id, IFormCollection collection)
        {
            try { return RedirectToAction(nameof(Index)); } catch { return View(); }
        }
    }
}
