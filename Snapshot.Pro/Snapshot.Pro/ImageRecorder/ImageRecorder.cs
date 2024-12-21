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

internal class ImageRecorder : IDisposable {

    private readonly RenderTargetBitmap renderTargetBitmap;

    private Stream Stream { get; }
    private PngBitmapEncoder Encoder { get; }

    public ImageRecorder(string path, int width, int height) {
        renderTargetBitmap = new RenderTargetBitmap( width, height, 96, 96, PixelFormats.Pbgra32 );
        Stream = File.Create( path );
        Encoder = new PngBitmapEncoder();
    }

    public void Dispose() {
        Encoder.Save( Stream );
        Stream.Flush();
        Stream.Dispose();
    }

    public void AddSnapshot(FrameworkElement element) {
        renderTargetBitmap.Render( element );
        Encoder.Frames.Add( BitmapFrame.Create( renderTargetBitmap ) );
    }

}
