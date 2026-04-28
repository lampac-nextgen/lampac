using Newtonsoft.Json;

namespace Shared.Services.Pools.Json;

public static class JsonDefaultSerializerPool
{
    [ThreadStatic]
    private static JsonSerializer _instance;

    public static JsonSerializer Instance
    {
        get
        {
            if (CoreInit.conf.lowMemoryMode)
                return JsonSerializer.CreateDefault();

            return _instance ??= JsonSerializer.CreateDefault();
        }
    }
}
