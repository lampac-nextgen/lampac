#include <gst/gst.h>
#include <gst/base/gstbasetransform.h>
#include <gst/video/video.h>

#include <libavfilter/avfilter.h>
#include <libavfilter/buffersink.h>
#include <libavfilter/buffersrc.h>
#include <libavutil/buffer.h>
#include <libavutil/dict.h>
#include <libavutil/error.h>
#include <libavutil/frame.h>
#include <libavutil/hwcontext.h>
#include <libavutil/imgutils.h>
#include <libavutil/log.h>

#include <errno.h>
#include <stdint.h>

#ifndef PACKAGE
#define PACKAGE "gsthdrtonemap"
#endif

GST_DEBUG_CATEGORY_STATIC(gst_hdr_tone_map_debug);
#define GST_CAT_DEFAULT gst_hdr_tone_map_debug

typedef enum
{
    GST_HDR_TONE_MAP_TRANSFER_AUTO,
    GST_HDR_TONE_MAP_TRANSFER_PQ,
    GST_HDR_TONE_MAP_TRANSFER_HLG
} GstHdrToneMapTransfer;

typedef enum
{
    GST_HDR_TONE_MAP_BACKEND_NONE,
    GST_HDR_TONE_MAP_BACKEND_CPU,
    GST_HDR_TONE_MAP_BACKEND_OPENCL
} GstHdrToneMapBackend;

typedef struct _GstHdrToneMap
{
    GstBaseTransform parent;
    GMutex lock;
    GstVideoInfo input_info;
    GstVideoInfo output_info;
    enum AVPixelFormat input_pixel_format;
    GstHdrToneMapTransfer transfer;
    GstHdrToneMapBackend backend;
    gdouble peak_nits;
    gboolean use_opencl;
    gboolean opencl_failed;
    AVFilterGraph *graph;
    AVFilterContext *source;
    AVFilterContext *sink;
} GstHdrToneMap;

typedef struct _GstHdrToneMapClass
{
    GstBaseTransformClass parent_class;
} GstHdrToneMapClass;

#define GST_TYPE_HDR_TONE_MAP (gst_hdr_tone_map_get_type())
#define GST_HDR_TONE_MAP(obj) (G_TYPE_CHECK_INSTANCE_CAST((obj), GST_TYPE_HDR_TONE_MAP, GstHdrToneMap))

G_DEFINE_TYPE(GstHdrToneMap, gst_hdr_tone_map, GST_TYPE_BASE_TRANSFORM)

enum
{
    PROP_0,
    PROP_TRANSFER,
    PROP_PEAK_NITS,
    PROP_USE_OPENCL,
    PROP_ACTIVE_BACKEND
};

static gsize opencl_initialized = 0;
static AVBufferRef *opencl_device = NULL;

static GstStaticPadTemplate sink_template = GST_STATIC_PAD_TEMPLATE(
    "sink", GST_PAD_SINK, GST_PAD_ALWAYS,
    GST_STATIC_CAPS(
        "video/x-raw, format=(string){ P010_10LE, I420_10LE }, "
        "width=(int)[ 1, MAX ], height=(int)[ 1, MAX ], "
        "framerate=(fraction)[ 0/1, MAX ]"
    )
);

static GstStaticPadTemplate source_template = GST_STATIC_PAD_TEMPLATE(
    "src", GST_PAD_SRC, GST_PAD_ALWAYS,
    GST_STATIC_CAPS(
        "video/x-raw, format=(string)I420, colorimetry=(string)bt709, "
        "width=(int)[ 1, MAX ], height=(int)[ 1, MAX ], "
        "framerate=(fraction)[ 0/1, MAX ]"
    )
);

static GType gst_hdr_tone_map_transfer_get_type(void)
{
    static gsize type_id = 0;
    static const GEnumValue values[] = {
        { GST_HDR_TONE_MAP_TRANSFER_AUTO, "Detect transfer from input caps", "auto" },
        { GST_HDR_TONE_MAP_TRANSFER_PQ, "SMPTE ST 2084 (PQ)", "pq" },
        { GST_HDR_TONE_MAP_TRANSFER_HLG, "ARIB STD-B67 (HLG)", "hlg" },
        { 0, NULL, NULL }
    };

    if (g_once_init_enter(&type_id))
    {
        GType registered = g_enum_register_static("GstHdrToneMapTransfer", values);
        g_once_init_leave(&type_id, registered);
    }
    return (GType)type_id;
}

static GType gst_hdr_tone_map_backend_get_type(void)
{
    static gsize type_id = 0;
    static const GEnumValue values[] = {
        { GST_HDR_TONE_MAP_BACKEND_NONE, "Not initialized", "none" },
        { GST_HDR_TONE_MAP_BACKEND_CPU, "CPU zscale and Hable", "cpu" },
        { GST_HDR_TONE_MAP_BACKEND_OPENCL, "OpenCL Hable", "opencl" },
        { 0, NULL, NULL }
    };

    if (g_once_init_enter(&type_id))
    {
        GType registered = g_enum_register_static("GstHdrToneMapBackend", values);
        g_once_init_leave(&type_id, registered);
    }
    return (GType)type_id;
}

static void gst_hdr_tone_map_clear_graph_unlocked(GstHdrToneMap *self)
{
    if (self->graph != NULL)
        avfilter_graph_free(&self->graph);
    self->source = NULL;
    self->sink = NULL;
    self->backend = GST_HDR_TONE_MAP_BACKEND_NONE;
}

static const gchar *gst_hdr_tone_map_av_error(gint error, gchar *buffer, gsize size)
{
    if (av_strerror(error, buffer, size) < 0)
        g_strlcpy(buffer, "unknown FFmpeg error", size);
    return buffer;
}

static enum AVPixelFormat gst_hdr_tone_map_pixel_format(GstVideoFormat format)
{
    switch (format)
    {
        case GST_VIDEO_FORMAT_P010_10LE:
            return AV_PIX_FMT_P010LE;
        case GST_VIDEO_FORMAT_I420_10LE:
            return AV_PIX_FMT_YUV420P10LE;
        default:
            return AV_PIX_FMT_NONE;
    }
}

static const gchar *gst_hdr_tone_map_transfer_name(GstHdrToneMap *self)
{
    if (self->transfer == GST_HDR_TONE_MAP_TRANSFER_PQ)
        return "smpte2084";
    if (self->transfer == GST_HDR_TONE_MAP_TRANSFER_HLG)
        return "arib-std-b67";

    switch (self->input_info.colorimetry.transfer)
    {
        case GST_VIDEO_TRANSFER_SMPTE2084:
            return "smpte2084";
        case GST_VIDEO_TRANSFER_ARIB_STD_B67:
            return "arib-std-b67";
        default:
            return NULL;
    }
}

static AVBufferRef *gst_hdr_tone_map_get_opencl_device(void)
{
    if (g_once_init_enter(&opencl_initialized))
    {
        AVDictionary *options = NULL;
        gint result = AVERROR(ENOSYS);

        if (avfilter_get_by_name("hwupload") != NULL &&
            avfilter_get_by_name("hwdownload") != NULL &&
            avfilter_get_by_name("tonemap_opencl") != NULL)
        {
            gint previous_log_level = av_log_get_level();
            av_dict_set(&options, "device_type", "gpu", 0);
            av_log_set_level(AV_LOG_FATAL);
            result = av_hwdevice_ctx_create(&opencl_device,
                AV_HWDEVICE_TYPE_OPENCL, NULL, options, 0);
            av_log_set_level(previous_log_level);
        }

        av_dict_free(&options);
        if (result >= 0)
            GST_INFO("OpenCL HDR tone-mapping backend is available");
        else
            GST_INFO("OpenCL HDR tone-mapping backend is unavailable; CPU fallback will be used");

        g_once_init_leave(&opencl_initialized, 1);
    }

    return opencl_device;
}

static gint gst_hdr_tone_map_create_filter(AVFilterGraph *graph,
    AVFilterContext **context, const gchar *filter_name, const gchar *instance_name,
    const gchar *arguments, AVBufferRef *device)
{
    const AVFilter *filter = avfilter_get_by_name(filter_name);

    if (filter == NULL)
        return AVERROR_FILTER_NOT_FOUND;

    *context = avfilter_graph_alloc_filter(graph, filter, instance_name);
    if (*context == NULL)
        return AVERROR(ENOMEM);

    if (device != NULL)
    {
        (*context)->hw_device_ctx = av_buffer_ref(device);
        if ((*context)->hw_device_ctx == NULL)
            return AVERROR(ENOMEM);
    }

    return avfilter_init_str(*context, arguments);
}

static void gst_hdr_tone_map_source_arguments(GstHdrToneMap *self,
    gchar *buffer, gsize size)
{
    g_snprintf(buffer, size,
        "video_size=%dx%d:pix_fmt=%d:time_base=1/1000000000:pixel_aspect=1/1:"
        "colorspace=bt2020nc:range=limited",
        GST_VIDEO_INFO_WIDTH(&self->input_info),
        GST_VIDEO_INFO_HEIGHT(&self->input_info),
        self->input_pixel_format);
}

static gboolean gst_hdr_tone_map_build_opencl_graph_unlocked(GstHdrToneMap *self)
{
    AVBufferRef *device = gst_hdr_tone_map_get_opencl_device();
    AVFilterContext *input_format = NULL;
    AVFilterContext *upload = NULL;
    AVFilterContext *tone_map = NULL;
    AVFilterContext *download = NULL;
    AVFilterContext *download_format = NULL;
    AVFilterContext *scale = NULL;
    AVFilterContext *output_format = NULL;
    AVFilterContext *chain[9];
    gchar source_arguments[256];
    gchar error_buffer[AV_ERROR_MAX_STRING_SIZE];
    gint result = AVERROR(ENOSYS);
    guint index;

    if (device == NULL || gst_hdr_tone_map_transfer_name(self) == NULL)
        return FALSE;

    gst_hdr_tone_map_clear_graph_unlocked(self);
    self->graph = avfilter_graph_alloc();
    if (self->graph == NULL)
        return FALSE;

    gst_hdr_tone_map_source_arguments(self, source_arguments, sizeof(source_arguments));
    result = gst_hdr_tone_map_create_filter(self->graph, &self->source,
        "buffer", "input", source_arguments, NULL);
    if (result < 0)
        goto fail;
    result = gst_hdr_tone_map_create_filter(self->graph, &input_format,
        "format", "opencl_input_format", "pix_fmts=p010le", NULL);
    if (result < 0)
        goto fail;
    result = gst_hdr_tone_map_create_filter(self->graph, &upload,
        "hwupload", "opencl_upload", NULL, device);
    if (result < 0)
        goto fail;
    result = gst_hdr_tone_map_create_filter(self->graph, &tone_map,
        "tonemap_opencl", "opencl_tone_map",
        "tonemap=hable:desat=0:format=nv12:transfer=bt709:matrix=bt709:"
        "primaries=bt709:range=limited", device);
    if (result < 0)
        goto fail;
    result = gst_hdr_tone_map_create_filter(self->graph, &download,
        "hwdownload", "opencl_download", NULL, NULL);
    if (result < 0)
        goto fail;
    result = gst_hdr_tone_map_create_filter(self->graph, &download_format,
        "format", "opencl_download_format", "pix_fmts=nv12", NULL);
    if (result < 0)
        goto fail;
    result = gst_hdr_tone_map_create_filter(self->graph, &scale,
        "scale", "opencl_output_scale", NULL, NULL);
    if (result < 0)
        goto fail;
    result = gst_hdr_tone_map_create_filter(self->graph, &output_format,
        "format", "opencl_output_format", "pix_fmts=yuv420p", NULL);
    if (result < 0)
        goto fail;
    result = gst_hdr_tone_map_create_filter(self->graph, &self->sink,
        "buffersink", "output", NULL, NULL);
    if (result < 0)
        goto fail;

    chain[0] = self->source;
    chain[1] = input_format;
    chain[2] = upload;
    chain[3] = tone_map;
    chain[4] = download;
    chain[5] = download_format;
    chain[6] = scale;
    chain[7] = output_format;
    chain[8] = self->sink;
    for (index = 0; index + 1 < G_N_ELEMENTS(chain); index++)
    {
        result = avfilter_link(chain[index], 0, chain[index + 1], 0);
        if (result < 0)
            goto fail;
    }

    result = avfilter_graph_config(self->graph, NULL);
    if (result < 0)
        goto fail;

    self->backend = GST_HDR_TONE_MAP_BACKEND_OPENCL;
    GST_INFO_OBJECT(self, "Using OpenCL HDR tone mapping");
    return TRUE;

fail:
    GST_INFO_OBJECT(self, "Could not create OpenCL HDR tone-mapping graph: %s; using CPU fallback",
        gst_hdr_tone_map_av_error(result, error_buffer, sizeof(error_buffer)));
    gst_hdr_tone_map_clear_graph_unlocked(self);
    return FALSE;
}

static gboolean gst_hdr_tone_map_build_cpu_graph_unlocked(GstHdrToneMap *self)
{
    const AVFilter *buffer_source = avfilter_get_by_name("buffer");
    const AVFilter *buffer_sink = avfilter_get_by_name("buffersink");
    const gchar *transfer = gst_hdr_tone_map_transfer_name(self);
    AVFilterInOut *inputs = NULL;
    AVFilterInOut *outputs = NULL;
    gchar source_arguments[256];
    gchar peak[G_ASCII_DTOSTR_BUF_SIZE];
    gchar *description = NULL;
    gchar error_buffer[AV_ERROR_MAX_STRING_SIZE];
    gint result;

    if (transfer == NULL)
    {
        GST_ERROR_OBJECT(self, "Input caps do not identify PQ or HLG transfer");
        return FALSE;
    }

    gst_hdr_tone_map_clear_graph_unlocked(self);
    self->graph = avfilter_graph_alloc();
    if (self->graph == NULL)
        return FALSE;

    gst_hdr_tone_map_source_arguments(self, source_arguments, sizeof(source_arguments));

    result = avfilter_graph_create_filter(&self->source, buffer_source, "input",
        source_arguments, NULL, self->graph);
    if (result < 0)
        goto fail;

    result = avfilter_graph_create_filter(&self->sink, buffer_sink, "output",
        NULL, NULL, self->graph);
    if (result < 0)
        goto fail;

    g_ascii_dtostr(peak, sizeof(peak), self->peak_nits);
    description = g_strdup_printf(
        "zscale=t=linear:tin=%s:pin=bt2020:min=bt2020nc:rin=limited:npl=%s,"
        "format=pix_fmts=gbrpf32le,"
        "tonemap=tonemap=hable:desat=0,"
        "zscale=p=bt709:t=bt709:m=bt709:r=limited,"
        "format=pix_fmts=yuv420p",
        transfer, peak);

    outputs = avfilter_inout_alloc();
    inputs = avfilter_inout_alloc();
    if (outputs == NULL || inputs == NULL)
    {
        result = AVERROR(ENOMEM);
        goto fail;
    }

    outputs->name = av_strdup("in");
    outputs->filter_ctx = self->source;
    outputs->pad_idx = 0;
    outputs->next = NULL;
    inputs->name = av_strdup("out");
    inputs->filter_ctx = self->sink;
    inputs->pad_idx = 0;
    inputs->next = NULL;

    result = avfilter_graph_parse_ptr(self->graph, description, &inputs, &outputs, NULL);
    if (result < 0)
        goto fail;
    result = avfilter_graph_config(self->graph, NULL);
    if (result < 0)
        goto fail;

    avfilter_inout_free(&inputs);
    avfilter_inout_free(&outputs);
    g_free(description);
    self->backend = GST_HDR_TONE_MAP_BACKEND_CPU;
    GST_INFO_OBJECT(self, "Using CPU HDR tone mapping");
    return TRUE;

fail:
    GST_ERROR_OBJECT(self, "Could not create HDR tone-mapping graph: %s",
        gst_hdr_tone_map_av_error(result, error_buffer, sizeof(error_buffer)));
    avfilter_inout_free(&inputs);
    avfilter_inout_free(&outputs);
    g_free(description);
    gst_hdr_tone_map_clear_graph_unlocked(self);
    return FALSE;
}

static gboolean gst_hdr_tone_map_build_graph_unlocked(GstHdrToneMap *self)
{
    if (self->use_opencl)
    {
        if (!self->opencl_failed && gst_hdr_tone_map_build_opencl_graph_unlocked(self))
            return TRUE;

        self->opencl_failed = TRUE;
    }
    return gst_hdr_tone_map_build_cpu_graph_unlocked(self);
}

static gint gst_hdr_tone_map_process_frame_unlocked(GstHdrToneMap *self,
    AVFrame *input_frame, AVFrame *output_frame)
{
    gint result = av_buffersrc_add_frame_flags(self->source, input_frame,
        AV_BUFFERSRC_FLAG_KEEP_REF);

    if (result >= 0)
        result = av_buffersink_get_frame(self->sink, output_frame);

    if (result >= 0 || self->backend != GST_HDR_TONE_MAP_BACKEND_OPENCL)
        return result;

    GST_INFO_OBJECT(self, "OpenCL HDR tone mapping failed during processing; switching to CPU: %d",
        result);
    self->opencl_failed = TRUE;
    av_frame_unref(output_frame);
    if (!gst_hdr_tone_map_build_cpu_graph_unlocked(self))
        return result;

    result = av_buffersrc_add_frame_flags(self->source, input_frame,
        AV_BUFFERSRC_FLAG_KEEP_REF);
    if (result >= 0)
        result = av_buffersink_get_frame(self->sink, output_frame);
    return result;
}

static GstCaps *gst_hdr_tone_map_transform_caps(GstBaseTransform *transform,
    GstPadDirection direction, GstCaps *caps, GstCaps *filter)
{
    const gchar *other_pad = direction == GST_PAD_SINK ? "src" : "sink";
    GstPadTemplate *other_template = gst_element_class_get_pad_template(
        GST_ELEMENT_GET_CLASS(transform), other_pad);
    GstCaps *template_caps = gst_pad_template_get_caps(other_template);
    GstCaps *stripped;
    GstCaps *result;
    guint index;

    if (gst_caps_is_any(caps))
        result = gst_caps_ref(template_caps);
    else
    {
        stripped = gst_caps_copy(caps);
        for (index = 0; index < gst_caps_get_size(stripped); index++)
        {
            GstStructure *structure = gst_caps_get_structure(stripped, index);
            gst_structure_remove_fields(structure, "format", "colorimetry", "chroma-site", NULL);
        }
        result = gst_caps_intersect_full(template_caps, stripped, GST_CAPS_INTERSECT_FIRST);
        gst_caps_unref(stripped);
    }

    if (filter != NULL)
    {
        GstCaps *intersection = gst_caps_intersect_full(filter, result, GST_CAPS_INTERSECT_FIRST);
        gst_caps_unref(result);
        result = intersection;
    }
    return result;
}

static gboolean gst_hdr_tone_map_set_caps(GstBaseTransform *transform,
    GstCaps *input_caps, GstCaps *output_caps)
{
    GstHdrToneMap *self = GST_HDR_TONE_MAP(transform);
    gboolean success = FALSE;

    g_mutex_lock(&self->lock);
    if (!gst_video_info_from_caps(&self->input_info, input_caps) ||
        !gst_video_info_from_caps(&self->output_info, output_caps))
        goto done;

    self->input_pixel_format = gst_hdr_tone_map_pixel_format(
        GST_VIDEO_INFO_FORMAT(&self->input_info));
    if (self->input_pixel_format == AV_PIX_FMT_NONE ||
        GST_VIDEO_INFO_FORMAT(&self->output_info) != GST_VIDEO_FORMAT_I420)
        goto done;

    success = gst_hdr_tone_map_build_graph_unlocked(self);
done:
    if (!success)
        gst_hdr_tone_map_clear_graph_unlocked(self);
    g_mutex_unlock(&self->lock);
    return success;
}

static GstFlowReturn gst_hdr_tone_map_prepare_output_buffer(GstBaseTransform *transform,
    GstBuffer *input, GstBuffer **output)
{
    GstHdrToneMap *self = GST_HDR_TONE_MAP(transform);
    gsize output_size = GST_VIDEO_INFO_SIZE(&self->output_info);
    if (output_size == 0)
        return GST_FLOW_NOT_NEGOTIATED;

    *output = gst_buffer_new_allocate(NULL, output_size, NULL);
    if (*output == NULL)
        return GST_FLOW_ERROR;

    gst_buffer_copy_into(*output, input,
        GST_BUFFER_COPY_FLAGS | GST_BUFFER_COPY_TIMESTAMPS, 0, (gsize)-1);
    return GST_FLOW_OK;
}

static GstFlowReturn gst_hdr_tone_map_transform(GstBaseTransform *transform,
    GstBuffer *input_buffer, GstBuffer *output_buffer)
{
    GstHdrToneMap *self = GST_HDR_TONE_MAP(transform);
    GstVideoFrame input_video = { 0 };
    GstVideoFrame output_video = { 0 };
    AVFrame *input_frame = NULL;
    AVFrame *output_frame = NULL;
    const uint8_t *source_data[4] = { NULL, NULL, NULL, NULL };
    int source_stride[4] = { 0, 0, 0, 0 };
    uint8_t *destination_data[4] = { NULL, NULL, NULL, NULL };
    int destination_stride[4] = { 0, 0, 0, 0 };
    const uint8_t *filtered_data[4] = { NULL, NULL, NULL, NULL };
    GstFlowReturn flow = GST_FLOW_ERROR;
    gboolean input_mapped = FALSE;
    gboolean output_mapped = FALSE;
    guint plane;
    gint result;

    g_mutex_lock(&self->lock);
    if (self->graph == NULL)
    {
        flow = GST_FLOW_NOT_NEGOTIATED;
        goto done;
    }

    input_mapped = gst_video_frame_map(&input_video, &self->input_info,
        input_buffer, GST_MAP_READ);
    output_mapped = gst_video_frame_map(&output_video, &self->output_info,
        output_buffer, GST_MAP_WRITE);
    if (!input_mapped || !output_mapped)
        goto done;

    input_frame = av_frame_alloc();
    output_frame = av_frame_alloc();
    if (input_frame == NULL || output_frame == NULL)
        goto done;

    input_frame->format = self->input_pixel_format;
    input_frame->width = GST_VIDEO_INFO_WIDTH(&self->input_info);
    input_frame->height = GST_VIDEO_INFO_HEIGHT(&self->input_info);
    input_frame->color_primaries = AVCOL_PRI_BT2020;
    input_frame->colorspace = AVCOL_SPC_BT2020_NCL;
    input_frame->color_range = AVCOL_RANGE_MPEG;
    input_frame->color_trc = g_strcmp0(gst_hdr_tone_map_transfer_name(self),
        "arib-std-b67") == 0 ? AVCOL_TRC_ARIB_STD_B67 : AVCOL_TRC_SMPTE2084;
    input_frame->pts = GST_BUFFER_PTS_IS_VALID(input_buffer)
        ? (int64_t)GST_BUFFER_PTS(input_buffer) : AV_NOPTS_VALUE;

    result = av_frame_get_buffer(input_frame, 32);
    if (result < 0)
        goto done;

    for (plane = 0; plane < GST_VIDEO_FRAME_N_PLANES(&input_video) && plane < 4; plane++)
    {
        source_data[plane] = GST_VIDEO_FRAME_PLANE_DATA(&input_video, plane);
        source_stride[plane] = GST_VIDEO_FRAME_PLANE_STRIDE(&input_video, plane);
    }
    av_image_copy(input_frame->data, input_frame->linesize,
        source_data, source_stride, self->input_pixel_format,
        input_frame->width, input_frame->height);

    result = gst_hdr_tone_map_process_frame_unlocked(self, input_frame, output_frame);
    if (result < 0 || output_frame->format != AV_PIX_FMT_YUV420P)
        goto done;

    for (plane = 0; plane < GST_VIDEO_FRAME_N_PLANES(&output_video) && plane < 4; plane++)
    {
        destination_data[plane] = GST_VIDEO_FRAME_PLANE_DATA(&output_video, plane);
        destination_stride[plane] = GST_VIDEO_FRAME_PLANE_STRIDE(&output_video, plane);
        filtered_data[plane] = output_frame->data[plane];
    }
    av_image_copy(destination_data, destination_stride,
        filtered_data, output_frame->linesize, AV_PIX_FMT_YUV420P,
        output_frame->width, output_frame->height);
    flow = GST_FLOW_OK;

done:
    av_frame_free(&input_frame);
    av_frame_free(&output_frame);
    if (input_mapped)
        gst_video_frame_unmap(&input_video);
    if (output_mapped)
        gst_video_frame_unmap(&output_video);
    g_mutex_unlock(&self->lock);
    return flow;
}

static gboolean gst_hdr_tone_map_stop(GstBaseTransform *transform)
{
    GstHdrToneMap *self = GST_HDR_TONE_MAP(transform);
    g_mutex_lock(&self->lock);
    gst_hdr_tone_map_clear_graph_unlocked(self);
    g_mutex_unlock(&self->lock);
    return TRUE;
}

static void gst_hdr_tone_map_set_property(GObject *object, guint property_id,
    const GValue *value, GParamSpec *specification)
{
    GstHdrToneMap *self = GST_HDR_TONE_MAP(object);
    g_mutex_lock(&self->lock);
    switch (property_id)
    {
        case PROP_TRANSFER:
            self->transfer = g_value_get_enum(value);
            break;
        case PROP_PEAK_NITS:
            self->peak_nits = g_value_get_double(value);
            break;
        case PROP_USE_OPENCL:
            self->use_opencl = g_value_get_boolean(value);
            if (self->use_opencl)
                self->opencl_failed = FALSE;
            break;
        default:
            G_OBJECT_WARN_INVALID_PROPERTY_ID(object, property_id, specification);
            break;
    }
    gst_hdr_tone_map_clear_graph_unlocked(self);
    g_mutex_unlock(&self->lock);
}

static void gst_hdr_tone_map_get_property(GObject *object, guint property_id,
    GValue *value, GParamSpec *specification)
{
    GstHdrToneMap *self = GST_HDR_TONE_MAP(object);
    switch (property_id)
    {
        case PROP_TRANSFER:
            g_value_set_enum(value, self->transfer);
            break;
        case PROP_PEAK_NITS:
            g_value_set_double(value, self->peak_nits);
            break;
        case PROP_USE_OPENCL:
            g_value_set_boolean(value, self->use_opencl);
            break;
        case PROP_ACTIVE_BACKEND:
            g_value_set_enum(value, self->backend);
            break;
        default:
            G_OBJECT_WARN_INVALID_PROPERTY_ID(object, property_id, specification);
            break;
    }
}

static void gst_hdr_tone_map_finalize(GObject *object)
{
    GstHdrToneMap *self = GST_HDR_TONE_MAP(object);
    gst_hdr_tone_map_clear_graph_unlocked(self);
    g_mutex_clear(&self->lock);
    G_OBJECT_CLASS(gst_hdr_tone_map_parent_class)->finalize(object);
}

static void gst_hdr_tone_map_class_init(GstHdrToneMapClass *klass)
{
    GObjectClass *object_class = G_OBJECT_CLASS(klass);
    GstElementClass *element_class = GST_ELEMENT_CLASS(klass);
    GstBaseTransformClass *transform_class = GST_BASE_TRANSFORM_CLASS(klass);

    object_class->set_property = gst_hdr_tone_map_set_property;
    object_class->get_property = gst_hdr_tone_map_get_property;
    object_class->finalize = gst_hdr_tone_map_finalize;

    g_object_class_install_property(object_class, PROP_TRANSFER,
        g_param_spec_enum("transfer", "Input transfer",
            "HDR input transfer function", gst_hdr_tone_map_transfer_get_type(),
            GST_HDR_TONE_MAP_TRANSFER_AUTO,
            G_PARAM_READWRITE | G_PARAM_STATIC_STRINGS));
    g_object_class_install_property(object_class, PROP_PEAK_NITS,
        g_param_spec_double("peak-nits", "Reference luminance",
            "Reference luminance used by the CPU fallback",
            90.0, 10000.0, 90.0,
            G_PARAM_READWRITE | G_PARAM_STATIC_STRINGS));
    g_object_class_install_property(object_class, PROP_USE_OPENCL,
        g_param_spec_boolean("use-opencl", "Use OpenCL",
            "Use OpenCL when available, with automatic CPU fallback",
            TRUE, G_PARAM_READWRITE | G_PARAM_STATIC_STRINGS));
    g_object_class_install_property(object_class, PROP_ACTIVE_BACKEND,
        g_param_spec_enum("active-backend", "Active backend",
            "Tone-mapping backend selected after caps negotiation",
            gst_hdr_tone_map_backend_get_type(), GST_HDR_TONE_MAP_BACKEND_NONE,
            G_PARAM_READABLE | G_PARAM_STATIC_STRINGS));

    gst_element_class_set_static_metadata(element_class,
        "HDR to SDR Hable tone mapper", "Filter/Converter/Video",
        "Converts PQ or HLG BT.2020 video to SDR BT.709 with OpenCL or CPU Hable",
        "NextGen");
    gst_element_class_add_static_pad_template(element_class, &sink_template);
    gst_element_class_add_static_pad_template(element_class, &source_template);

    transform_class->transform_caps = gst_hdr_tone_map_transform_caps;
    transform_class->set_caps = gst_hdr_tone_map_set_caps;
    transform_class->prepare_output_buffer = gst_hdr_tone_map_prepare_output_buffer;
    transform_class->transform = gst_hdr_tone_map_transform;
    transform_class->stop = gst_hdr_tone_map_stop;
}

static void gst_hdr_tone_map_init(GstHdrToneMap *self)
{
    g_mutex_init(&self->lock);
    gst_video_info_init(&self->input_info);
    gst_video_info_init(&self->output_info);
    self->input_pixel_format = AV_PIX_FMT_NONE;
    self->transfer = GST_HDR_TONE_MAP_TRANSFER_AUTO;
    self->backend = GST_HDR_TONE_MAP_BACKEND_NONE;
    self->peak_nits = 90.0;
    self->use_opencl = TRUE;
    self->opencl_failed = FALSE;
    gst_base_transform_set_in_place(GST_BASE_TRANSFORM(self), FALSE);
    gst_base_transform_set_passthrough(GST_BASE_TRANSFORM(self), FALSE);
}

static gboolean plugin_init(GstPlugin *plugin)
{
    const gchar *required_filters[] = { "buffer", "buffersink", "format", "zscale", "tonemap" };
    guint index;

    GST_DEBUG_CATEGORY_INIT(gst_hdr_tone_map_debug, "hdrtonemap", 0,
        "HDR to SDR tone mapping");
    for (index = 0; index < G_N_ELEMENTS(required_filters); index++)
    {
        if (avfilter_get_by_name(required_filters[index]) == NULL)
        {
            GST_ERROR("Required FFmpeg filter '%s' is unavailable", required_filters[index]);
            return FALSE;
        }
    }
    return gst_element_register(plugin, "hdrtonemap", GST_RANK_NONE, GST_TYPE_HDR_TONE_MAP);
}

GST_PLUGIN_DEFINE(GST_VERSION_MAJOR, GST_VERSION_MINOR, hdrtonemap,
    "HDR to SDR OpenCL and CPU Hable tone mapper", plugin_init,
    "0.2.0", "LGPL", "NextGen", "https://github.com/")
