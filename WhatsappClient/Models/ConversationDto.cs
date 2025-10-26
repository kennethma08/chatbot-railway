namespace WhatsappClient.Models
{
    public class ConversationDto
    {
        public int Id { get; set; }
        public int ContactId { get; set; }
        public int ConversationSessionId { get; set; }
        public string Message { get; set; } = string.Empty;
        public string Sender { get; set; } = string.Empty;   
        public string Type { get; set; } = string.Empty;
        public string MediaPath { get; set; } = string.Empty; 
        public DateTime SentAt { get; set; }
    }
}