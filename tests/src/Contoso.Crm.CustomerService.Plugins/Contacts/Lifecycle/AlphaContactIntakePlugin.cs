using System;
using Microsoft.Xrm.Sdk;
using XrmTools.Meta.Attributes;

namespace Contoso.Crm.CustomerService.Plugins.Contacts.Lifecycle
{
    /// <summary>
    /// An ordinary class in the second assembly, and the first of the three that prove the
    /// class list stays grouped by assembly.
    ///
    /// The environment hands types back sorted by full type name. This class,
    /// BravoFollowUpTaskPlugin and CharlieContactJobTitlePlugin share one namespace and
    /// alternate between Contoso.Crm.CustomerService.Plugins and
    /// Contoso.Crm.CustomerService.Orphan.Plugins when sorted that way. The list and the
    /// preview regroup them by assembly regardless, so each assembly heading has to appear
    /// exactly once.
    /// </summary>
    public class AlphaContactIntakePlugin : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider)
        {
        }
    }
}
