namespace Snapshot.Pro;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.VisualStudio.Shell;
using Community.VisualStudio.Toolkit;

[PackageRegistration( UseManagedResourcesOnly = true, AllowsBackgroundLoading = true )]
[InstalledProductRegistration( Vsix.Name, Vsix.Description, Vsix.Version )]
[ProvideMenuResource( "Menus.ctmenu", 1 )]
[Guid( PackageGuids.SnapshotProString )]
public sealed class Package : ToolkitPackage {

    protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress) {
        await this.RegisterCommandsAsync();
    }

}
