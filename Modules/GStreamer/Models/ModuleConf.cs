using Shared.Models.Module;
using System.Collections.Generic;

namespace GStreamer;

public class ModuleConf : ModuleBaseConf
{
    public bool enable { get; set; }

    public int inactiveMinutes { get; set; }


    public double gst_version { get; set; }

    public string PATH { get; set; }

    public Dictionary<string, ModuleConf> conf_uids { get; set; }

    public string[] allowed_uids { get; set; }


    public bool tempfs { get; set; }

    public int tempfs_ring { get; set; }


    public int aac_bitrate { get; set; }

    public int segment_seconds { get; set; }

    public bool transcodeH264 { get; set; }

    public bool transcodeH265 { get; set; }

    public bool transcodeAV1 { get; set; }

    public bool transcodeVP9 { get; set; }

    public int video_bitrate { get; set; }


    public int pipeline_timeSeconds { get; set; }

    public int pipeline_audioQueue { get; set; }

    public int pipeline_videoQueue { get; set; }

    public int pipeline_sinkQueue { get; set; }
}