namespace LogUserRequest.Models.DTO;

public class StatsDto
{
    public int Today { get; set; }
    public int Month { get; set; }
    public int UniqueIp { get; set; }
    public int UniqueUserAgent { get; set; }
    public TopItemDto[] TopUsers { get; set; } = Array.Empty<TopItemDto>();
    public TopItemDto[] TopBalancers { get; set; } = Array.Empty<TopItemDto>();
}

public class TopItemDto
{
    public string Name { get; set; } = "";
    public int Count { get; set; }
}
