using System.Text.Json.Serialization;

namespace QRAuth.Models
{
    /// <summary>Shape of a shared users.json record — mirrors Lampac core's own AccsUser
    /// (lampac/Shared/Models/Base/AccsUser.cs), whose ban/ban_msg fields this must match
    /// exactly for revocation to take effect (see the Ban property below).</summary>
    public class LampacUser
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("tg_id")]
        public long TgId { get; set; } = 0;

        [JsonPropertyName("group")]
        public int Group { get; set; } = 1;

        [JsonPropertyName("expires")]
        public string Expires { get; set; } = "";

        [JsonPropertyName("comment")]
        public string Comment { get; set; } = "";

        // Matches Lampac core's own AccsUser.ban/ban_msg (lampac/Shared/Models/Base/AccsUser.cs)
        // — this is the field Lampac's Accsdb middleware actually checks per-request
        // (Core/Middlewares/Accsdb.cs) to deny an already-logged-in session. Deleting a row
        // from users.json only stops FUTURE logins: Lampac's Program.cs:UpdateUsersDb polls
        // users.json every ~1s but only ever upserts into its in-memory user cache, never
        // prunes an entry whose row disappeared — so a removed user stays cached as
        // authorized until the process restarts. Setting ban=true on the EXISTING row is
        // what actually mutates that same cached object in place, revoking access within ~1s.
        [JsonPropertyName("ban")]
        public bool Ban { get; set; } = false;

        [JsonPropertyName("ban_msg")]
        public string BanMsg { get; set; } = "";

        [JsonPropertyName("params")]
        public LampacUserParams Params { get; set; } = new();
    }

    public class LampacUserParams
    {
        [JsonPropertyName("adult")]
        public bool Adult { get; set; } = false;

        [JsonPropertyName("admin")]
        public bool Admin { get; set; } = false;
    }
}
