using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using PluginStepCodegen.Harness;
using PluginStepCodegen.Logic;

namespace PluginStepCodegen.UiHarness
{
    /// <summary>
    /// Hosts the tool's control in a bare form, fills it with sample rows and screenshots it, so
    /// layout work can be checked without XrmToolBox and without a Dataverse connection. Run it
    /// through ui.ps1 rather than directly.
    ///
    ///   uiharness.exe &lt;width&gt; &lt;height&gt; &lt;output.png&gt; [comment] [source folder] [window title]
    ///
    /// The three optional arguments exist for the screenshots in the README, which want the other
    /// output mode, a folder path that is somebody's project rather than this machine's, and a
    /// title that names the tool. Everything they change is a value the control would have been
    /// given anyway; nothing about the render is faked for the camera.
    ///
    /// The control is driven the way XrmToolBox drives it - construct, dock, show - so anything
    /// the real tool does on Load and Resize happens here too. Sample data goes in through
    /// reflection: the lists and their backing fields are private, and exposing them for a test
    /// harness would be a worse trade than this file knowing their names.
    ///
    /// This one photographs the layout. Its sibling, the slow harness, drives the same control
    /// against an environment that answers slowly; the sample data and the capture are shared.
    /// </summary>
    internal static class Program
    {
        private const BindingFlags Priv = BindingFlags.NonPublic | BindingFlags.Instance;

        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length < 3)
            {
                Console.Error.WriteLine(
                    "usage: uiharness.exe <width> <height> <output.png> [comment] [source folder] [window title]");
                return 2;
            }

            var width = int.Parse(args[0]);
            var height = int.Parse(args[1]);
            var outPath = args[2];
            var comment = args.Length > 3 && args[3].Equals("comment", StringComparison.OrdinalIgnoreCase);
            // The source column scans whatever this points at, so the default is a seeded sample
            // tree beside the shots rather than this machine's own files.
            var folder = args.Length > 4
                ? args[4]
                : Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[2])), "sample-src");
            var title = args.Length > 5 ? args[5] : "Plugin Step Codegen UI harness";

            // Without this a layout mistake surfaces as a modal error dialog on a machine nobody
            // is looking at, and the run just hangs until it is killed.
            var failures = new List<string>();
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) => failures.Add(e.Exception.ToString());

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            var control = new PluginStepCodegenControl { Dock = DockStyle.Fill };
            var form = new QuietForm
            {
                Text = title,
                ClientSize = new Size(width, height)
            };
            form.Controls.Add(control);

            form.Shown += (s, e) =>
            {
                try { Populate(control, comment, folder); }
                catch (Exception ex) { failures.Add(ex.ToString()); }

                // Let the form settle before grabbing it: the lists redistribute their columns on
                // resize, the preview is debounced and then coloured in a pass of its own, and the
                // source scan runs on a worker and lands whenever it lands.
                var settle = new Timer { Interval = 1800 };
                settle.Tick += (s2, e2) =>
                {
                    settle.Stop();
                    if (Environment.GetEnvironmentVariable("UIHARNESS_DRAG") == "1")
                    {
                        try { File.WriteAllText(outPath + ".drag.txt", Drags(control)); }
                        catch (Exception ex) { failures.Add(ex.ToString()); }
                    }

                    try { Capture.Of(form, outPath); }
                    catch (Exception ex) { failures.Add(ex.ToString()); }
                    form.Close();
                };
                settle.Start();
            };

            Application.Run(form);

            if (failures.Count == 0) return 0;

            var log = outPath + ".error.txt";
            File.WriteAllText(log, string.Join("\n----\n", failures));
            Console.Error.WriteLine("failed, see " + log);
            return 1;
        }

        private const int WM_MOUSEMOVE = 0x0200, WM_LBUTTONDOWN = 0x0201, WM_LBUTTONUP = 0x0202;
        private const int LVM_GETHEADER = 0x101F;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wparam, IntPtr lparam);

        private static IntPtr Point(int x, int y)
        {
            return (IntPtr)((y << 16) | (x & 0xFFFF));
        }

        /// <summary>
        /// A press, a walk and a release, as mouse messages to the window under the pointer. A
        /// drag is the one thing no property set can stand in for: SplitterMoving only fires for
        /// a hand on the splitter, and the header only tracks a divider it saw pressed.
        /// </summary>
        private static void Drag(IntPtr hwnd, int x, int y, int dx, Action during)
        {
            SendMessage(hwnd, WM_LBUTTONDOWN, (IntPtr)1, Point(x, y));
            for (var step = 1; step <= 10; step++)
            {
                SendMessage(hwnd, WM_MOUSEMOVE, (IntPtr)1, Point(x + dx * step / 10, y));
                Application.DoEvents();
            }

            if (during != null) during();
            SendMessage(hwnd, WM_LBUTTONUP, IntPtr.Zero, Point(x + dx, y));
            Application.DoEvents();
        }

        private static void Settle()
        {
            for (var i = 0; i < 20; i++)
            {
                Application.DoEvents();
                System.Threading.Thread.Sleep(10);
            }
        }

        /// <summary>
        /// Drags both splitters and a column divider by hand and says where each ended up, so the
        /// caps can be read as numbers: past the widest line, short of it, and back out again.
        /// </summary>
        private static string Drags(PluginStepCodegenControl control)
        {
            var log = new System.Text.StringBuilder();
            var main = Field<SplitContainer>(control, "_mainSplit");
            var right = Field<SplitContainer>(control, "_rightSplit");
            var types = Field<ListView>(control, "_lvTypes");

            Action<string, SplitContainer, string, int> split = (name, s, natural, dx) =>
            {
                var before = s.SplitterDistance;
                var x = s.SplitterRectangle.X + s.SplitterRectangle.Width / 2;
                var y = s.Height / 2;
                var mid = -1;
                Drag(s.Handle, x, y, dx, () => mid = s.SplitterDistance);
                var released = s.SplitterDistance;
                Settle();
                log.AppendLine(name + " drag " + dx + ": before " + before + ", mid-drag " + mid + ", on release "
                    + released + ", settled " + s.SplitterDistance + ", natural " + Field<int>(control, natural));
            };

            split("left", main, "_leftNatural", 200);
            split("left", main, "_leftNatural", -40);
            Invoke(control, "RenderAssemblies");
            Settle();
            log.AppendLine("left after a re-render: " + main.SplitterDistance);
            split("left", main, "_leftNatural", 200);

            split("source", right, "_sourceNatural", 200);
            split("source", right, "_sourceNatural", -40);
            Invoke(control, "RenderAssemblies");
            Settle();
            log.AppendLine("source after a re-render: " + right.SplitterDistance);
            split("source", right, "_sourceNatural", 200);

            var naturals = Field<Dictionary<ListView, int[]>>(control, "_columnNaturals")[types];
            var header = SendMessage(types.Handle, LVM_GETHEADER, IntPtr.Zero, IntPtr.Zero);
            var divider = types.Columns[0].Width + types.Columns[1].Width;
            var was = types.Columns[1].Width;
            var midWidth = -1;
            Drag(header, divider, 8, 60, () => midWidth = types.Columns[1].Width);
            Settle();
            log.AppendLine("Steps divider drag 60: before " + was + ", mid-drag " + midWidth + ", settled "
                + types.Columns[1].Width + ", natural " + naturals[1]);
            return log.ToString();
        }

        private static T Field<T>(object target, string name)
        {
            var field = target.GetType().GetField(name, Priv);
            if (field == null) throw new MissingFieldException(target.GetType().Name, name);
            return (T)field.GetValue(target);
        }

        private static void Invoke(object target, string name)
        {
            var method = target.GetType().GetMethod(name, Priv);
            if (method == null) throw new MissingMethodException(target.GetType().Name, name);
            method.Invoke(target, null);
        }

        /// <summary>
        /// Fills the control's own state rather than its lists, then asks it to render: everything
        /// the screenshot is meant to show - the Microsoft count on the switch, the status line,
        /// the grouping, the preview - is computed on the way from one to the other.
        /// </summary>
        private static void Populate(PluginStepCodegenControl control, bool comment, string folder)
        {
            var assemblies = Sample.Assemblies();
            control.GetType().GetField("_assemblies", Priv).SetValue(control, assemblies);

            var typesByAssembly = Field<Dictionary<Guid, List<PluginTypeInfo>>>(control, "_typesByAssembly");
            var checkedAssemblies = Field<HashSet<Guid>>(control, "_checkedAssemblies");

            // The three that are ticked: one that carries most of the registrations, one small one,
            // and one that turned out to have nothing registered at all - which the list cannot show
            // and the status line has to.
            // Split the way RegistrationQuery splits it. A type with no steps is never listed -
            // there is nothing to write for it - and its name is kept only so the folder's copy of
            // it is not reported as unregistered. Handing the control the unsplit sample would
            // photograph a row the real tool cannot produce.
            var steplessNames = Field<HashSet<string>>(control, "_steplessClassNames");
            foreach (var assembly in assemblies.GetRange(0, 3))
            {
                checkedAssemblies.Add(assembly.Id);
                var types = Sample.Types(assembly);
                typesByAssembly[assembly.Id] = types.Where(t => t.Steps.Count > 0).ToList();
                foreach (var stepless in types.Where(t => t.Steps.Count == 0))
                {
                    steplessNames.Add(stepless.ClassName);
                }
            }

            // A folder that exists and holds one of everything the scan can find - a current file,
            // a stale one, plain ones, a duplicate pair, an unregistered class - so the source
            // column and every mark it feeds render in one shot. Seeded only when the folder is
            // empty or absent; a folder that already has files in it is left alone.
            Sample.SeedSourceFolder(folder, typesByAssembly);
            Field<TextBox>(control, "_txtFolder").Text = folder;

            // The state being faked is "assemblies loaded", which is what enables Refresh. Set on
            // the flag the control reads rather than on the button, because the button is worked
            // out from that flag every time anything else changes.
            control.GetType().GetField("_loaded", Priv).SetValue(control, true);

            if (comment)
            {
                Field<RadioButton>(control, "_rbComment").Checked = true;
            }

            // The unread marks, for the one shot that is about them: an assembly and a class the
            // control would have judged new against the environment's memory. The judgement is
            // skipped, not faked - the harness has no connection, so it has no memory to judge
            // against - and the sets it would have filled are filled by hand.
            if (Environment.GetEnvironmentVariable("UIHARNESS_NEW") == "1")
            {
                Field<HashSet<Guid>>(control, "_newAssemblies").Add(assemblies[1].Id);
                var fresh = typesByAssembly[assemblies[0].Id];
                Field<HashSet<Guid>>(control, "_newTypes").Add(fresh[fresh.Count - 1].Id);
            }

            // A class with a step somebody is profiling, for the shot about the bug mark. Set on
            // the step exactly as RegistrationQuery sets it once it has read through the
            // profiler's stand-in; everything else about the step already reads as registered.
            // UIHARNESS_PROFILED=both puts it on the class that is also new, so the two marks
            // are seen sharing a row.
            var profiled = Environment.GetEnvironmentVariable("UIHARNESS_PROFILED");
            if (!string.IsNullOrEmpty(profiled))
            {
                var classes = typesByAssembly[assemblies[0].Id];
                var target = profiled == "both" ? classes[classes.Count - 1] : classes[1];
                target.Steps[0].IsProfiled = true;
            }

            Invoke(control, "RenderAssemblies");
            Invoke(control, "RenderTypes");

            // An env var rather than yet another positional argument: only one shot ever wants
            // the output pane collapsed, and the click is the honest way to get there. Deferred
            // until the background scan has rendered, which is the order a person does it in:
            // load, look, collapse.
            if (Environment.GetEnvironmentVariable("UIHARNESS_COLLAPSE_PREVIEW") == "1")
            {
                var collapse = new Timer { Interval = 1200 };
                collapse.Tick += (s, e) =>
                {
                    collapse.Stop();
                    Field<Button>(control, "_btnPreviewToggle").PerformClick();
                    // And a rescan at the collapsed width, so the shot proves the list can be
                    // rebuilt while it is wide - the case a resize glitch would eat rows in.
                    Invoke(control, "StartScan");
                };
                collapse.Start();
            }

            // A source row picked the way a person picks it, once the scan has filled the list
            // and the debounced preview has rendered, so the shot shows the preview following
            // the pick. The row is the third ticked class the scan found, so following it means
            // scrolling rather than already being at the top.
            if (Environment.GetEnvironmentVariable("UIHARNESS_SELECT_SOURCE") == "1")
            {
                var select = new Timer { Interval = 1200 };
                select.Tick += (s, e) =>
                {
                    select.Stop();
                    var blocks = Field<Dictionary<Guid, KeyValuePair<int, int>>>(control, "_previewBlocks");
                    var row = Field<ListView>(control, "_lvSource").Items.Cast<ListViewItem>()
                        .Where(i => i.Tag is Guid && blocks.ContainsKey((Guid)i.Tag))
                        .Skip(2).FirstOrDefault();
                    if (row == null) throw new InvalidOperationException("no source row for a ticked class");
                    row.Selected = true;
                };
                select.Start();
            }
        }
    }
}
