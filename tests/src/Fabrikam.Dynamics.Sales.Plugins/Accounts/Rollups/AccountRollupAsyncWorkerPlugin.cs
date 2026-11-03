using System;
using Microsoft.Xrm.Sdk;
using XrmTools.Meta.Attributes;

namespace Fabrikam.Dynamics.Sales.Plugins.Accounts.Rollups
{
    /// <summary>
    /// Every named argument at once - Name, ExecutionOrder, Description, AsyncAutoDelete -
    /// on a step long enough that the emitter has to break it onto one argument per line.
    /// </summary>
    public class AccountRollupAsyncWorkerPlugin : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider)
        {
        }
    }
}
