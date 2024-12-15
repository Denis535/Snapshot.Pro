namespace Snapshot.Pro;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Commands;
using Microsoft.VisualStudio.Extensibility.Shell;
using Microsoft.VisualStudio.Extensibility.VSSdkCompatibility;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.TextManager.Interop;

[VisualStudioContribution]
public class TakeVideoSnapshotCommand : Microsoft.VisualStudio.Extensibility.Commands.Command {

    private TraceSource Logger { get; }
    private AsyncServiceProviderInjection<DTE, DTE2> DTE { get; }
    private AsyncServiceProviderInjection<SVsTextManager, IVsTextManager> TextManager { get; }
    private MefInjection<IVsEditorAdaptersFactoryService> EditorAdaptersFactoryService { get; }

    public override CommandConfiguration CommandConfiguration => new CommandConfiguration( "%Snapshot.Pro.TakeVideoSnapshotCommand.DisplayName%" ) {
        Icon = new CommandIconConfiguration( ImageMoniker.KnownValues.Extension, IconSettings.IconAndText ),
        Placements = [ CommandPlacement.KnownPlacements.ToolsMenu ],
    };

    public TakeVideoSnapshotCommand(
        VisualStudioExtensibility extensibility,
        TraceSource logger,
        AsyncServiceProviderInjection<DTE, DTE2> dte,
        AsyncServiceProviderInjection<SVsTextManager, IVsTextManager> textManager,
        MefInjection<IVsEditorAdaptersFactoryService> editorAdaptersFactoryService
        ) : base( extensibility ) {
        Logger = logger;
        DTE = dte;
        TextManager = textManager;
        EditorAdaptersFactoryService = editorAdaptersFactoryService;
    }

    public override Task InitializeAsync(CancellationToken cancellationToken) {
        return base.InitializeAsync( cancellationToken );
    }

    public override async Task ExecuteCommandAsync(IClientContext context, CancellationToken cancellationToken) {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        try {
            var textViewSnapshot = await context.GetActiveTextViewAsync( cancellationToken ) ?? throw new NullReferenceException( "ITextViewSnapshot is null" );
            //var textDocumentSnapshot = view.Document ?? throw new NullReferenceException( "ITextDocumentSnapshot is null" );

            var textManager = await TextManager.GetServiceAsync();
            ErrorHandler.ThrowOnFailure( textManager.GetActiveView( 1, null, out var activeTextView ) );

            var editorAdaptersFactoryService = await EditorAdaptersFactoryService.GetServiceAsync();
            var wpfTextViewHost = editorAdaptersFactoryService.GetWpfTextViewHost( activeTextView ) ?? throw new NullReferenceException( "IWpfTextViewHost is null" );
            var wpfTextView = wpfTextViewHost.TextView ?? throw new NullReferenceException( "IwpfTextView is null" );
            var wpfTextViewMargin = wpfTextViewHost.GetTextViewMargin( PredefinedMarginNames.LineNumber ) ?? throw new NullReferenceException( "IWpfTextViewMargin is null" );

            var path = $"C:/Snapshot.Pro/{DateTime.UtcNow.Ticks}-{Path.GetFileNameWithoutExtension( textViewSnapshot.FilePath ).Replace( ".", "_" )}.h264";
            var element = GetRoot( wpfTextView.VisualElement );
            var stopwatch = Stopwatch.StartNew();
            TakeVideoSnapshot( path, element, wpfTextView, wpfTextViewMargin );
            stopwatch.Stop();
            Debug.WriteLine( $"Snapshot was saved ({stopwatch.Elapsed.TotalMinutes} minutes): {path}" );
            await Extensibility.Shell().ShowPromptAsync( $"Snapshot was saved ({stopwatch.Elapsed.TotalMinutes} minutes): {path}", PromptOptions.OK, cancellationToken );
        } catch (Exception ex) {
            Debug.WriteLine( ex.ToString() );
            await Extensibility.Shell().ShowPromptAsync( ex.ToString(), PromptOptions.OK, cancellationToken );
        }
    }

    private static void TakeVideoSnapshot(string path, FrameworkElement element, IWpfTextView view, IWpfTextViewMargin margin) {
        ThreadHelper.ThrowIfNotOnUIThread();
        Directory.CreateDirectory( Path.GetDirectoryName( path ) );
        using (var stream = File.Create( path )) {
            using (var encoder = new VideoEncoder2( stream, (int) element.ActualWidth, (int) element.ActualHeight, 60 )) {
                TakeVideoSnapshot( encoder, element, view, margin );
                encoder.Flush();
            }
            stream.Flush();
        }
    }
    private static void TakeVideoSnapshot(VideoEncoder2 encoder, FrameworkElement element, IWpfTextView view, IWpfTextViewMargin margin) {
        ThreadHelper.ThrowIfNotOnUIThread();
        var bitmap = new RenderTargetBitmap( (int) element.ActualWidth, (int) element.ActualHeight, 96, 96, PixelFormats.Pbgra32 );
        {
            view.ViewportLeft = 0;
            view.DisplayTextLineContainingBufferPosition( new SnapshotPoint( view.TextSnapshot, 0 ), 0, ViewRelativePosition.Top );
            view.Caret.MoveTo( new SnapshotPoint( view.TextSnapshot, 0 ), PositionAffinity.Predecessor );
        }
        var frame = 0;
        {
            for (var i = 0; i < 60 * 2; i++) {
                TakeVideoSnapshot( encoder, bitmap, element, frame, 0, view, margin );
                frame++;
            }
            for (var i = 0; view.TextViewLines.LastVisibleLine.End.Position < view.TextSnapshot.Length; i++) {
                TakeVideoSnapshot( encoder, bitmap, element, frame, 0, view, margin );
                frame++;
                var delta = Math.Min( (double) i / (60 * 2), 1 );
                view.ViewScroller.ScrollViewportVerticallyByPixels( -delta );
            }
            for (var i = 0; i < 60 * 2; i++) {
                TakeVideoSnapshot( encoder, bitmap, element, frame, 0, view, margin );
                frame++;
            }
        }
        while (frame < 60 * 7) {
            TakeVideoSnapshot( encoder, bitmap, element, frame, 0, view, margin );
            frame++;
        }
    }
    private static void TakeVideoSnapshot(VideoEncoder2 encoder, RenderTargetBitmap bitmap, FrameworkElement element, int frame, int duration, IWpfTextView view, IWpfTextViewMargin margin) {
        view.VisualElement.UpdateLayout();
        UpdateLineNumbers( margin );
        bitmap.Render( element );
        encoder.Add( bitmap, frame, duration );
    }

    // Helpers
    private static FrameworkElement GetRoot(FrameworkElement element) {
        ThreadHelper.ThrowIfNotOnUIThread();
        while (element.GetVisualOrLogicalParent() != null) {
            element = (FrameworkElement) element.GetVisualOrLogicalParent();
        }
        return element;
    }
    private static void UpdateLineNumbers(IWpfTextViewMargin margin) {
        var method = margin.GetType().GetMethod( "UpdateLineNumbers", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic );
        method.Invoke( margin, [] );
    }

}
