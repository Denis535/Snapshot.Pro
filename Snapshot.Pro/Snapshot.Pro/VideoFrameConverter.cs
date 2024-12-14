namespace Snapshot.Pro;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using FFmpeg.AutoGen.Abstractions;

internal unsafe sealed class VideoFrameConverter : IDisposable {

    private IntPtr frameBuffer;
    private byte_ptr4 destData;
    private int4 destLineSize;

    private int SrcWidth { get; }
    private int SrcHeight { get; }
    private AVPixelFormat SrcFormat { get; }

    private int DestWidth { get; }
    private int DestHeight { get; }
    private AVPixelFormat DestFormat { get; }

    private SwsContext* Context { get; }

    public VideoFrameConverter(int srcWidth, int srcHeight, AVPixelFormat srcFormat, int destWidth, int destHeight, AVPixelFormat destFormat) {
        frameBuffer = Marshal.AllocHGlobal( ffmpeg.av_image_get_buffer_size( destFormat, destWidth, destHeight, 1 ) );
        destData = new byte_ptr4();
        destLineSize = new int4();
        ffmpeg.av_image_fill_arrays( ref destData, ref destLineSize, (byte*) frameBuffer, destFormat, destWidth, destHeight, 1 );

        SrcWidth = srcWidth;
        SrcHeight = srcHeight;
        SrcFormat = srcFormat;

        DestWidth = destWidth;
        DestHeight = destHeight;
        DestFormat = destFormat;

        Context = ffmpeg.sws_getContext( srcWidth, srcHeight, srcFormat, destWidth, destHeight, destFormat, ffmpeg.SWS_FAST_BILINEAR, null, null, null );
        if (Context == null) throw new NullReferenceException( "SwsContext is null" );
    }

    public void Dispose() {
        Marshal.FreeHGlobal( frameBuffer );
        ffmpeg.sws_freeContext( Context );
    }

    public AVFrame Convert(AVFrame frame) {
        if (frame.width != SrcWidth) throw new ArgumentException( $"Argument 'frame' (width) is invalid" );
        if (frame.height != SrcHeight) throw new ArgumentException( $"Argument 'frame' (height) is invalid" );
        if (frame.format != (int) SrcFormat) throw new ArgumentException( $"Argument 'frame' (format) is invalid" );

        ffmpeg.sws_scale( Context,
            frame.data, frame.linesize, 0, frame.height,
            this.destData, this.destLineSize );

        var destData = new byte_ptr8();
        destData.UpdateFrom( destData );

        var destLineSize = new int8();
        destLineSize.UpdateFrom( destLineSize );

        return frame with {
            data = destData,
            linesize = destLineSize,
            width = DestWidth,
            height = DestHeight,
        };
    }

}
