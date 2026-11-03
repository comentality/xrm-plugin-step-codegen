using System;
using Microsoft.Xrm.Sdk;
using XrmTools.Meta.Attributes;

namespace Contoso.Crm.CustomerService.Plugins.Contacts.Lifecycle
{
    /// <summary>
    /// The far side of the interleave: sorted by type name this class follows
    /// BravoFollowUpTaskPlugin, which is in the same namespace but a different assembly, so
    /// a list built straight from the query order would show the
    /// Contoso.Crm.CustomerService.Plugins heading twice.
    /// </summary>
    public class CharlieContactJobTitlePlugin : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider)
        {
        }
    }
}
