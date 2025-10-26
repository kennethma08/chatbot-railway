namespace WhatsappClient.Models
{
    public class UsuarioDto
    {
        public int Id { get; set; }
        public string? Nombre { get; set; }
        public string? Correo { get; set; }
        public string? Cedula { get; set; }
        public string? Telefono { get; set; }
        public bool? Estado { get; set; }
        public int? IdPerfil { get; set; }
        public string? Empresa { get; set; }

        public int? AgentId { get; set; }
        public int? EmpresaID { get; set; }
        public int? ContactId { get; set; }
        public DateTime? LastLogin { get; set; }
        public DateTime? LastActivity { get; set; }
        public bool IsOnline { get; set; }
        public int ConversationCount { get; set; }
    }
}
