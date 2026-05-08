namespace ProxyLimiter;

public class ModuleConf
{
    public int PermitLimit { get; set; }

    public int Window { get; set; }

    public int SegmentsPerWindow { get; set; }

    public int QueueLimit { get; set; }
}
