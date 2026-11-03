using System;
using Microsoft.Xrm.Sdk;

namespace Contoso.Crm.CustomerService.Plugins.Activities.Tasks
{
    /// <summary>
    /// The second missing file, so the report has to say "No matching .cs file (2)" rather
    /// than name a single unlucky class.
    /// </summary>
    public class GhostTaskImpersonatedUpdatePlugin : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider)
        {
        }
    }
}
