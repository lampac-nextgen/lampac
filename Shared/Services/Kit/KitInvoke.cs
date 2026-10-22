using Newtonsoft.Json.Linq;
using Shared.Models.Events;
using System.Reflection;

namespace Shared.Services.Kit;

public static class KitInvoke
{
    public static T Load<T>(T _init, JObject appinit, RequestModel requestInfo, Func<JObject, T, T, T> func = null, bool clone = true) where T : BaseSettings, ICloneable
    {
        var init = clone ? (T)_init.Clone() : _init;
        init.IsKitConf = false;
        init.IsCloneable = true;

        if (EventListener.LoadKitInit != null)
        {
            var em = new EventLoadKit(init, init, appinit, requestInfo);
            foreach (Action<EventLoadKit> handler in EventListener.LoadKitInit.GetInvocationList())
                handler(em);
        }

        if (!init.kit || appinit == null || string.IsNullOrEmpty(init.plugin) || !appinit.ContainsKey(init.plugin))
            return init;

        var userconf = appinit.Value<JObject>(init.plugin);

        var defaultinit = EventListener.LoadKit != null
            ? (clone ? _init : (T)_init.Clone())
            : null;

        if (CoreInit.conf.kit.absolute)
        {
            foreach (var prop in userconf.Properties())
            {
                try
                {
                    if (IsScriptSink(prop.Name))
                        continue;

                    var propertyInfo = typeof(T).GetProperty(prop.Name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                    if (propertyInfo?.CanWrite != true)
                        continue;

                    var value = prop.Value.ToObject(propertyInfo.PropertyType);
                    propertyInfo.SetValue(init, value);
                }
                catch (Exception ex)
                {
                    Serilog.Log.Error(ex, "{Class} {CatchId}", "KitInvoke", "id_dj2liohc");
                }
            }
        }
        else
        {
            void update<T2>(string key, Action<T2> updateAction)
            {
                if (userconf.ContainsKey(key))
                    updateAction(userconf.Value<T2>(key));
            }

            update<bool>("enable", v => init.enable = v);
            if (userconf.ContainsKey("enable") && init.enable)
                init.geo_hide = null;

            update<string>("displayname", v => init.displayname = v);
            update<int>("displayindex", v => init.displayindex = v);
            update<string>("client_type", v => init.client_type = v);

            update<string>("cookie", v => init.cookie = v);
            update<string>("token", v => init.token = v);

            update<string>("host", v => init.host = v);
            update<string>("apihost", v => init.apihost = v);
            update<string>("scheme", v => init.scheme = v);
            update<bool>("hls", v => init.hls = v);

            update<string>("overridehost", v => init.overridehost = v);
            update<string>("overridepasswd", v => init.overridepasswd = v);
            if (userconf.ContainsKey("overridehosts"))
                init.overridehosts = userconf["overridehosts"].ToObject<string[]>();

            if (userconf.ContainsKey("headers"))
                init.headers = userconf["headers"].ToObject<Dictionary<string, string>>();

            init.apnstream = true;
            if (userconf.ContainsKey("apn"))
                init.apn = userconf["apn"].ToObject<ApnConf>();

            init.useproxystream = false;
            update<bool>("streamproxy", v => init.streamproxy = v);
            update<bool>("qualitys_proxy", v => init.qualitys_proxy = v);
            if (userconf.ContainsKey("geostreamproxy"))
                init.geostreamproxy = userconf["geostreamproxy"].ToObject<string[]>();

            if (userconf.ContainsKey("proxy"))
            {
                init.proxy = userconf["proxy"].ToObject<ProxySettings>();
                if (init?.proxy?.list != null && init.proxy.list.Length > 0)
                    update<bool>("useproxy", v => init.useproxy = v);
            }

            if (init.useproxy)
            {
                init.rhub = false;
                init.rhub_fallback = true;
            }
            else if (CoreInit.conf.kit.rhub_fallback || init.rhub_fallback)
            {
                update<bool>("rhub", v => init.rhub = v);
                update<bool>("rhub_fallback", v => init.rhub_fallback = v);
            }
            else
            {
                init.rhub = true;
                init.rhub_fallback = false;
            }

            if (init.rhub)
                update<int>("cache_time", v => init.cache_time = v);
        }

        init.IsKitConf = true;

        if (EventListener.LoadKit != null)
        {
            var em = new EventLoadKit(defaultinit, init, userconf, requestInfo);
            foreach (Action<EventLoadKit> handler in EventListener.LoadKit.GetInvocationList())
                handler(em);
        }

        if (func != null)
            return func.Invoke(userconf, init, userconf.ToObject<T>());

        return init;
    }

    static bool IsScriptSink(string name)
    {
        return name.Contains("eval", StringComparison.OrdinalIgnoreCase)
            || name.Equals("route", StringComparison.OrdinalIgnoreCase)
            || name.Equals("view", StringComparison.OrdinalIgnoreCase)
            || name.Equals("contentParse", StringComparison.OrdinalIgnoreCase)
            || name.Equals("content", StringComparison.OrdinalIgnoreCase)
            || name.Equals("list", StringComparison.OrdinalIgnoreCase)
            || name.Equals("search", StringComparison.OrdinalIgnoreCase)
            || name.Equals("model", StringComparison.OrdinalIgnoreCase)
            || name.Equals("menu", StringComparison.OrdinalIgnoreCase)
            || name.Equals("card_parse", StringComparison.OrdinalIgnoreCase);
    }
}
