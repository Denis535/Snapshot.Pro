namespace Snapshot.Pro;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using FFmpeg.AutoGen.Abstractions;
using FFmpeg.AutoGen.Bindings.DynamicallyLoaded;

internal unsafe sealed class VideoEncoder : IDisposable {

    private readonly AVCodecContext* context;

    private int Width { get; }
    private int Height { get; }
    private AVPixelFormat Format { get; }
    private int Fps { get; }

    private AVCodecContext* Context { get => context; init => context = value; }

    public VideoEncoder(int width, int height, AVPixelFormat format, int fps) {
        DynamicallyLoadedBindings.LibrariesPath = Path.Combine( "C:\\FFmpeg", "bin", Environment.Is64BitProcess ? "x64" : "x86" );
        DynamicallyLoadedBindings.ThrowErrorIfFunctionNotFound = true;
        DynamicallyLoadedBindings.Initialize();

        Width = width;
        Height = height;
        Format = format;
        Fps = fps;

        var codec = ffmpeg.avcodec_find_encoder( AVCodecID.AV_CODEC_ID_H264 );
        if (codec == null) throw new Exception( $"AVCodec {AVCodecID.AV_CODEC_ID_H264} was not found" );

        Context = ffmpeg.avcodec_alloc_context3( codec );
        if (Context == null) throw new NullReferenceException( "AVCodecContext is null" );
        Context->width = width;
        Context->height = height;
        Context->pix_fmt = format;
        Context->time_base = new AVRational() { num = 1, den = fps };
        ffmpeg.av_opt_set( Context->priv_data, "preset", "veryfast", 0 ); // https://trac.ffmpeg.org/wiki/Encode/H.264
        ffmpeg.av_opt_set( Context->priv_data, "tune", "stillimage", 0 );
        ffmpeg.av_opt_set( Context->priv_data, "crf", "10", 0 );

        ThrowIfError( ffmpeg.avcodec_open2( Context, codec, null ) );
    }

    public void Dispose() {
        fixed (AVCodecContext** ptr = &context) ffmpeg.avcodec_free_context( ptr );
    }

    public void Add(Stream stream, AVFrame frame) {
        if (frame.width != Width) throw new ArgumentException( $"Argument 'frame' (width) is invalid" );
        if (frame.height != Height) throw new ArgumentException( $"Argument 'frame' (height) is invalid" );
        if (frame.format != (int) Format) throw new ArgumentException( $"Argument 'frame' (format) is invalid" );
        if (frame.data[ 1 ] - frame.data[ 0 ] < Width * Height) throw new ArgumentException( $"Argument 'frame' (data) is invalid" );
        if (frame.data[ 2 ] - frame.data[ 1 ] < (Width / 2) * (Height / 2)) throw new ArgumentException( $"Argument 'frame' (data) is invalid" );
        if (frame.linesize[ 0 ] < Width) throw new ArgumentException( $"Argument 'frame' (linesize) is invalid" );
        if (frame.linesize[ 1 ] < Width / 2) throw new ArgumentException( $"Argument 'frame' (linesize) is invalid" );
        if (frame.linesize[ 2 ] < Width / 2) throw new ArgumentException( $"Argument 'frame' (linesize) is invalid" );

        var packet = ffmpeg.av_packet_alloc();
        try {
            ThrowIfError( ffmpeg.avcodec_send_frame( Context, &frame ) );
            while (true) {
                var response = ffmpeg.avcodec_receive_packet( Context, packet );
                if (response == 0) {
                    using (var stream_ = new UnmanagedMemoryStream( packet->data, packet->size )) {
                        stream_.CopyTo( stream );
                    }
                    continue;
                }
                if (response == ffmpeg.AVERROR( ffmpeg.EAGAIN )) {
                    break;
                }
                if (response == ffmpeg.AVERROR( ffmpeg.AVERROR_EOF )) {
                    break;
                }
                throw new Exception( response.ToString() );
            }
        } finally {
            ffmpeg.av_packet_free( &packet );
        }
    }

    public void Flush(Stream stream) {
        var packet = ffmpeg.av_packet_alloc();
        try {
            ThrowIfError( ffmpeg.avcodec_send_frame( Context, null ) );
            while (true) {
                var response = ffmpeg.avcodec_receive_packet( Context, packet );
                if (response == 0) {
                    using (var stream_ = new UnmanagedMemoryStream( packet->data, packet->size )) {
                        stream_.CopyTo( stream );
                    }
                    continue;
                }
                if (response == ffmpeg.AVERROR( ffmpeg.AVERROR_EOF )) {
                    break;
                }
                //throw new Exception( response.ToString() );
                break;
            }
        } finally {
            ffmpeg.av_packet_free( &packet );
        }
    }

    // Helpers
    private static void ThrowIfError(int error) {
        if (error < 0) {
            var buffer = stackalloc byte[ 1024 ];
            if (ffmpeg.av_strerror( error, buffer, 1024 ) == 0) {
                var message = Marshal.PtrToStringAnsi( (IntPtr) buffer );
                throw new Exception( $"{error} ({message})" );
            } else {
                throw new Exception( error.ToString() );
            }
        }
    }

}
