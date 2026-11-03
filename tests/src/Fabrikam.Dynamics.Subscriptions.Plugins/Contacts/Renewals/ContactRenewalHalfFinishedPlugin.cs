using System;
using Microsoft.Xrm.Sdk;
using XrmTools.Meta.Attributes;

namespace Fabrikam.Dynamics.Subscriptions.Plugins.Contacts.Renewals
{
    /// <summary>
    /// Half written and switched off, which is the normal resting state of a step in a
    /// development environment: registered, filtered, and disabled from the registration
    /// tool while the rest of the work happens.
    ///
    /// The managed fixture can only reach a disabled step through a whole companion
    /// solution imported without --activate-plugins. Here it is one field on one record,
    /// which is the other half of the same check: the tool has to say "disabled"
    /// whichever way the step got that way.
    /// </summary>
    public class ContactRenewalHalfFinishedPlugin : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider)
        {
        }
    }
}
