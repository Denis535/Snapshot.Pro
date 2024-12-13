﻿namespace Snapshot.Pro;
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
public class TakeSnapshotCommand : Microsoft.VisualStudio.Extensibility.Commands.Command {

    private TraceSource Logger { get; }
    private AsyncServiceProviderInjection<DTE, DTE2> DTE { get; }
    private MefInjection<IVsEditorAdaptersFactoryService> EditorAdaptersFactoryService { get; }
    private AsyncServiceProviderInjection<SVsTextManager, IVsTextManager> TextManager { get; }

    public override CommandConfiguration CommandConfiguration => new CommandConfiguration( "%Snapshot.Pro.TakeSnapshotCommand.DisplayName%" ) {
        Icon = new CommandIconConfiguration( ImageMoniker.KnownValues.Extension, IconSettings.IconAndText ),
        Placements = [ CommandPlacement.KnownPlacements.ToolsMenu ],
    };

    public TakeSnapshotCommand(
        VisualStudioExtensibility extensibility,
        TraceSource logger,
        AsyncServiceProviderInjection<DTE, DTE2> dte,
        MefInjection<IVsEditorAdaptersFactoryService> editorAdaptersFactoryService,
        AsyncServiceProviderInjection<SVsTextManager, IVsTextManager> textManager
        ) : base( extensibility ) {
        Logger = logger;
        DTE = dte;
        EditorAdaptersFactoryService = editorAdaptersFactoryService;
        TextManager = textManager;
    }

    public override Task InitializeAsync(CancellationToken cancellationToken) {
        return base.InitializeAsync( cancellationToken );
    }

    public override async Task ExecuteCommandAsync(IClientContext context, CancellationToken cancellationToken) {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        try {
            var textViewSnapshot = await context.GetActiveTextViewAsync( cancellationToken ) ?? throw new NullReferenceException( "ITextViewSnapshot is null" );
            //var textDocumentSnapshot = view.Document ?? throw new NullReferenceException( "ITextDocumentSnapshot is null" );

            var editorAdaptersFactoryService = await EditorAdaptersFactoryService.GetServiceAsync();

            var textManager = await TextManager.GetServiceAsync();
            ErrorHandler.ThrowOnFailure( textManager.GetActiveView( 1, null, out var activeTextView ) );

            var wpfTextViewHost = editorAdaptersFactoryService.GetWpfTextViewHost( activeTextView ) ?? throw new NullReferenceException( "IWpfTextViewHost is null" );
            var wpfTextView = wpfTextViewHost.TextView ?? throw new NullReferenceException( "IwpfTextView is null" );
            var wpfTextViewMargin = wpfTextViewHost.GetTextViewMargin( PredefinedMarginNames.LineNumber ) ?? throw new NullReferenceException( "IWpfTextViewMargin is null" );

            var path = $"D:/Snapshots/{DateTime.UtcNow.Ticks}-{Path.GetFileNameWithoutExtension( textViewSnapshot.FilePath ).Replace( ".", "_" )}.h264";
            var element = GetRoot( wpfTextView.VisualElement );
            TakeSnapshot( path, element, wpfTextView, wpfTextViewMargin );
            Debug.WriteLine( $"Snapshot was saved: " + path );
            await Extensibility.Shell().ShowPromptAsync( $"Snapshot was saved: " + path, PromptOptions.OK, cancellationToken );
        } catch (Exception ex) {
            Debug.WriteLine( ex.ToString() );
            await Extensibility.Shell().ShowPromptAsync( ex.ToString(), PromptOptions.OK, cancellationToken );
        }
    }

    private static void TakeSnapshot(string path, FrameworkElement element, IWpfTextView view, IWpfTextViewMargin margin) {
        ThreadHelper.ThrowIfNotOnUIThread();
        Directory.CreateDirectory( Path.GetDirectoryName( path ) );
        using (var stream = File.Create( path )) {
            using (var encoder = new VideoEncoder( stream, (int) element.ActualWidth, (int) element.ActualHeight, 60 )) {
                using (var converter = new VideoFrameConverter( (int) element.ActualWidth, (int) element.ActualHeight, (int) element.ActualWidth, (int) element.ActualHeight )) {
                    TakeSnapshot( encoder, converter, element, view, margin );
                }
                encoder.Flush();
            }
            stream.Flush();
        }
    }
    private static void TakeSnapshot(VideoEncoder encoder, VideoFrameConverter converter, FrameworkElement element, IWpfTextView view, IWpfTextViewMargin margin) {
        ThreadHelper.ThrowIfNotOnUIThread();
        var bitmap = new RenderTargetBitmap( (int) element.ActualWidth, (int) element.ActualHeight, 96, 96, PixelFormats.Pbgra32 );
        {
            view.ViewportLeft = 0;
            view.DisplayTextLineContainingBufferPosition( new SnapshotPoint( view.TextSnapshot, 0 ), 0, ViewRelativePosition.Top );
            view.Caret.MoveTo( new SnapshotPoint( view.TextSnapshot, 0 ), PositionAffinity.Predecessor );
            {
                view.VisualElement.UpdateLayout();
                UpdateLineNumbers( margin );
                bitmap.Render( element );
                encoder.Add( bitmap, converter );
            }
        }
        while (view.TextViewLines.LastVisibleLine.End.Position < view.TextSnapshot.Length) {
            view.ViewScroller.ScrollViewportVerticallyByPixels( -50 );
            {
                view.VisualElement.UpdateLayout();
                UpdateLineNumbers( margin );
                bitmap.Render( element );
                encoder.Add( bitmap, converter );
            }
        }
    }
    private static FrameworkElement GetRoot(FrameworkElement element) {
        ThreadHelper.ThrowIfNotOnUIThread();
        while (element.GetVisualOrLogicalParent() != null) {
            element = (FrameworkElement) element.GetVisualOrLogicalParent();
        }
        return element;
    }

    // Helpers
    private static void UpdateLineNumbers(IWpfTextViewMargin margin) {
        var method = margin.GetType().GetMethod( "UpdateLineNumbers", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic );
        method.Invoke( margin, [] );
    }

}
