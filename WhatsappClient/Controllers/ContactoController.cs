using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WhatsappClient.Models;
using WhatsappClient.Services;

namespace WhatsappClient.Controllers
{
    [Authorize]
    public class ContactoController : Controller
    {
        private readonly ApiService _apiService;

        public ContactoController(ApiService apiService)
        {
            _apiService = apiService;
        }

        public async Task<IActionResult> Index()
        {
            var contactos = await _apiService.ObtenerContactosAsync();
            return View(contactos);
        }

        [HttpPost]
        public async Task<IActionResult> ActualizarNombre([FromBody] UpdateNombreReq req)
        {
            if (req == null || req.Id <= 0 || string.IsNullOrWhiteSpace(req.Nombre))
                return BadRequest("Parámetros inválidos.");

            var ok = await _apiService.UpdateNombreContactoAsync(req.Id, req.Nombre.Trim());
            if (!ok) return StatusCode(500, "No se pudo actualizar el nombre.");
            return Ok(new { mensaje = "Actualizado" });
        }

        public record UpdateNombreReq(int Id, string Nombre);

    }
}
