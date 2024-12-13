namespace Snapshot.Pro;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using FFmpeg.AutoGen;

internal unsafe class VideoEncoder : IDisposable {

    private readonly AVCodecContext* codecContext;

    private Stream Stream { get; }
    public int Width { get; }
    public int Height { get; }

    private int LineSizeY { get; }
    private int LineSizeU { get; }
    private int LineSizeV { get; }

    private int SizeY { get; }
    private int SizeU { get; }

    private AVCodec* Codec { get; }
    private AVCodecContext* CodecContext { get => codecContext; init => codecContext = value; }

    public VideoEncoder(Stream stream, int width, int height, int fps) {
        Stream = stream;
        Width = width;
        Height = height;

        LineSizeY = width;
        LineSizeU = LineSizeV = width / 2;

        SizeY = width * height;
        SizeU = (width * height) / 2;

        ffmpeg.RootPath = Path.Combine( "C:\\FFmpeg", "bin", Environment.Is64BitProcess ? "x64" : "x86" );
        DynamicallyLoadedBindings.ThrowErrorIfFunctionNotFound = true;
        DynamicallyLoadedBindings.Initialize();
        Codec = ffmpeg.avcodec_find_encoder( AVCodecID.AV_CODEC_ID_H264 );
        if (Codec == null) throw new Exception( $"Codec {AVCodecID.AV_CODEC_ID_H264} was not found" );
        CodecContext = ffmpeg.avcodec_alloc_context3( Codec );
        CodecContext->width = width;
        CodecContext->height = height;
        CodecContext->time_base = new AVRational() { num = 1, den = fps };
        CodecContext->pix_fmt = AVPixelFormat.AV_PIX_FMT_YUV420P;
        ffmpeg.av_opt_set( CodecContext->priv_data, "preset", "veryslow", 0 );
        ThrowIfError( ffmpeg.avcodec_open2( CodecContext, Codec, null ) );
    }

    public void Dispose() {
        fixed (AVCodecContext** ptr = &codecContext) ffmpeg.avcodec_free_context( ptr );
    }

    public void Add(RenderTargetBitmap frame) {
        //using var codec = SKCodec.Create( frameFile );
        //var bitmap = SKBitmap.Decode( codec );
        //var bytes = new byte[];
        //frame.CopyPixels( bytes, 0, 0 );
        //var data = bitmap.Bytes;
        //fixed (byte* data_ = data) {
        //    Add( new AVFrame() {
        //        data = new byte_ptr8() {
        //            [ 0 ] = data_
        //        },
        //        linesize = new int8() {
        //            [ 0 ] = data.Length / frame.PixelHeight
        //        },
        //        height = frame.PixelHeight
        //    } );
        //}
    }

    public void Add(AVFrame frame) {
        if (frame.format != (int) CodecContext->pix_fmt) throw new ArgumentException( "Argument 'frame' is invalid" );
        if (frame.width != Width) throw new ArgumentException( "Argument 'frame' is invalid" );
        if (frame.height != Height) throw new ArgumentException( "Argument 'frame' is invalid" );
        if (frame.linesize[ 0 ] < LineSizeY) throw new ArgumentException( "Argument 'frame' is invalid" );
        if (frame.linesize[ 1 ] < LineSizeU) throw new ArgumentException( "Argument 'frame' is invalid" );
        if (frame.linesize[ 2 ] < LineSizeV) throw new ArgumentException( "Argument 'frame' is invalid" );
        if (frame.data[ 1 ] - frame.data[ 0 ] < SizeY) throw new ArgumentException( "Argument 'frame' is invalid" );
        if (frame.data[ 2 ] - frame.data[ 1 ] < SizeU) throw new ArgumentException( "Argument 'frame' is invalid" );

        var packet = ffmpeg.av_packet_alloc();
        try {
            ThrowIfError( ffmpeg.avcodec_send_frame( CodecContext, &frame ) );
            while (true) {
                var response = ffmpeg.avcodec_receive_packet( CodecContext, packet );
                if (response == 0) {
                    using (var stream = new UnmanagedMemoryStream( packet->data, packet->size )) {
                        stream.CopyTo( Stream );
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

    public void Flush() {
        var packet = ffmpeg.av_packet_alloc();
        try {
            ThrowIfError( ffmpeg.avcodec_send_frame( CodecContext, null ) );
            while (true) {
                var response = ffmpeg.avcodec_receive_packet( CodecContext, packet );
                if (response == 0) {
                    using (var stream = new UnmanagedMemoryStream( packet->data, packet->size )) {
                        stream.CopyTo( Stream );
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
                throw new Exception( message );
            } else {
                throw new Exception( error.ToString() );
            }
        }
    }

}
