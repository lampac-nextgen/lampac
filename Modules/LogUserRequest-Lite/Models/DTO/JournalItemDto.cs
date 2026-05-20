namespace LogUserRequest.Models.DTO;

public class JournalItemDto
{
    public int Id { get; set; }
    public DateTime Time { get; set; }
    public string Uri { get; set; } = "";
    public string UserUid { get; set; } = "";
    public string Ip { get; set; } = "unknown";
    public string Country { get; set; } = "";
    public string UserAgent { get; set; } = "unknown";
    public int DurationMs { get; set; }
    public string? Balancer { get; set; }
}
