namespace Snapshot.Pro;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
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
            {
                var stopwatch = Stopwatch.StartNew();
                var path = $"C:/Snapshot.Pro/{DateTime.UtcNow.Ticks}-{Path.GetFileNameWithoutExtension( textViewSnapshot.FilePath ).Replace( ".", "_" )}.h264";
                TakeVideoSnapshot( path, GetRoot( wpfTextView.VisualElement ), wpfTextView, wpfTextViewMargin );
                stopwatch.Stop();
                Debug.WriteLine( $"Snapshot was saved ({stopwatch.Elapsed.TotalMinutes} minutes): {path}" );
                await Extensibility.Shell().ShowPromptAsync( $"Snapshot was saved ({stopwatch.Elapsed.TotalMinutes} minutes): {path}", PromptOptions.OK, cancellationToken );
            }
        } catch (Exception ex) {
            Debug.WriteLine( ex.ToString() );
            await Extensibility.Shell().ShowPromptAsync( ex.ToString(), PromptOptions.OK, cancellationToken );
        }
    }

    private static void TakeVideoSnapshot(string path, FrameworkElement element, IWpfTextView view, IWpfTextViewMargin margin) {
        ThreadHelper.ThrowIfNotOnUIThread();
        Directory.CreateDirectory( Path.GetDirectoryName( path ) );
        using (var stream = File.Create( path )) {
            using (var recorder = new VideoRecorder( stream, (int) element.ActualWidth, (int) element.ActualHeight, 60 )) {
                recorder.TakeVideoSnapshot( element, view, margin );
                recorder.Flush();
            }
            stream.Flush();
        }
    }

    // Helpers
    private static FrameworkElement GetRoot(FrameworkElement element) {
        ThreadHelper.ThrowIfNotOnUIThread();
        while (element.GetVisualOrLogicalParent() != null) {
            element = (FrameworkElement) element.GetVisualOrLogicalParent();
        }
        return element;
    }

}
