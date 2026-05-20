namespace Shared.Models.AppConf;

public class PoolConf
{
    public int BufferMax { get; set; }

    public int BufferSize { get; set; }

    public int BufferValidityMinutes { get; set; }


    public int BufferByteTinyMaxCount { get; set; }
    public int BufferByteExtraSmallMaxCount { get; set; }
    public int BufferByteSmallMaxCount { get; set; }
    public int BufferByteMediumMaxCount { get; set; }
    public int BufferByteLargeMaxCount { get; set; }


    public int BufferCharTinyMaxCount { get; set; }
    public int BufferCharExtraSmallMaxCount { get; set; }
    public int BufferCharSmallMaxCount { get; set; }
    public int BufferCharMediumMaxCount { get; set; }
    public int BufferCharLargeMaxCount { get; set; }


    public int BufferWriterSmallMaxCount { get; set; }
    public int BufferWriterLargeMaxCount { get; set; }


    public int StringBuilderSmallMaxCount { get; set; }
    public int StringBuilderLargeMaxCount { get; set; }
}
