using System;
using Microsoft.Xrm.Sdk;

namespace Contoso.Crm.CustomerService.Empty.Plugins.Queues.Monitoring
{
    /// <summary>Registered as a plugin type, given no steps, in an assembly where nothing else has any either.</summary>
    public class IdleQueueMonitorPlugin : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider)
        {
        }
    }
}
