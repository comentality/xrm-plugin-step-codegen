using System;
using Microsoft.Xrm.Sdk;
using XrmTools.Meta.Attributes;

namespace Contoso.Crm.CustomerService.Plugins.Contacts.Guards
{
    /// <summary>
    /// The other half of the cross assembly collision fixture; the Fabrikam one is in
    /// Fabrikam.Dynamics.Sales.Plugins\Activities\Tasks\Guards. Both are registered, from
    /// different assemblies, and the namespaces settle it: a run writes
    /// Contoso.Crm.CustomerService.Plugins.Contacts.Guards.RivalDeleteGuardPlugin's
    /// registration here, and the Fabrikam class's never lands in this file.
    /// </summary>
    public class RivalDeleteGuardPlugin : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider)
        {
        }
    }
}
