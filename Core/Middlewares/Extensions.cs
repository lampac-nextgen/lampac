using Microsoft.AspNetCore.Builder;

namespace Core.Middlewares;

public static class Extensions
{
    public static IApplicationBuilder UseBaseMod(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<BaseMod>();
    }

    public static IApplicationBuilder UseWAF(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<WAF>();
    }

    public static IApplicationBuilder UseModHeaders(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<ModHeaders>();
    }

    public static IApplicationBuilder UseResponseAvgStatistics(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<ResponseAvgStatistics>();
    }

    public static IApplicationBuilder UseOverrideResponse(this IApplicationBuilder builder, bool first)
    {
        return builder.UseMiddleware<OverrideResponse>(first);
    }

    public static IApplicationBuilder UseRequestInfo(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<RequestInfo>();
    }

    public static IApplicationBuilder UseAnonymousRequest(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<AnonymousRequest>();
    }

    public static IApplicationBuilder UseAccsdb(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<Accsdb>();
    }

    public static IApplicationBuilder UseProxyAPI(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<ProxyAPI>();
    }

    public static IApplicationBuilder UseProxyIMG(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<ProxyImg>();
    }

    public static IApplicationBuilder UseModule(this IApplicationBuilder builder, bool first)
    {
        return builder.UseMiddleware<Module>(first);
    }

    public static IApplicationBuilder UseModuleAsync(this IApplicationBuilder builder, bool first)
    {
        return builder.UseMiddleware<ModuleAsync>(first);
    }

    public static IApplicationBuilder UseStaticache(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<Staticache>();
    }

    public static IApplicationBuilder UseStaticacheWriter(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<StaticacheWriter>();
    }
}
