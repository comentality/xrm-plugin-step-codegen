using System;
using Microsoft.Xrm.Sdk;
using XrmTools.Meta.Attributes;

namespace Fabrikam.Dynamics.Sales.Plugins.Notes.Annotations
{
    /// <summary>
    /// What the tool must not touch, sitting next to what it must overwrite.
    ///
    /// This summary, the remarks block below it and the [Obsolete] have to survive a run
    /// untouched. The [Step] and the "Register:" remarks are stale output from an earlier
    /// run against a different registration, and have to be replaced rather than appended
    /// to - run the tool twice and the file must settle after the first write.
    /// </summary>
    /// <remarks>
    /// Hand written remarks that are none of the tool's business. It only owns a remarks
    /// block whose first line is the marker, so this one has to come through verbatim.
    /// </remarks>
    /// <remarks>
    /// Register:
    /// Sync Post-Delete of contact (order 9, disabled)
    ///     PostImage: (all columns)
    /// </remarks>
    [Obsolete("Not really obsolete - here to prove foreign attributes are left alone.")]
    [Step("Delete", "contact", Stages.PostOperation, ExecutionMode.Synchronous, ExecutionOrder = 9)]
    public class AnnotationHandWrittenDocsPlugin : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider)
        {
        }
    }
}
