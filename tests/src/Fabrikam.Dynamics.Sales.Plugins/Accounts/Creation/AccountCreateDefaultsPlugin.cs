using System;
using Microsoft.Xrm.Sdk;
using XrmTools.Meta.Attributes;

namespace Fabrikam.Dynamics.Sales.Plugins.Accounts.Creation
{
    /// <summary>
    /// Baseline. One step, every value left at its default, so anything the tool
    /// emits beyond the bare positional constructor is noise it should have suppressed:
    /// rank 1, the auto-generated step name, no filter, no description, no images.
    /// </summary>
    public class AccountCreateDefaultsPlugin : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider)
        {
        }
    }
}
