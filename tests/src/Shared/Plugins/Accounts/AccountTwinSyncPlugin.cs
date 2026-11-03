using System;
using Microsoft.Xrm.Sdk;
using XrmTools.Meta.Attributes;

namespace Litware.Shared.Plugins.Accounts
{
    /// <summary>
    /// One file, compiled into two assemblies, registered from both.
    ///
    /// Fabrikam.Dynamics.Sales.Plugins and Contoso.Crm.CustomerService.Plugins each link
    /// this file and each register Litware.Shared.Plugins.Accounts.AccountTwinSyncPlugin
    /// with steps of their own, which is what a shared base library looks like once it has
    /// been deployed twice. The tool matches a class to a file by short name and knows
    /// nothing about assemblies, so both registrations resolve to this one file and the
    /// second write is what survives - the collision only a fixture with more than one
    /// assembly can produce.
    /// </summary>
    public class AccountTwinSyncPlugin : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider)
        {
        }
    }
}
