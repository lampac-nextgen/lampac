using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace TelegramAuth.Models
{
    public class LegacyDeviceRecord
    {
        public string uid { get; set; } = "";
        public string? label { get; set; }
        public string? bound_at { get; set; }
        public string? last_seen { get; set; }
        public string? last_ip { get; set; }
        public string? fingerprint { get; set; }
    }

    public class LegacyTokenRecord
    {
        public string token { get; set; } = "";
        public string telegram_id { get; set; } = "";
        public string tg_username { get; set; } = "";
        public DateTime? created_at { get; set; }
        public DateTime? expires_at { get; set; }
        public string approved_by { get; set; } = "";
        public List<LegacyDeviceRecord> devices { get; set; } = new();
    }

    public class DeviceRecord
    {
        public string Uid { get; set; } = "";
        public string? Name { get; set; }
        public DateTime LinkedAt { get; set; } = DateTime.UtcNow;
        public DateTime? LastSeenAt { get; set; }
        public bool Active { get; set; } = true;
        public string Source { get; set; } = "import";
    }

    public class TelegramUserRecord
    {
        public string TelegramId { get; set; } = "";
        public string TgUsername { get; set; } = "";
        public string Role { get; set; } = "user";
        public string Lang { get; set; } = "ru";
        public string? ApprovedBy { get; set; }
        public DateTime? CreatedAt { get; set; }
        public DateTime? ExpiresAt { get; set; }


        public bool Disabled { get; set; }

        /// <summary>Ожидает решения модератора (подтвердить / отклонить регистрацию).</summary>
        public bool RegistrationPending { get; set; }

        public List<DeviceRecord> Devices { get; set; } = new();
    }

    public class AuthStatusResponse
    {
        public bool Authorized { get; set; }
        public bool Pending { get; set; }
        /// <summary>True если аккаунт создан и ждёт подтверждения администратора.</summary>
        public bool RegistrationPending { get; set; }
        public string? Message { get; set; }
        public string? TelegramId { get; set; }
        public string? Username { get; set; }
        public string? Role { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public int DeviceCount { get; set; }
    }

    public class BindStartRequest
    {
        public string Uid { get; set; } = "";
    }

    public class DeviceSetNameRequest
    {
        [JsonProperty("uid", Required = Required.Always)]
        public string Uid { get; set; } = "";
        [JsonProperty("name")]
        public string? Name { get; set; }
    }

    public class DeviceReactivateRequest
    {
        public string TelegramId { get; set; } = "";
        public string Uid { get; set; } = "";
    }

    public class DeviceUnbindRequest
    {
        public string TelegramId { get; set; } = "";
        public string Uid { get; set; } = "";
    }

    public class BindCompleteRequest
    {
        public string Uid { get; set; } = "";
        public string TelegramId { get; set; } = "";
        public string? Username { get; set; }
        public string? DeviceName { get; set; }
    }

    public class ImportResult
    {
        public int ImportedUsers { get; set; }
        public int ImportedDevices { get; set; }
        public int ImportedAdmins { get; set; }
        public int ImportedLangs { get; set; }
    }

    public class AdminSetUserDisabledRequest
    {
        public string TelegramId { get; set; } = "";
        public bool Disabled { get; set; } = true;
    }

    public class AdminPendingDecisionRequest
    {
        public string TelegramId { get; set; } = "";
        /// <summary>true — подтвердить и включить доступ; false — отклонить и удалить запись пользователя.</summary>
        [JsonProperty("approve")]
        public bool Approve { get; set; } = true;
    }

    public class BindDeviceOutcome
    {
        public bool NewUserProvisioned { get; set; }
        public bool PendingAdminApproval { get; set; }
    }
}
