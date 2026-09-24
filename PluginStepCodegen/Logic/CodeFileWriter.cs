using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace PluginStepCodegen.Logic
{
    /// <summary>
    /// Locates the .cs file declaring a plugin class and splices registration
    /// attributes in above the class declaration.
    /// </summary>
    public static class CodeFileWriter
    {
        /// <summary>
        /// Matches a balanced attribute group sitting at the very end of the text,
        /// starting at the beginning of its own line. String literals are skipped so
        /// that brackets inside them do not unbalance the match.
        /// </summary>
        private static readonly Regex TrailingAttribute = new Regex(
            @"(?m)^[ \t]*\[(?:[^\[\]""]|""(?:\\.|[^""\\])*""|(?<open>\[)|(?<-open>\]))*(?(open)(?!))\][ \t]*\r?\n?\z",
            RegexOptions.Compiled);

        /// <summary>Attributes this tool owns, and therefore may replace.</summary>
        private static readonly Regex OwnedAttribute = new Regex(
            @"^\s*\[\s*(Plugin|Step|Image)(Attribute)?\s*[\(\]]",
            RegexOptions.Compiled);

        /// <summary>
        /// A doc comment this tool owns: a remarks block at the very end of the text whose
        /// first line is the emitter's marker. Any other remarks block is the user's.
        /// </summary>
        private static readonly Regex TrailingRemarks = new Regex(
            @"(?m)^[ \t]*///[ \t]*<remarks>[ \t]*\r?\n"
            + @"[ \t]*///[ \t]*" + Regex.Escape(RemarksEmitter.Marker) + @"[ \t]*\r?\n"
            + @"(?:[ \t]*///.*\r?\n)*?"
            + @"[ \t]*///[ \t]*</remarks>[ \t]*\r?\n?\z",
            RegexOptions.Compiled);

        public static Regex ClassDeclaration(string className)
        {
            return new Regex(
                @"(?m)^[ \t]*(?:(?:public|internal|private|protected|abstract|sealed|static|partial|new)[ \t]+)*class[ \t]+"
                + Regex.Escape(className) + @"\b");
        }

        /// <summary>
        /// Finds the single .cs file declaring <paramref name="className"/>. Returns null
        /// when there is no match; sets <paramref name="ambiguous"/> when there are several.
        /// When several files declare the short name, the registered namespace breaks the
        /// tie if exactly one of them declares it.
        ///
        /// This reads the whole folder to answer about one class, so it is for the one-off
        /// question. Anything asking about a batch of classes should build a
        /// <see cref="SourceIndex"/> once and call <see cref="SourceIndex.Find"/>.
        /// </summary>
        public static string FindFile(string folder, string className, string namespaceName, out List<string> ambiguous)
        {
            var file = SourceIndex.Build(folder).Find(className, namespaceName, out ambiguous);
            return file == null ? null : file.Path;
        }

        /// <summary>
        /// Every .cs file under the folder that somebody wrote, as opposed to a build's.
        /// Build folders are stepped over rather than walked and discarded, because a bin
        /// folder holds more files than the source beside it.
        /// </summary>
        public static IEnumerable<string> EnumerateSources(string folder)
        {
            var pending = new Stack<string>();
            pending.Push(folder);

            while (pending.Count > 0)
            {
                var current = pending.Pop();

                string[] files;
                string[] directories;
                try
                {
                    files = Directory.GetFiles(current, "*.cs");
                    directories = Directory.GetDirectories(current);
                }
                catch (UnauthorizedAccessException)
                {
                    // A folder somebody cannot open is not a folder their plugins are in.
                    continue;
                }
                catch (DirectoryNotFoundException)
                {
                    // Deleted between being listed and being opened.
                    continue;
                }
                catch (PathTooLongException)
                {
                    // Nested past what Windows will open for anybody, which a package cache
                    // manages without trying. Nothing under it can be written to either, so
                    // the branch is stepped over the way an unreadable one is - the rest of
                    // the tree is still the source folder somebody asked about.
                    continue;
                }

                foreach (var file in files)
                {
                    if (!IsGenerated(file))
                    {
                        yield return file;
                    }
                }

                // Pushed backwards so they come off the stack in the order they were listed:
                // a list of the files declaring an ambiguous class reads better down the tree
                // than up it, and a stable order is one less thing to explain.
                for (var i = directories.Length - 1; i >= 0; i--)
                {
                    if (!IsBuildOutput(directories[i]))
                    {
                        pending.Push(directories[i]);
                    }
                }
            }
        }

        private static bool IsBuildOutput(string directory)
        {
            var name = Path.GetFileName(directory);
            return string.Equals(name, "bin", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(name, "obj", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsGenerated(string path)
        {
            if (path.IndexOf(@"\obj\", StringComparison.OrdinalIgnoreCase) >= 0 ||
                path.IndexOf(@"\bin\", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            var name = Path.GetFileName(path);
            return name.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase)
                   || name.EndsWith(".designer.cs", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Replaces this tool's own output above the class declaration, leaving anything
        /// else in place.
        ///
        /// The two outputs are independent: pass null for either to leave whatever is
        /// already in the file untouched, so switching output mode never silently deletes
        /// the other mode's work.
        /// </summary>
        public static string Splice(string code, string className, IEnumerable<string> remarks, IEnumerable<string> attributes)
        {
            var match = ClassDeclaration(className).Match(code);
            if (!match.Success)
            {
                throw new InvalidOperationException("Could not find a declaration for class '" + className + "'.");
            }

            var lineStart = match.Index;
            var declarationLine = code.Substring(lineStart, match.Length);
            var indent = declarationLine.Substring(0, declarationLine.Length - declarationLine.TrimStart(' ', '\t').Length);

            var head = code.Substring(0, lineStart);
            var tail = code.Substring(lineStart);

            // Peel every attribute directly above the class. This happens even when we are
            // not writing attributes, because our remarks block sits above them and cannot
            // be found otherwise; in that case they are all put back untouched.
            var kept = new List<string>();
            while (true)
            {
                var attribute = TrailingAttribute.Match(head);
                if (!attribute.Success)
                {
                    break;
                }

                if (attributes == null || !OwnedAttribute.IsMatch(attribute.Value))
                {
                    kept.Insert(0, attribute.Value);
                }

                head = head.Substring(0, attribute.Index);
            }

            // Our remarks block sits above the attributes, so that it stays contiguous with
            // any hand written summary and the two read as one doc comment.
            if (remarks != null)
            {
                var existing = TrailingRemarks.Match(head);
                if (existing.Success)
                {
                    head = head.Substring(0, existing.Index);
                }
            }

            var newline = code.IndexOf("\r\n", StringComparison.Ordinal) >= 0 ? "\r\n" : "\n";
            var sb = new StringBuilder(head);
            if (remarks != null)
            {
                foreach (var line in remarks)
                {
                    sb.Append(indent).Append(line).Append(newline);
                }
            }

            foreach (var attribute in kept)
            {
                sb.Append(attribute);
            }

            if (attributes != null)
            {
                foreach (var attribute in attributes)
                {
                    // Re-indent continuation lines of multi-line attributes to match the class.
                    var text = attribute.Replace("\r\n", "\n").Replace("\n", newline + indent);
                    sb.Append(indent).Append(text).Append(newline);
                }
            }

            return sb.Append(tail).ToString();
        }

        /// <summary>
        /// Where a matched file stands relative to what a write would put in it.
        /// </summary>
        public enum WriteState
        {
            /// <summary>Nothing of this tool's in the file yet; a write would add it.</summary>
            Missing,
            /// <summary>The file already says exactly what the registration says.</summary>
            Current,
            /// <summary>This tool's output is present but no longer matches the registration.</summary>
            Stale
        }

        /// <summary>
        /// Classifies without writing: the same splice a write would do, compared against
        /// what is there. Null outputs mean the same here as in <see cref="Splice"/> - not
        /// this mode's concern - so the state is always about the mode that is selected.
        /// </summary>
        public static WriteState StateOf(string code, string className, IEnumerable<string> remarks, IEnumerable<string> attributes)
        {
            string updated;
            try
            {
                updated = Splice(code, className, remarks, attributes);
            }
            catch (InvalidOperationException)
            {
                return WriteState.Missing;
            }

            if (string.Equals(code, updated, StringComparison.Ordinal))
            {
                return WriteState.Current;
            }

            return HasOwnedOutput(code, className, attributes != null) ? WriteState.Stale : WriteState.Missing;
        }

        /// <summary>
        /// Whether the class already carries this tool's output in the given mode: owned
        /// attributes directly above the declaration, or the marked remarks block above those.
        /// </summary>
        private static bool HasOwnedOutput(string code, string className, bool attributeMode)
        {
            var match = ClassDeclaration(className).Match(code);
            if (!match.Success)
            {
                return false;
            }

            var head = code.Substring(0, match.Index);
            while (true)
            {
                var attribute = TrailingAttribute.Match(head);
                if (!attribute.Success)
                {
                    break;
                }

                if (attributeMode && OwnedAttribute.IsMatch(attribute.Value))
                {
                    return true;
                }

                head = head.Substring(0, attribute.Index);
            }

            return !attributeMode && TrailingRemarks.IsMatch(head);
        }

        /// <summary>
        /// Writes the spliced file in place. No copy is kept: the source is assumed to be under
        /// source control, which is a better undo than a .bak beside every file.
        /// Returns false when the file already had exactly these attributes.
        /// </summary>
        public static bool Update(string filePath, string className, IEnumerable<string> remarks, IEnumerable<string> attributes)
        {
            var encoding = DetectEncoding(filePath);
            var original = File.ReadAllText(filePath);
            var updated = Splice(original, className, remarks, attributes);

            if (string.Equals(original, updated, StringComparison.Ordinal))
            {
                return false;
            }

            File.WriteAllText(filePath, updated, encoding);
            return true;
        }

        private static Encoding DetectEncoding(string path)
        {
            using (var reader = new StreamReader(path, Encoding.UTF8, true))
            {
                reader.Peek();
                return reader.CurrentEncoding;
            }
        }
    }
}
