using System;
using System.Collections.Generic;

namespace PluginStepCodegen
{
    /// <summary>
    /// What the tool carries from one opening of an environment to the next: the ticks, the
    /// folder, and what was on the lists last time, so that what was not can be pointed at.
    /// One file per environment, because every id in here is the environment's own.
    ///
    /// Persisted through XrmToolBox's SettingsManager, which XML serialises - hence the
    /// public parameterless shape and the lists rather than sets. A field that is missing
    /// from an older file deserialises to an empty list, which reads as "nothing remembered",
    /// which is the right answer for it.
    /// </summary>
    public class SessionMemory
    {
        /// <summary>The source folder that was in the box.</summary>
        public string Folder { get; set; }

        /// <summary>The assemblies that were ticked.</summary>
        public List<Guid> CheckedAssemblies { get; set; }

        /// <summary>The classes that were unticked - the exception, as the control keeps it.</summary>
        public List<Guid> ExcludedTypes { get; set; }

        /// <summary>Every assembly the list had a row for, ticked or not, shown or not.</summary>
        public List<Guid> SeenAssemblies { get; set; }

        /// <summary>
        /// The assemblies whose classes were fetched. A class can only be new against an
        /// assembly whose classes were looked at before; in one that was listed and never
        /// ticked, every class is unseen and none of them is news.
        /// </summary>
        public List<Guid> FetchedAssemblies { get; set; }

        /// <summary>Every class that was fetched from any of those.</summary>
        public List<Guid> SeenTypes { get; set; }

        public SessionMemory()
        {
            CheckedAssemblies = new List<Guid>();
            ExcludedTypes = new List<Guid>();
            SeenAssemblies = new List<Guid>();
            FetchedAssemblies = new List<Guid>();
            SeenTypes = new List<Guid>();
        }
    }
}
