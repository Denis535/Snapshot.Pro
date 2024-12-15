namespace Snapshot.Pro;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using FFmpeg.AutoGen.Abstractions;
using FFmpeg.AutoGen.Bindings.DynamicallyLoaded;

internal unsafe class VideoEncoder2 : IDisposable {

    private readonly byte[] pixels;

    private Stream Stream { get; }
    private FrameConverter VideoFrameConverter { get; }
    private VideoEncoder VideoEncoder { get; }

    public VideoEncoder2(Stream stream, int width, int height, int fps) {
        DynamicallyLoadedBindings.LibrariesPath = Path.Combine( "C:\\FFmpeg", "bin", Environment.Is64BitProcess ? "x64" : "x86" );
        DynamicallyLoadedBindings.ThrowErrorIfFunctionNotFound = true;
        DynamicallyLoadedBindings.Initialize();
        pixels = new byte[ width * height * 4 ];
        Stream = stream;
        VideoFrameConverter = new FrameConverter( width, height, AVPixelFormat.AV_PIX_FMT_BGRA, width, height, AVPixelFormat.AV_PIX_FMT_YUV420P );
        VideoEncoder = new VideoEncoder( width, height, AVPixelFormat.AV_PIX_FMT_YUV420P, fps );
    }

    public void Dispose() {
        VideoEncoder.Dispose();
        VideoFrameConverter.Dispose();
    }

    public void Add(BitmapSource bitmap, int frame, int duration) {
        bitmap.CopyPixels( pixels, bitmap.PixelWidth * 4, 0 );
        fixed (byte* pixels_ = pixels) {
            var frame_ = new AVFrame() {
                data = new byte_ptr8() {
                    [ 0 ] = pixels_
                },
                linesize = new int8() {
                    [ 0 ] = bitmap.PixelWidth * 4
                },
                width = bitmap.PixelWidth,
                height = bitmap.PixelHeight,
                format = (int) AVPixelFormat.AV_PIX_FMT_BGRA,
                pts = frame,
                duration = duration
            };
            VideoEncoder.Add( Stream, VideoFrameConverter.Convert( frame_ ) );
        }
    }

    public void Flush() {
        VideoEncoder.Flush( Stream );
    }

}
