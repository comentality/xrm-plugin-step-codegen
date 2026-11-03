using System;
using Microsoft.Xrm.Sdk;
using XrmTools.Meta.Attributes;

namespace Fabrikam.Dynamics.Sales.Plugins.Common.Stamping
{
    /// <summary>
    /// The generic plugin: one class registered against many tables.
    ///
    /// It stamps the same column on every table it is registered for, which is why it has
    /// a create and an update step on each of five - the shape a plugin repository has far
    /// more of than it has one-table plugins, and the only class here whose two output
    /// modes come out in different orders.
    ///
    /// The attributes stay in execution order, tables interleaved, because that is the
    /// order Xrm Tools reads them back in. The comment, which nothing reads back, regroups
    /// the same steps a table at a time so they can be read at all.
    ///
    /// Registered scrambled, so a run has the sort to do. Five of its steps tie on stage,
    /// rank and message name, which is what pins the table as the last tiebreak in
    /// execution order; and its annotation steps run out of message-name order - the
    /// update is PreOperation, the create PostOperation - which is what pins that
    /// regrouping is a stable sort rather than a re-sort.
    /// </summary>
    public class GenericStatusChangeDateStamperPlugin : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider)
        {
        }
    }
}
