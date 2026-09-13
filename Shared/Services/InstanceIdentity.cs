using System;
using System.IO;
using System.Text.RegularExpressions;

namespace Shared.Services;

/// <summary>
/// Общая идентичность клиентов этого сервера.
///
/// Веб-клиент сам себе придумывает случайный <c>lampac_unic_id</c> и живёт с ним: перенести его
/// на второе устройство нечем, а вход в куб и вовсе уводит запросы в область по <c>account_email</c>.
/// Поэтому идентичность раздаёт сервер — и телефон, и приставка попадают в одну область данных.
///
/// Только там, где accsdb пуст: со списком пользователей идентичность уже есть у каждого своя,
/// и общая на всех означала бы одну историю просмотров на весь сервер.
/// </summary>
public static class InstanceIdentity
{
    static readonly object _lock = new();
    static readonly string _path = "database/instance.uid";
    static string _value;

    /// <summary>Идентичность для клиента, у которого своей нет. <c>null</c>, когда её раздавать нельзя.</summary>
    public static string Assigned
    {
        get
        {
            var users = CoreInit.conf.accsdb?.users;
            if (users != null && users.Count > 0)
                return null;

            return Value;
        }
    }

    static string Value
    {
        get
        {
            if (_value != null)
                return _value;

            lock (_lock)
            {
                if (_value != null)
                    return _value;

                _value = Read() ?? Create();
                return _value;
            }
        }
    }

    static string Read()
    {
        try
        {
            if (!File.Exists(_path))
                return null;

            string stored = File.ReadAllText(_path).Trim();
            // Тот же алфавит, что переживает санитайзер user_id в TimeCode.
            return Regex.IsMatch(stored, "^[a-z0-9]{8,64}$") ? stored : null;
        }
        catch { return null; }
    }

    static string Create()
    {
        string generated = Guid.NewGuid().ToString("N").Substring(0, 16);

        try
        {
            Directory.CreateDirectory("database");
            File.WriteAllText(_path, generated);
            Serilog.Log.Information("{Module} shared client identity created", "InstanceIdentity");
        }
        catch (Exception ex)
        {
            // Не записалось — работаем в памяти: до перезапуска клиенты всё равно сойдутся.
            Serilog.Log.Warning(ex, "{Module} cannot persist shared client identity", "InstanceIdentity");
        }

        return generated;
    }
}
