using System.Collections.Concurrent;
using System.Collections.Generic;
using System;

namespace Shared.Models.Module;

/// <summary>
/// Что сервер умеет для нативных клиентов. Модуль заявляет о себе сам в <c>Loaded</c>:
/// версию своего контракта знает только он, и ядру незачем помнить их все.
/// </summary>
public static class ModuleCapabilities
{
    static readonly ConcurrentDictionary<string, int> _features = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Заявить возможность. <paramref name="version"/> растёт при несовместимом изменении контракта.</summary>
    public static void Set(string id, int version)
    {
        if (!string.IsNullOrWhiteSpace(id))
            _features[id.Trim()] = version;
    }

    /// <summary>Снять возможность: модуль выгружен, и обещать его эндпоинты больше нельзя.</summary>
    public static void Remove(string id)
    {
        if (!string.IsNullOrWhiteSpace(id))
            _features.TryRemove(id.Trim(), out _);
    }

    public static IReadOnlyDictionary<string, int> All => _features;

    static volatile object _source;

    /// <summary>
    /// Описание входа для нативных клиентов (Source Connect, <c>luxo-source/1</c>): отдаётся в
    /// <c>/api/capabilities</c> под ключом <c>source</c>. Заполняет модуль, который выдаёт токены.
    /// </summary>
    public static object Source => _source;

    public static void SetSource(object manifest) => _source = manifest;

    public static void RemoveSource() => _source = null;
}
