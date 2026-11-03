using System;
using Microsoft.Xrm.Sdk;
using XrmTools.Meta.Attributes;

namespace Fabrikam.Dynamics.Sales.Plugins.Diagnostics.Columns
{
    /// <summary>
    /// The three shapes of a column list the experimental "(all columns except: ...)"
    /// rendering has to tell apart: a filter that is every updatable column of annotation
    /// but two, a filter that is every updatable column of task spelled out, and a filter
    /// carrying a name outside the universe it is measured against - createdon is real
    /// but not updatable - which must stay verbatim, because the odd name out is the
    /// finding. The first step's image is every real column of annotation but the blob.
    /// The near-complete lists are expanded against the live table when this is
    /// registered, so the data is genuinely "seventy of seventy five" whatever the
    /// environment carries.
    /// </summary>
    public class NearlyAllColumnsFilteringPlugin : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider)
        {
        }
    }
}
