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

internal unsafe class VideoEncoder : IDisposable {

    internal readonly RenderTargetBitmap renderTargetBitmap;
    internal readonly byte[] pixels;

    private Stream Stream { get; }
    private FrameConverter Converter { get; }
    private VideoEncoderInternal Encoder { get; }

    public VideoEncoder(Stream stream, int width, int height, int fps) {
        DynamicallyLoadedBindings.LibrariesPath = Path.Combine( "C:\\FFmpeg", "bin", Environment.Is64BitProcess ? "x64" : "x86" );
        DynamicallyLoadedBindings.ThrowErrorIfFunctionNotFound = true;
        DynamicallyLoadedBindings.Initialize();
        renderTargetBitmap = new RenderTargetBitmap( width, height, 96, 96, PixelFormats.Pbgra32 );
        pixels = new byte[ width * height * 4 ];
        Stream = stream;
        Converter = new FrameConverter( width, height, AVPixelFormat.AV_PIX_FMT_BGRA, width, height, AVPixelFormat.AV_PIX_FMT_YUV420P );
        Encoder = new VideoEncoderInternal( width, height, AVPixelFormat.AV_PIX_FMT_YUV420P, fps );
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

    public void Add(AVFrame frame) {
        Encoder.Encode( Stream, Converter.Convert( frame ) );
    }

    public void Flush() {
        Encoder.Flush( Stream );
    }

}
internal static class VideoEncoderExtensions {

    public static void AddVideoSnapshot(this VideoEncoder encoder, FrameworkElement element, IWpfTextView view, IWpfTextViewMargin margin) {
        ThreadHelper.ThrowIfNotOnUIThread();
        view.ViewportLeft = 0;
        view.DisplayTextLineContainingBufferPosition( new SnapshotPoint( view.TextSnapshot, 0 ), 0, ViewRelativePosition.Top );
        view.Caret.MoveTo( new SnapshotPoint( view.TextSnapshot, 0 ), PositionAffinity.Predecessor );
        var frame = 0;
        for (var i = 0; i < 60 * 3; i++) {
            encoder.AddSnapshot( element, frame, 0, view, margin );
            frame++;
        }
        for (var i = 0; view.TextViewLines.LastVisibleLine.End.Position < view.TextSnapshot.Length; i++) {
            encoder.AddSnapshot( element, frame, 0, view, margin );
            frame++;
            view.ViewScroller.ScrollViewportVerticallyByPixels( -Math.Min( (double) i / (60 * 3), 1 ) );
        }
        for (var i = 0; i < 60 * 3; i++) {
            encoder.AddSnapshot( element, frame, 0, view, margin );
            frame++;
        }
    }

    public static void AddSnapshot(this VideoEncoder encoder, FrameworkElement element, int frame, int duration, IWpfTextView view, IWpfTextViewMargin margin) {
        ThreadHelper.ThrowIfNotOnUIThread();
        view.VisualElement.UpdateLayout();
        UpdateLineNumbers( margin );
        encoder.renderTargetBitmap.Render( element );
        encoder.Add( encoder.renderTargetBitmap, frame, duration );
    }

    // Helpers
    private static void UpdateLineNumbers(IWpfTextViewMargin margin) {
        var method = margin.GetType().GetMethod( "UpdateLineNumbers", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic );
        method.Invoke( margin, [] );
    }

}
