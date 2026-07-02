namespace Shared.Models.Online.Settings;

public class PidTorSettings : Igroup, ICloneable
{
    bool _enable;

    public bool enable
    {
        get
        {
            if (CoreInit.conf.defaultOn == "enabled")
                return enabled;

            return _enable;
        }
        set
        {
            _enable = value;
        }
    }

    public bool enabled { get; set; }

    public bool gst { get; set; }

    /// <summary>
    /// [0,1,2,3,4,5,6,7,8,9,10,11,12,13,14,15,16,17,18,19,20,21,22,23,24]
    /// </summary>
    public int[] workinghours { get; set; }


    public string displayname { get; set; }

    public int displayindex { get; set; }

    public string redapi { get; set; }

    public string apikey { get; set; }

    public int min_sid { get; set; }

    public long max_size { get; set; }

    public long max_serial_size { get; set; }

    public bool emptyVoice { get; set; }

    public bool forceAll { get; set; }

    public string filter { get; set; }

    public string filter_ignore { get; set; }

    public string sort { get; set; }

    public PidTorAuthTS base_auth { get; set; }

    public string[] torrs { get; set; }

    public List<PidTorAuthTS> auth_torrs { get; set; }

    public int group { get; set; }

    public bool group_hide { get; set; } = true;


    public PidTorSettings Clone()
    {
        return (PidTorSettings)MemberwiseClone();
    }

    object ICloneable.Clone()
    {
        return MemberwiseClone();
    }
}

public class PidTorAuthTS
{
    public bool enable { get; set; }

    public string host { get; set; }

    public bool aes { get; set; }

    public string login { get; set; }

    public string passwd { get; set; }

    public string country { get; set; }

    public string no_country { get; set; }

    public Dictionary<string, string> headers { get; set; }
}
