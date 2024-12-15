namespace Snapshot.Pro;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FFmpeg.AutoGen.Abstractions;
using FFmpeg.AutoGen.Bindings.DynamicallyLoaded;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;

internal unsafe class VideoRecorder : IDisposable {

    internal readonly RenderTargetBitmap renderTargetBitmap;
    internal readonly byte[] pixels;

    private Stream Stream { get; }
    private FrameConverter Converter { get; }
    private VideoEncoder Encoder { get; }

    public VideoRecorder(Stream stream, int width, int height, int fps) {
        DynamicallyLoadedBindings.LibrariesPath = Path.Combine( "C:\\FFmpeg", "bin", Environment.Is64BitProcess ? "x64" : "x86" );
        DynamicallyLoadedBindings.ThrowErrorIfFunctionNotFound = true;
        DynamicallyLoadedBindings.Initialize();
        renderTargetBitmap = new RenderTargetBitmap( width, height, 96, 96, PixelFormats.Pbgra32 );
        pixels = new byte[ width * height * 4 ];
        Stream = stream;
        Converter = new FrameConverter( width, height, AVPixelFormat.AV_PIX_FMT_BGRA, width, height, AVPixelFormat.AV_PIX_FMT_YUV420P );
        Encoder = new VideoEncoder( width, height, AVPixelFormat.AV_PIX_FMT_YUV420P, fps );
    }

    public void Dispose() {
        Encoder.Dispose();
        Converter.Dispose();
    }

    public void Add(BitmapSource bitmap, int frame, int duration) {
        bitmap.CopyPixels( pixels, bitmap.PixelWidth * 4, 0 );
        fixed (byte* pixels_ = pixels) {
            Add( new AVFrame() {
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
            } );
        }
    }

    private void Add(AVFrame frame) {
        Encoder.Encode( Stream, Converter.Convert( frame ) );
    }

    public void Flush() {
        Encoder.Flush( Stream );
    }

}
internal static class VideoRecorderExtensions {

    public static void TakeVideoSnapshot(this VideoRecorder recorder, FrameworkElement element, IWpfTextView view, IWpfTextViewMargin margin) {
        ThreadHelper.ThrowIfNotOnUIThread();
        view.ViewportLeft = 0;
        view.DisplayTextLineContainingBufferPosition( new SnapshotPoint( view.TextSnapshot, 0 ), 0, ViewRelativePosition.Top );
        view.Caret.MoveTo( new SnapshotPoint( view.TextSnapshot, 0 ), PositionAffinity.Predecessor );
        var frame = 0;
        for (var i = 0; i < 60 * 3; i++) {
            recorder.TakeSnapshot( element, frame, 0, view, margin );
            frame++;
        }
        for (var i = 0; view.TextViewLines.LastVisibleLine.End.Position < view.TextSnapshot.Length; i++) {
            recorder.TakeSnapshot( element, frame, 0, view, margin );
            frame++;
            view.ViewScroller.ScrollViewportVerticallyByPixels( -Math.Min( (double) i / (60 * 3), 1 ) );
        }
        for (var i = 0; i < 60 * 3; i++) {
            recorder.TakeSnapshot( element, frame, 0, view, margin );
            frame++;
        }
    }

    public static void TakeSnapshot(this VideoRecorder recorder, FrameworkElement element, int frame, int duration, IWpfTextView view, IWpfTextViewMargin margin) {
        ThreadHelper.ThrowIfNotOnUIThread();
        view.VisualElement.UpdateLayout();
        UpdateLineNumbers( margin );
        recorder.renderTargetBitmap.Render( element );
        recorder.Add( recorder.renderTargetBitmap, frame, duration );
    }

    // Helpers
    private static void UpdateLineNumbers(IWpfTextViewMargin margin) {
        var method = margin.GetType().GetMethod( "UpdateLineNumbers", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic );
        method.Invoke( margin, [] );
    }

}
