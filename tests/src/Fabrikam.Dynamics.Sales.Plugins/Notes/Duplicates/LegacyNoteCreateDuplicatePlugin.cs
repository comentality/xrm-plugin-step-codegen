using System;
using Microsoft.Xrm.Sdk;
using XrmTools.Meta.Attributes;

namespace Fabrikam.Dynamics.Sales.Plugins.Notes.Legacy
{
    /// <summary>
    /// The other half of the short-name-collision fixture. Never registered; it exists
    /// only so that the short name NoteCreateDuplicatePlugin resolves to two files under
    /// the selected folder, and the registered namespace has a tie to settle. A run may
    /// never modify this file.
    /// </summary>
    public class NoteCreateDuplicatePlugin : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider)
        {
        }
    }
}
