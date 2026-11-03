using System;
using Microsoft.Xrm.Sdk;

namespace Contoso.Crm.CustomerService.Plugins.Contacts.Lifecycle
{
    /// <summary>
    /// Registered, with steps, and no .cs anywhere the tool will look. It also sits
    /// between AlphaContactIntakePlugin and CharlieContactJobTitlePlugin when types are
    /// sorted by name - same namespace, other assembly - which is what makes the class list
    /// interleave two assemblies before it regroups them.
    /// </summary>
    public class BravoFollowUpTaskPlugin : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider)
        {
        }
    }
}
