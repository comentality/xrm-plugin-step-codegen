using System;
using Microsoft.Xrm.Sdk;
using XrmTools.Meta.Attributes;

namespace Fabrikam.Dynamics.Sales.Plugins.Activities.Tasks.Guards
{
    /// <summary>
    /// Half of the cross assembly collision fixture, the other half being
    /// Contoso.Crm.CustomerService.Plugins.Contacts.Guards.RivalDeleteGuardPlugin in
    /// Contoso.Crm.CustomerService.Plugins\Contacts\Guards. Two assemblies, two files, two
    /// registrations, one short name - and the namespaces settle it: each registration
    /// is written into its own file, never the other's.
    /// </summary>
    public class RivalDeleteGuardPlugin : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider)
        {
        }
    }
}
