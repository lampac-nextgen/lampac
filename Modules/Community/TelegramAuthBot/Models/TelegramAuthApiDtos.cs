using System.Collections.Generic;

namespace TelegramAuthBot.Models
{
    public class UserByTelegramDto
    {
        public bool found { get; set; }
        public string telegramId { get; set; }
        public string username { get; set; }
        public string role { get; set; }
        public string lang { get; set; }
        public bool disabled { get; set; }
        public bool active { get; set; }
        public string expiresAt { get; set; }
        public int deviceCount { get; set; }
        public int maxDevices { get; set; }
    }

    public class DevicesResponseDto
    {
        public string telegramId { get; set; }
        public string username { get; set; }
        public List<DeviceDto> devices { get; set; }
    }

    public class DeviceDto
    {
        public string uid { get; set; }
        public string name { get; set; }
        public bool active { get; set; }
    }

    public class AdminUsersListResponseDto
    {
        public bool ok { get; set; }
        public List<AdminUserRowDto> users { get; set; }
    }

    public class AdminUserRowDto
    {
        public string telegramId { get; set; }
        public string username { get; set; }
        public string role { get; set; }
        public bool disabled { get; set; }
        public bool active { get; set; }
        public string expiresAt { get; set; }
        public int deviceCount { get; set; }
    }
}
