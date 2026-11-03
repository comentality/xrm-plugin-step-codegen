using System;
using Microsoft.Xrm.Sdk;
using XrmTools.Meta.Attributes;

namespace Fabrikam.Dynamics.Sales.Plugins.Notes.Current
{
    /// <summary>
    /// Half of the short-name-collision fixture. This is the class that is actually
    /// registered; Fabrikam.Dynamics.Sales.Plugins.Notes.Legacy.NoteCreateDuplicatePlugin
    /// declares the same short name in a sibling file, so the short name alone matches
    /// both. The registered namespace settles it: a run writes this file and may not touch
    /// the sibling. Neither file is named after the class it declares, so the file name
    /// cannot settle it either.
    /// </summary>
    public class NoteCreateDuplicatePlugin : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider)
        {
        }
    }
}
