using System;
using Microsoft.Xrm.Sdk;
using XrmTools.Meta.Attributes;

namespace Fabrikam.Dynamics.Sales.Plugins.Common.Placeholders
{
    /// <summary>
    /// Present in the assembly and registered as a plugin type, but with no steps against
    /// it. The tool lists types by their steps, so this one must never appear - and
    /// this file must come back from a run byte for byte identical.
    /// </summary>
    public class RegisteredTypeWithoutStepsPlugin : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider)
        {
        }
    }
}
