using System;

namespace WhatsappClient.Models
{
    public class ConversationSessionDto
    {
        public int Id { get; set; }
        public int ContactId { get; set; }
        public DateTime StartedAt { get; set; }
        public DateTime LastActivityAt { get; set; }
        public bool GreetingSent { get; set; }
        public string Status { get; set; } = string.Empty;
        public DateTime? EndedAt { get; set; }

        public int? ClosedByUserId { get; set; }
    }
}
