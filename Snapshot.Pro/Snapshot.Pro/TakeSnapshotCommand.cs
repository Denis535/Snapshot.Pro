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
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.TextManager.Interop;

[VisualStudioContribution]
public class TakeSnapshotCommand : Microsoft.VisualStudio.Extensibility.Commands.Command {

    private TraceSource Logger { get; }
    private AsyncServiceProviderInjection<DTE, DTE2> DTE { get; }
    private AsyncServiceProviderInjection<SVsTextManager, IVsTextManager> TextManager { get; }
    private MefInjection<IVsEditorAdaptersFactoryService> EditorAdaptersFactoryService { get; }

    public override CommandConfiguration CommandConfiguration => new CommandConfiguration( "%Snapshot.Pro.TakeSnapshotCommand.DisplayName%" ) {
        Icon = new CommandIconConfiguration( ImageMoniker.KnownValues.Extension, IconSettings.IconAndText ),
        Placements = [ CommandPlacement.KnownPlacements.ToolsMenu ],
    };

    public TakeSnapshotCommand(
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
                var path = $"C:/Snapshot.Pro/{DateTime.UtcNow.Ticks}-{Path.GetFileNameWithoutExtension( textViewSnapshot.FilePath ).Replace( ".", "_" )}.png";
                TakeSnapshot( path, GetRoot( wpfTextView.VisualElement ), wpfTextView, wpfTextViewMargin );
                Debug.WriteLine( $"Snapshot was saved: {path}" );
                await Extensibility.Shell().ShowPromptAsync( $"Snapshot was saved: {path}", PromptOptions.OK, cancellationToken );
            }
        } catch (Exception ex) {
            Debug.WriteLine( ex.ToString() );
            await Extensibility.Shell().ShowPromptAsync( ex.ToString(), PromptOptions.OK, cancellationToken );
        }
    }

    private static void TakeSnapshot(string path, FrameworkElement element, IWpfTextView view, IWpfTextViewMargin margin) {
        ThreadHelper.ThrowIfNotOnUIThread();
        Directory.CreateDirectory( Path.GetDirectoryName( path ) );
        using (var stream = File.Create( path )) {
            var encoder = new PngBitmapEncoder();
            {
                var bitmap = new RenderTargetBitmap( (int) element.ActualWidth, (int) element.ActualHeight, 96, 96, PixelFormats.Pbgra32 );
                bitmap.Render( element );
                encoder.Frames.Add( BitmapFrame.Create( bitmap ) );
            }
            encoder.Save( stream );
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
