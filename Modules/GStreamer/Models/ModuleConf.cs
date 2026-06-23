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


    /// <summary>
    /// файловый буфер http потока
    /// </summary>
    public bool tempfs { get; set; } = true;

    /// <summary>
    /// количество буферных блоков videoQueue
    /// </summary>
    public int tempfs_ring { get; set; } = 1;


    /// <summary>
    /// 256 кбит/с
    /// </summary>
    public int aac_bitrate { get; set; } = 256;

    public int segment_seconds { get; set; } = 6;

    public bool transcodeH264 { get; set; }

    public bool transcodeH265 { get; set; }

    public bool transcodeAV1 { get; set; }

    public bool transcodeVP9 { get; set; }

    /// <summary>
    /// 10 Мбит/c
    /// </summary>
    public int video_bitrate { get; set; } = 10_000;


    public int pipeline_timeSeconds { get; set; } = 20;

    public int pipeline_audioQueue { get; set; } = 4;

    public int pipeline_videoQueue { get; set; } = 32;

    public int pipeline_sinkQueue { get; set; } = 64;
}