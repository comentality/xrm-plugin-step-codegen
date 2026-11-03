using System;
using Microsoft.Xrm.Sdk;
using XrmTools.Meta.Attributes;

namespace Fabrikam.Dynamics.Sales.Plugins.Accounts.Images
{
    /// <summary>
    /// Every image shape on one class: named columns, no columns at all (which the summary
    /// must call out as "(all columns)"), a custom Name and EntityAlias, and imagetype 2 -
    /// the pre and post pair Dataverse allows on Update. The Create step's post-image also
    /// pins MessagePropertyName, which Dataverse sets to "Id" rather than "Target" there.
    /// </summary>
    public class AccountImageShapesPlugin : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider)
        {
        }
    }
}
