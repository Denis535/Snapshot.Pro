namespace Snapshot.Pro;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FFmpeg.AutoGen.Abstractions;
using FFmpeg.AutoGen.Bindings.DynamicallyLoaded;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;

internal unsafe class VideoRecorder : IDisposable {

    private readonly RenderTargetBitmap renderTargetBitmap;
    private readonly byte[] pixels;

    private Stream Stream { get; }
    private FrameConverter Converter { get; }
    private VideoEncoder Encoder { get; }

    static VideoRecorder() {
        DynamicallyLoadedBindings.LibrariesPath = Path.Combine( Path.GetDirectoryName( typeof( VideoRecorder ).Assembly.Location ), "FFmpeg", "bin", Environment.Is64BitProcess ? "x64" : "x86" );
        //Debug.WriteLine( "FFmpeg: " + DynamicallyLoadedBindings.LibrariesPath );
        DynamicallyLoadedBindings.ThrowErrorIfFunctionNotFound = true;
        DynamicallyLoadedBindings.Initialize();
    }

    public VideoRecorder(string path, int width, int height, int fps) {
        renderTargetBitmap = new RenderTargetBitmap( width, height, 96, 96, PixelFormats.Pbgra32 );
        pixels = new byte[ width * height * 4 ];
        Stream = File.Create( path );
        Converter = new FrameConverter( width, height, AVPixelFormat.AV_PIX_FMT_BGRA, width, height, AVPixelFormat.AV_PIX_FMT_YUV420P );
        Encoder = new VideoEncoder( width, height, AVPixelFormat.AV_PIX_FMT_YUV420P, fps );
    }

    public void Dispose() {
        Encoder.Flush( Stream );
        Stream.Flush();
        Encoder.Dispose();
        Converter.Dispose();
        Stream.Dispose();
    }

    public void AddSnapshot(FrameworkElement element, int frame, int duration) {
        renderTargetBitmap.Render( element );
        AddFrame( renderTargetBitmap, frame, duration );
    }

    private void AddFrame(BitmapSource bitmap, int frame, int duration) {
        bitmap.CopyPixels( pixels, bitmap.PixelWidth * 4, 0 );
        fixed (byte* pixels_ = pixels) {
            var frame2 = new AVFrame() {
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
                pkt_dts = frame,
                duration = duration
            };
            Encoder.Encode( Stream, Converter.Convert( frame2 ) );
        }
    }

}
internal static class VideoRecorderExtensions {

    public static void AddVideoSnapshot(this VideoRecorder recorder, FrameworkElement element, IWpfTextView view, IWpfTextViewMargin margin) {
        view.ViewportLeft = 0;
        view.DisplayTextLineContainingBufferPosition( new SnapshotPoint( view.TextSnapshot, 0 ), 0, ViewRelativePosition.Top );
        view.Caret.MoveTo( new SnapshotPoint( view.TextSnapshot, 0 ), PositionAffinity.Predecessor );
        var frame = 0;
        for (var i = 0; i < 60 * 3; i++) {
            recorder.AddSnapshot( element, frame, 0, view, margin );
            frame++;
        }
        for (var i = 0; view.TextViewLines.LastVisibleLine.End.Position < view.TextSnapshot.Length; i++) {
            recorder.AddSnapshot( element, frame, 0, view, margin );
            frame++;
            view.ViewScroller.ScrollViewportVerticallyByPixels( -Math.Min( (double) i / (60 * 3), 1 ) );
        }
        for (var i = 0; i < 60 * 3; i++) {
            recorder.AddSnapshot( element, frame, 0, view, margin );
            frame++;
        }
    }

    public static void AddSnapshot(this VideoRecorder recorder, FrameworkElement element, int frame, int duration, IWpfTextView view, IWpfTextViewMargin margin) {
        view.VisualElement.UpdateLayout();
        UpdateLineNumbers( margin );
        recorder.AddSnapshot( element, frame, duration );
        static void UpdateLineNumbers(IWpfTextViewMargin margin) {
            var method = margin.GetType().GetMethod( "UpdateLineNumbers", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic );
            method.Invoke( margin, [] );
        }
    }

}
