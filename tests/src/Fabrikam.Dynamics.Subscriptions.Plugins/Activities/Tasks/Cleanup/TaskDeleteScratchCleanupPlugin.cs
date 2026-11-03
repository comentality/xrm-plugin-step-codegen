using System;
using Microsoft.Xrm.Sdk;
using XrmTools.Meta.Attributes;

namespace Fabrikam.Dynamics.Subscriptions.Plugins.Activities.Tasks.Cleanup
{
    /// <summary>
    /// A step someone named and described by hand in the registration tool, next to two
    /// that were left with the name it offered. The tool is supposed to tell those
    /// apart - emitting Name only when it is not the generated one - and the generated
    /// name it compares against is the registration tool's, not the solution importer's.
    /// This is the only fixture where that string comes from the same place a user's does.
    ///
    /// Its pre image is on a Delete, which is the only message a pre image is much use on
    /// and, on this route, one of the few the platform will accept it for.
    /// </summary>
    public class TaskDeleteScratchCleanupPlugin : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider)
        {
        }
    }
}
