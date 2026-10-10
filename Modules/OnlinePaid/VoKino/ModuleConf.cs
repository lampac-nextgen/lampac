using Shared.Models.Base;
using System;
using System.Collections.Generic;

namespace VoKino;

public class ModuleConf : BaseSettings, ICloneable
{
    public ModuleConf(string plugin, string host)
    {
        this.plugin = plugin;

        if (host != null)
            this.host = host.StartsWith("http") ? host : Decrypt(host);
    }

    public bool onlyBalancerName { get; set; }

    public ViewOnline online { get; set; } = new ViewOnline();

    /// <summary>
    /// Индивидуальное управление streamproxy для каждого балансера из init.conf.
    /// Пример: "streamproxy_balancers": { "videobase": false, "coconut": false }
    /// </summary>
    public Dictionary<string, bool> streamproxy_balancers { get; set; }

    /// <summary>
    /// Несколько токенов. Если заданы — выбирается случайный на каждый запрос.
    /// Старое поле token тоже работает (fallback).
    /// </summary>
    public string[] tokens { get; set; }

    /// <summary>
    /// Проверяет необходимость проксирования потока для конкретного балансера.
    /// Приоритет у настроек из init.conf (streamproxy_balancers).
    /// </summary>
    public bool IsStreamProxy(string balancer)
    {
        if (balancer != null && streamproxy_balancers != null)
        {
            foreach (var item in streamproxy_balancers)
            {
                if (string.Equals(item.Key, balancer, StringComparison.OrdinalIgnoreCase))
                    return item.Value;
            }
        }

        return balancer?.ToLowerInvariant() switch
        {
            "filmix" or "monframe" or "videobase" => false,
            "vokino" or "alloha" or "vibix" or "coconut" => true,
            _ => streamproxy
        };
    }

    /// <summary>
    /// Возвращает рабочий токен: случайный из tokens, либо token.
    /// </summary>
    public string GetToken()
    {
        if (tokens != null && tokens.Length > 0)
        {
            var valid = Array.FindAll(tokens, t => !string.IsNullOrWhiteSpace(t));
            if (valid.Length > 0)
                return valid[Random.Shared.Next(valid.Length)];
        }

        return token;
    }

    /// <summary>
    /// Есть ли хоть один токен.
    /// </summary>
    public bool HasToken()
    {
        if (tokens != null && tokens.Length > 0 && Array.Exists(tokens, t => !string.IsNullOrWhiteSpace(t)))
            return true;

        return !string.IsNullOrEmpty(token);
    }

    public ModuleConf Clone()
    {
        return (ModuleConf)MemberwiseClone();
    }

    object ICloneable.Clone()
    {
        return MemberwiseClone();
    }
}
