// Models/MessageDto.cs
using System;
using System.Text.Json.Serialization;

namespace WhatsappClient.Models
{
    public class MessageDto
    {
        //  Campos devueltos por la API
        [JsonPropertyName("id")]
        public int? Id { get; set; }

        [JsonPropertyName("conversationId")]
        public int? ConversationId { get; set; }

        [JsonPropertyName("contactId")]
        public int? ContactId { get; set; }

        [JsonPropertyName("agentId")]
        public int? AgentId { get; set; }

        [JsonPropertyName("sender")]
        public string? Sender { get; set; }

        [JsonPropertyName("message")]
        public string Contenido { get; set; } = string.Empty;

        [JsonPropertyName("type")]
        public string Type { get; set; } = "text";


        [JsonPropertyName("mediaPath")]
        public string? Media { get; set; }

        // ⬅️ CLAVE: fecha que usa el gráfico (API devuelve "sentAt")
        [JsonPropertyName("sentAt")]
        public DateTime Timestamp { get; set; }

        // Campos “UI” (si no vienen de la API, quedan por defecto)
        public string From { get; set; } = string.Empty;
        public string? To { get; set; }
        public string Nombre { get; set; } = string.Empty;
    }
}
