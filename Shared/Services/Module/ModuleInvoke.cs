using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Shared.Services
{
    public static class ModuleInvoke
    {
        static readonly object _syncCurrentConf = new object();

        public static T Init<T>(string filed, T val)
        {
            if (val == null)
                return val;

            // Use existing ConfObject logic to get merged JObject/token
            var confObj = Conf(filed, val);
            if (confObj == null)
                return val;

            // If caller expects a JObject, return directly
            if (typeof(T) == typeof(JObject))
            {
                UpdateCurrentConf(filed, confObj);
                return (T)(object)confObj;
            }

            // If we have a wrapper for non-object values { "value": ... }, extract it
            if (confObj.Count == 1 && confObj.ContainsKey("value"))
            {
                try
                {
                    var token = confObj["value"];
                    UpdateCurrentConf(filed, token);
                    return token.ToObject<T>();
                }
                catch
                {
                    return val;
                }
            }

            // Otherwise try to convert the merged object back to T
            try
            {
                var result = confObj.ToObject<T>();
                UpdateCurrentConf(filed, confObj);
                return result;
            }
            catch
            {
                return val;
            }
        }

        public static T DeserializeInit<T>(T back) where T : class
        {
            try
            {
                string initfile = GetInitSource();
                bool initExists = string.IsNullOrEmpty(initfile)
                    ? false
                    : true;

                string basefile = GetBaseSource();
                bool baseExists = string.IsNullOrEmpty(basefile)
                    ? false
                    : true;

                if (!initExists && !baseExists)
                    return back;

                T conf = default;

                if (initExists && baseExists)
                {
                    conf = JsonConvert.DeserializeObject<T>(basefile, new JsonSerializerSettings
                    {
                        Error = (se, ev) =>
                        {
                            ev.ErrorContext.Handled = true;
                            Console.WriteLine($"DeserializeObject Exception base.conf:\n{ev.ErrorContext.ToString()}\n\n");
                        }
                    });

                    JsonConvert.PopulateObject(initfile, conf, new JsonSerializerSettings
                    {
                        Error = (se, ev) =>
                        {
                            ev.ErrorContext.Handled = true;
                            Console.WriteLine($"DeserializeObject Exception init.conf:\n{ev.ErrorContext.Error}\n\n");
                        }
                    });
                }
                else
                {
                    if (initExists)
                    {
                        conf = JsonConvert.DeserializeObject<T>(initfile, new JsonSerializerSettings
                        {
                            Error = (se, ev) =>
                            {
                                ev.ErrorContext.Handled = true;
                                Console.WriteLine($"DeserializeObject Exception init.conf:\n{ev.ErrorContext.ToString()}\n\n");
                            }
                        });
                    }
                    else
                    {
                        conf = JsonConvert.DeserializeObject<T>(basefile, new JsonSerializerSettings
                        {
                            Error = (se, ev) =>
                            {
                                ev.ErrorContext.Handled = true;
                                Console.WriteLine($"DeserializeObject Exception base.conf:\n{ev.ErrorContext.ToString()}\n\n");
                            }
                        });
                    }
                }

                if (conf == default)
                    return back;

                UpdateCurrentConf(conf);
                return conf;
            }
            catch { return back; }
        }


        static T UpdateCurrentConf<T>(T val) where T : class
        {
            if (val == null)
                return val;

            try
            {
                var updateObj = JObject.FromObject(val);

                lock (_syncCurrentConf)
                {
                    if (CoreInit.CurrentConf == null)
                        CoreInit.CurrentConf = CoreInit.conf != null ? JObject.FromObject(CoreInit.conf) : new JObject();

                    Merge(CoreInit.CurrentConf, updateObj);
                }
            }
            catch { }

            return val;
        }

        static void UpdateCurrentConf(string filed, JToken val)
        {
            if (string.IsNullOrEmpty(filed) || val == null)
                return;

            try
            {
                lock (_syncCurrentConf)
                {
                    if (CoreInit.CurrentConf == null)
                        CoreInit.CurrentConf = CoreInit.conf != null ? JObject.FromObject(CoreInit.conf) : new JObject();

                    CoreInit.CurrentConf[filed] = val.DeepClone();
                }
            }
            catch { }
        }

        static JObject Conf(string filed, object val)
        {
            if (val == null)
                return null;

            // Convert incoming value to JToken/JObject
            JToken baseToken = val as JToken ?? JToken.FromObject(val);
            if (baseToken == null)
                return null;

            if (baseToken.Type != JTokenType.Object)
            {
                // For non-object values wrap into a simple object so merging still possible
                return new JObject { ["value"] = baseToken };
            }

            var baseObj = (JObject)baseToken;

            try
            {
                JObject jo = null;

                string basefile = GetBaseSource();
                if (!string.IsNullOrEmpty(basefile))
                {
                    try
                    {
                        jo = JObject.Parse(basefile);
                    }
                    catch { }
                }

                string initfile = GetInitSource();
                if (!string.IsNullOrEmpty(initfile))
                {
                    try
                    {
                        if (jo == null)
                            jo = JObject.Parse(initfile);
                        else
                            Merge(jo, JObject.Parse(initfile));
                    }
                    catch { }
                }

                if (jo == null || !jo.ContainsKey(filed))
                    return baseObj;

                var node = jo[filed];

                // If field explicitly false -> return original val
                if (node.Type == JTokenType.Boolean && node.Value<bool>() == false)
                    return baseObj;

                // If node is not an object, nothing to merge -> return original
                if (node.Type != JTokenType.Object)
                    return baseObj;

                var overrideObj = (JObject)node;

                // Deep clone base
                var result = (JObject)baseObj.DeepClone();

                Merge(result, overrideObj);

                return result;
            }
            catch
            {
                return baseObj;
            }
        }

        static void Merge(JObject target, JObject source)
        {
            foreach (var prop in source.Properties())
            {
                var tprop = target.Property(prop.Name);

                if (tprop != null && tprop.Value.Type == JTokenType.Object && prop.Value.Type == JTokenType.Object)
                {
                    Merge((JObject)tprop.Value, (JObject)prop.Value);
                }
                else
                {
                    // Replace or add
                    target[prop.Name] = prop.Value.DeepClone();
                }
            }
        }


        #region GetInitSource
        static (DateTime nextCheckTime, DateTime LastWriteTime, string source) _cacheInitFile;

        static string GetInitSource()
        {
            try
            {
                if (_cacheInitFile.nextCheckTime > DateTime.Now)
                    return _cacheInitFile.source;

                if (!File.Exists("init.conf"))
                {
                    _cacheInitFile.nextCheckTime = DateTime.Now.AddSeconds(1);
                    _cacheInitFile.source = null;
                    return null;
                }

                var lastWriteTime = File.GetLastWriteTimeUtc("init.conf");
                if (_cacheInitFile.LastWriteTime != lastWriteTime)
                {
                    string source = File.ReadAllText("init.conf");

                    if (!source.AsSpan().TrimStart().StartsWith("{"))
                        source = "{" + source + "}";

                    _cacheInitFile.source = source;
                    _cacheInitFile.LastWriteTime = lastWriteTime;
                    _cacheInitFile.nextCheckTime = DateTime.Now.AddSeconds(1);

                    return _cacheInitFile.source;
                }
                else
                {
                    _cacheInitFile.nextCheckTime = DateTime.Now.AddSeconds(1);
                    return _cacheInitFile.source;
                }
            }
            catch
            {
                return null;
            }
        }
        #endregion

        #region GetBaseSource
        static (DateTime nextCheckTime, DateTime LastWriteTime, string source) _cacheBaseFile;

        static string GetBaseSource()
        {
            try
            {
                if (_cacheBaseFile.nextCheckTime > DateTime.Now)
                    return _cacheBaseFile.source;

                if (!File.Exists("base.conf"))
                {
                    _cacheBaseFile.nextCheckTime = DateTime.Now.AddSeconds(5);
                    _cacheBaseFile.source = null;
                    return null;
                }

                var lastWriteTime = File.GetLastWriteTimeUtc("base.conf");
                if (_cacheBaseFile.LastWriteTime != lastWriteTime)
                {
                    _cacheBaseFile.source = File.ReadAllText("base.conf");
                    _cacheBaseFile.LastWriteTime = lastWriteTime;
                    _cacheBaseFile.nextCheckTime = DateTime.Now.AddSeconds(5);

                    return _cacheBaseFile.source;
                }
                else
                {
                    _cacheBaseFile.nextCheckTime = DateTime.Now.AddSeconds(5);
                    return _cacheBaseFile.source;
                }
            }
            catch
            {
                return null;
            }
        }
        #endregion
    }
}
