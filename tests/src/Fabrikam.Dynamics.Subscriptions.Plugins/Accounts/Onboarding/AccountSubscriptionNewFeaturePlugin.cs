using System;
using Microsoft.Xrm.Sdk;
using XrmTools.Meta.Attributes;

namespace Fabrikam.Dynamics.Subscriptions.Plugins.Accounts.Onboarding
{
    /// <summary>
    /// The ordinary case on the route the tool is really used through: an assembly
    /// registered straight into a development environment rather than imported inside a
    /// solution, so every record it owns is unmanaged and belongs to no solution but the
    /// Default one.
    ///
    /// Nothing the tool reads is supposed to differ, which is exactly why it is
    /// pinned: an unmanaged step is created with the plugin type on EventHandler and the
    /// platform is what fills in PluginTypeId, and the tool's step query joins on
    /// PluginTypeId. If that were ever left empty here, the tool would find no steps at
    /// all in the one environment shape that matters most.
    /// </summary>
    public class AccountSubscriptionNewFeaturePlugin : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider)
        {
        }
    }
}
