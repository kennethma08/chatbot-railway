using System.Text.Json.Serialization;

namespace WhatsappClient.Models
{
    public class ContactoDto
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public string? PhoneNumber { get; set; }
        public string? Country { get; set; }
        public string? IpAddress { get; set; }
        public DateTime? CreatedAt { get; set; }
        public DateTime? LastMessageAt { get; set; }
        public string? ProfilePic { get; set; }
        public string? Status { get; set; }
        public bool WelcomeSent { get; set; }
    }

    public class DataWrapper<T>
    {
        [JsonPropertyName("$values")]
        public List<T> Values { get; set; } = new();
    }

    public class ApiResponse<T>
    {
        public DataWrapper<T> Data { get; set; } = new();
        public bool Exitoso { get; set; }
        public string Mensaje { get; set; } = string.Empty;
        public int StatusCode { get; set; }
    }
}
