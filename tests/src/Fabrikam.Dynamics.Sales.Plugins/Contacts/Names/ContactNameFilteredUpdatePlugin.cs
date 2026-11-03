using System;
using Microsoft.Xrm.Sdk;
using XrmTools.Meta.Attributes;

namespace Fabrikam.Dynamics.Sales.Plugins.Contacts.Names
{
    /// <summary>
    /// Filtering attributes as the third positional argument, and a pre-image that names
    /// its columns. Also carries a plugin type description, so [Plugin] is not bare.
    /// </summary>
    public class ContactNameFilteredUpdatePlugin : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider)
        {
        }
    }
}
