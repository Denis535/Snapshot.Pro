namespace Snapshot.Pro;
using System;
using System.Collections;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.Extensibility;

[VisualStudioContribution]
public class VSExtension : Extension {

    public override ExtensionConfiguration ExtensionConfiguration => new ExtensionConfiguration() {
        RequiresInProcessHosting = true,
    };

    protected override void InitializeServices(IServiceCollection serviceCollection) {
        base.InitializeServices( serviceCollection );
    }

}
