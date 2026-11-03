using System;
using Microsoft.Xrm.Sdk;
using XrmTools.Meta.Attributes;

namespace Contoso.Crm.CustomerService.Plugins.Leads.Qualification
{
    /// <summary>
    /// In the codebase, carrying the tool's own output, and not registered anywhere.
    ///
    /// The attributes and the "Register:" block below are the tool's, written when this
    /// class still had steps; the registration has since been deleted. The tool lists
    /// classes by their steps, so this one never reaches the list and nothing here is
    /// rewritten: stale documentation is left exactly as stale as it was found, and this
    /// file has to come back from a run byte for byte identical.
    /// </summary>
    /// <remarks>
    /// Register:
    /// Sync Post-Create of lead (order 1): (all columns)
    ///     PreImage: subject, companyname
    /// </remarks>
    [Plugin]
    [Step("Create", "lead", Stages.PostOperation, ExecutionMode.Synchronous)]
    [Image(ImageTypes.PreImage, "subject,companyname")]
    public class LeadCreateStaleDocsPlugin : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider)
        {
        }
    }
}
