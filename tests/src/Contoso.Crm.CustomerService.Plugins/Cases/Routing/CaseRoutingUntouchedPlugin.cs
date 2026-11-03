using System;
using Microsoft.Xrm.Sdk;
using XrmTools.Meta.Attributes;

namespace Contoso.Crm.CustomerService.Plugins.Cases.Routing
{
    /// <summary>
    /// In the codebase, never registered, never documented - the ordinary state of most of
    /// a plugin project. It is here to prove that a run over a folder holding four
    /// assemblies' worth of source only touches the classes it was asked about: this file
    /// has to come back byte for byte identical.
    /// </summary>
    public class CaseRoutingUntouchedPlugin : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider)
        {
        }
    }
}
