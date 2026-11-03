using System;
using Microsoft.Xrm.Sdk;
using XrmTools.Meta.Attributes;

namespace Fabrikam.Dynamics.Sales.Plugins.Accounts.Text
{
    /// <summary>
    /// Free text carrying every character the C# literal writer has to escape - quote,
    /// backslash, tab, carriage return, newline - plus the XML metacharacters that would
    /// break the doc comment if the summary emitter let them through.
    /// </summary>
    public class AccountEscapedTextDescriptionPlugin : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider)
        {
        }
    }
}
