using Shared.Models.Module;
using System.Collections.Generic;

namespace GStreamer;

public class ModuleConf : ModuleBaseConf
{
    public bool enable { get; set; }

    public string debugType { get; set; }

    public int inactiveMinutes { get; set; }

    /// <summary>
    /// Максимальное количество одновременно хранимых задач. 0 = без ограничения.
    /// </summary>
    public int maxTasks { get; set; }


    public double gst_version { get; set; }

    public string PATH { get; set; }

    /// <summary>
    /// Максимальная скорость чтения souphttpsrc в MB/s. 0 = без ограничения.
    /// </summary>
    public int souphttpsrc_max_mb { get; set; }

    public Dictionary<string, ModuleConf> conf_uids { get; set; }

    public string[] allowed_uids { get; set; }


    /// <summary>
    /// задний кеш m4s
    /// </summary>
    public int segment_past { get; set; } = 1;

    /// <summary>
    /// максимальный размер заднего кеша в MB. 0 = без ограничения.
    /// </summary>
    public int segment_past_mb { get; set; }

    /// <summary>
    /// количество буферных m4s
    /// </summary>
    public int segment_buffer { get; set; } = 10;

    /// <summary>
    /// максимальный размер буферных m4s в MB. 0 = без ограничения.
    /// </summary>
    public int segment_buffer_mb { get; set; }

    /// <summary>
    /// без transcode видео - примерная длительность сегмента
    /// для transcode видео - точная длительность сегмента
    /// </summary>
    public int segment_seconds { get; set; } = 6;

    /// <summary>
    /// граница выравнивания
    /// </summary>
    public int segment_diff { get; set; } = 20;

    public bool subtitles { get; set; } = true;


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


    /// <summary>
    /// если нужна нарезка m4s сегментов срого по segment_seconds
    /// </summary>
    public bool transcodeH264 { get; set; }

    /// <summary>
    /// конвертировать видео в h.265 > h.264
    /// </summary>
    public bool transcodeH265 { get; set; }

    public bool transcodeAV1 { get; set; }

    public bool transcodeVP9 { get; set; }

    public bool transcodeVP8 { get; set; }

    public bool transcodeAVI { get; set; }

    /// <summary>Convert detected HDR video to SDR. Requires a real HDR tone-mapping backend.</summary>
    public bool hdr_to_sdr { get; set; }

    /// <summary>Use a hardware H.264 backend after a successful startup probe.</summary>
    public bool hardwareAcceleration { get; set; } = true;

    /// <summary>Enable GPU backends added by this module. GStreamer decodebin remains automatic.</summary>
    public bool useGpu { get; set; } = true;

    /// <summary>Use the x264 ultrafast preset instead of veryfast for software encoding.</summary>
    public bool x264Ultrafast { get; set; }

    /// <summary>
    /// 14 Мбит/c
    /// </summary>
    public int video_bitrate { get; set; } = 14_000;
}
