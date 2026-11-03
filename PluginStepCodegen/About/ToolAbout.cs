using System;
using System.ComponentModel.Composition;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Comentality.Xtb.About
{
    /// <summary>
    /// What an About box has to say about one tool, and where every word of it came from.
    ///
    /// Almost nothing here is written down twice. The name, the description and the logo are
    /// read back out of the tool's own <see cref="ExportMetadataAttribute"/>s - the same ones
    /// XrmToolBox paints its tile from - so the About box cannot end up disagreeing with the
    /// tools list about what this tool is called. The version comes out of the assembly, which
    /// the build already stamps with the commit. What is left for a tool to supply is three
    /// strings: its page on the site, its repository, and its licence notices if it has any.
    ///
    /// The namespace is deliberately not the tool's own. This file and <see cref="AboutBox"/>
    /// are meant to be the same file in every Comentality tool, so they are named after nothing
    /// in particular and copy across unchanged. Fix them here and propagate; do not fork.
    /// </summary>
    public sealed class ToolAbout
    {
        /// <summary>Where feedback goes. One address for every tool.</summary>
        public const string FeedbackAddress = "feedback@comentality.com";

        /// <summary>Every tool is a section of one page on the site, named by its slug.</summary>
        private const string SitePrefix = "https://comentality.com/tools/#";

        private ToolAbout(string name, string description, Image logo, string version,
                          string pageUrl, string repositoryUrl)
        {
            Name = name;
            Description = description;
            Logo = logo;
            Version = version;
            PageUrl = pageUrl;
            RepositoryUrl = repositoryUrl;
        }

        /// <summary>The name XrmToolBox shows in its tools list.</summary>
        public string Name { get; }

        /// <summary>The one-line description from the tool's metadata.</summary>
        public string Description { get; }

        /// <summary>The tile image, or null if the tool ships without one.</summary>
        public Image Logo { get; }

        /// <summary>
        /// What to call this build out loud: <c>1.0.1-dev (fee4a2d)</c>. The commit is in the
        /// parentheses when the build stamped one, and gone when it did not.
        /// </summary>
        public string Version { get; }

        public string PageUrl { get; }

        public string RepositoryUrl { get; }

        /// <summary>Where this tool's releases are written up. Beside the repository.</summary>
        public string ReleaseNotesUrl
        {
            get { return RepositoryUrl == null ? null : RepositoryUrl.TrimEnd('/') + "/releases/latest"; }
        }

        /// <summary>
        /// Licences and credits for anything this tool redistributes, or null when it
        /// redistributes nothing. Null is the common case: only Icons carries a library.
        /// </summary>
        public string Notices { get; set; }

        /// <summary>What the line that opens <see cref="Notices"/> calls them.</summary>
        public string NoticesCaption { get; set; }

        /// <summary>
        /// Reads a tool out of its own plugin class.
        /// </summary>
        /// <param name="pluginType">The class carrying the <c>ExportMetadata</c> attributes.</param>
        /// <param name="pageSlug">The tool's anchor on comentality.com/tools/, without the #.</param>
        /// <param name="repositoryUrl">The GitHub repository, with no trailing slash.</param>
        public static ToolAbout FromPlugin(Type pluginType, string pageSlug, string repositoryUrl)
        {
            if (pluginType == null) throw new ArgumentNullException(nameof(pluginType));
            if (string.IsNullOrWhiteSpace(pageSlug))
                throw new ArgumentException("A tool needs a page to link to.", nameof(pageSlug));

            return new ToolAbout(
                Metadata(pluginType, "Name") ?? pluginType.Name,
                Metadata(pluginType, "Description"),
                ImageFrom(Metadata(pluginType, "BigImageBase64")),
                VersionOf(pluginType.Assembly),
                SitePrefix + pageSlug.Trim('/'),
                repositoryUrl)
            {
                NoticesCaption = "Licences and credits"
            };
        }

        /// <summary>
        /// The four lines that belong in a bug report, and nothing else. No organisation url, no
        /// user, no environment id: those belong to somebody's customer, and a diagnostics block
        /// is pasted into mail by people who have not read it yet.
        /// </summary>
        public string Diagnostics()
        {
            return Compose(Name, Version, HostVersion(),
                           RuntimeInformation.FrameworkDescription,
                           RuntimeInformation.OSDescription);
        }

        /// <summary>The mail this tool would rather receive: addressed, titled, and already carrying its versions.</summary>
        public Uri MailTo()
        {
            var subject = Name + " " + Version;
            var body =
                "What happened, and what you expected instead:" + Environment.NewLine +
                Environment.NewLine +
                Environment.NewLine +
                "---" + Environment.NewLine +
                Diagnostics();

            return new Uri("mailto:" + FeedbackAddress +
                           "?subject=" + Uri.EscapeDataString(subject) +
                           "&body=" + Uri.EscapeDataString(body));
        }

        /// <summary>The body of <see cref="Diagnostics"/>, with nothing of its own to ask the machine.</summary>
        internal static string Compose(string name, string version, string host, string framework, string os)
        {
            return string.Join(Environment.NewLine, new[]
            {
                (name + " " + version).Trim(),
                host,
                framework,
                os
            }.Where(line => !string.IsNullOrWhiteSpace(line)).ToArray());
        }

        /// <summary>
        /// How the assembly says it was built. <c>AssemblyInformationalVersion</c> carries the
        /// commit after a <c>+</c> when the build stamped one - <c>1.0.1-dev+fee4a2d...</c> -
        /// and the first seven characters of it are what anybody actually types back.
        /// </summary>
        internal static string VersionOf(Assembly assembly)
        {
            var informational = assembly
                .GetCustomAttributes(typeof(AssemblyInformationalVersionAttribute), false)
                .Cast<AssemblyInformationalVersionAttribute>()
                .Select(a => a.InformationalVersion)
                .FirstOrDefault();

            if (string.IsNullOrWhiteSpace(informational))
                return assembly.GetName().Version.ToString(3);

            var plus = informational.IndexOf('+');
            if (plus < 0) return informational;

            var version = informational.Substring(0, plus);
            var commit = informational.Substring(plus + 1);
            if (commit.Length > 7) commit = commit.Substring(0, 7);

            return commit.Length == 0 ? version : version + " (" + commit + ")";
        }

        /// <summary>
        /// Which XrmToolBox this is running in - the copy loaded beside the tool, not the one it
        /// was compiled against. A tool built against 1.2025.7 and running in 1.2026.8 should say
        /// 1.2026.8, because that is the one the bug is in.
        /// </summary>
        private static string HostVersion()
        {
            var host = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "XrmToolBox.Extensibility");

            return host == null
                ? "XrmToolBox (not loaded)"
                : "XrmToolBox " + host.GetName().Version;
        }

        private static string Metadata(Type pluginType, string key)
        {
            return pluginType
                .GetCustomAttributes(typeof(ExportMetadataAttribute), false)
                .Cast<ExportMetadataAttribute>()
                .Where(a => a.Name == key)
                .Select(a => a.Value as string)
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        }

        /// <summary>
        /// The tile image, copied out of the stream it was decoded from rather than handed back
        /// still holding it - <see cref="Image.FromStream(Stream)"/> keeps the stream for the
        /// life of the image, and this one is about to close.
        /// </summary>
        private static Image ImageFrom(string base64)
        {
            if (string.IsNullOrWhiteSpace(base64)) return null;

            try
            {
                using (var stream = new MemoryStream(Convert.FromBase64String(base64)))
                using (var decoded = Image.FromStream(stream))
                {
                    return new Bitmap(decoded);
                }
            }
            catch (FormatException)
            {
                return null;
            }
            catch (ArgumentException)
            {
                return null;
            }
        }
    }
}
