namespace Snapshot.Pro;
using System;
using System.Collections;
using System.Collections.Generic;
using Microsoft.VisualStudio.Shell;
using Community.VisualStudio.Toolkit;
using System.Threading.Tasks;
using System.IO;
using Microsoft.VisualStudio.Text.Editor;
using System.Windows.Media.Imaging;
using System.Windows;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Text;
using System.Windows.Media;

[Command( PackageIds.TakeSnapshotCommand )]
internal sealed class TakeSnapshotCommand : BaseCommand<TakeSnapshotCommand> {

    protected override async Task ExecuteAsync(OleMenuCmdEventArgs e) {
        await Package.JoinableTaskFactory.SwitchToMainThreadAsync();
        var documentView = await VS.Documents.GetActiveDocumentViewAsync();
        var document = documentView.Document;
        var view = documentView.TextView;

        try {
            var path = $"D:/Snapshots/{DateTime.UtcNow.Ticks}-{Path.GetFileNameWithoutExtension( document.FilePath ).Replace( ".", "_" )}.gif";
            TakeSnapshot( path, view );
            await VS.MessageBox.ShowAsync( "Snapshot.Pro", $"Snapshot was saved: {path}" );
        } catch (Exception ex) {
            await VS.MessageBox.ShowErrorAsync( "Snapshot.Pro", ex.Message );
        }
    }

    // TakeSnapshot
    private static void TakeSnapshot(string path, IWpfTextView view) {
        ThreadHelper.ThrowIfNotOnUIThread();
        Directory.CreateDirectory( Path.GetDirectoryName( path ) );
        using (var stream = File.Create( path )) {
            var encoder = new GifBitmapEncoder();
            TakeSnapshot( encoder, view );
            encoder.Save( stream );
        }
    }
    private static void TakeSnapshot(BitmapEncoder encoder, IWpfTextView view) {
        ThreadHelper.ThrowIfNotOnUIThread();
        var element = GetMainWindow( view );
        {
            view.ViewportLeft = 0;
            view.DisplayTextLineContainingBufferPosition( new SnapshotPoint( view.TextSnapshot, 0 ), 0, ViewRelativePosition.Top );
            view.Caret.MoveTo( new SnapshotPoint( view.TextSnapshot, 0 ), PositionAffinity.Predecessor );
            element.UpdateLayout();
            TakeSnapshot_( encoder, element );
        }
        while (view.TextViewLines.LastVisibleLine.End.Position < view.TextSnapshot.Length) {
            view.ViewScroller.ScrollViewportVerticallyByPixels( -50 );
            element.UpdateLayout();
            TakeSnapshot_( encoder, element );
        }
    }
    private static void TakeSnapshot_(BitmapEncoder encoder, FrameworkElement element) {
        var renderTargetBitmap = new RenderTargetBitmap( (int) element.ActualWidth, (int) element.ActualHeight, 96, 96, PixelFormats.Pbgra32 );
        renderTargetBitmap.Render( element );
        encoder.Frames.Add( BitmapFrame.Create( renderTargetBitmap ) );
    }
    private static FrameworkElement GetMainWindow(IWpfTextView view) {
        ThreadHelper.ThrowIfNotOnUIThread();
        var element = view.VisualElement;
        while (element.GetVisualOrLogicalParent() != null) {
            element = (FrameworkElement) element.GetVisualOrLogicalParent();
        }
        return element;
    }

}
