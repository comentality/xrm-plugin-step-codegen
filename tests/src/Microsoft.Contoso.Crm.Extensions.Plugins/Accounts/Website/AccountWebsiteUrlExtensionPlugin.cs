using System;
using Microsoft.Xrm.Sdk;
using XrmTools.Meta.Attributes;

namespace Microsoft.Contoso.Crm.Extensions.Plugins.Accounts.Website
{
    /// <summary>
    /// The only class in the assembly the Microsoft switch hides, and the reason hiding it
    /// is a display decision rather than a filter: its source is right here, and once the
    /// switch or the filter box brings the assembly back, this class documents like any
    /// other.
    /// </summary>
    public class AccountWebsiteUrlExtensionPlugin : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider)
        {
        }
    }
}
