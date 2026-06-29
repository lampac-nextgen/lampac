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
    public int tempfs_ring { get; set; }


    /// <summary>
    /// 256 кбит/с
    /// </summary>
    public int aac_bitrate { get; set; } = 256;

    /// <summary>
    /// sample rate для AAC энкодера (Hz). 0 = берётся из исходной дорожки.
    /// </summary>
    public int aac_samplerate { get; set; }

    /// <summary>
    /// количество каналов AAC. 0 = берётся из исходной дорожки (поддерживается до 7.1 / 8 каналов).
    /// </summary>
    public int aac_channels { get; set; }

    public int segment_seconds { get; set; } = 6;

    public bool transcodeH264 { get; set; }

    public bool transcodeH265 { get; set; }

    public bool transcodeAV1 { get; set; }

    public bool transcodeVP9 { get; set; }

    /// <summary>
    /// 10 Мбит/c
    /// </summary>
    public int video_bitrate { get; set; } = 10_000;


    /// <summary>
    /// Мбит/c
    /// </summary>
    public int pipeline_downloadRate { get; set; }

    public int pipeline_appsinkBuffers { get; set; } = 1000;
}