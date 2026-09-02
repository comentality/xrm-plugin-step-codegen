using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using ThreadExceptionEventHandler = System.Threading.ThreadExceptionEventHandler;
using McTools.Xrm.Connection;
using PluginStepCodegen.Harness;

namespace PluginStepCodegen.SlowHarness
{
    /// <summary>One thing that happens, at a moment, to a tool somebody is using.</summary>
    public class Beat
    {
        public int At;
        public string Label;
        public Action<Run> Do;
    }

    /// <summary>
    /// A tool in a window, an environment that answers slowly, and a list of things that go wrong.
    /// Handed to every beat of a scenario.
    /// </summary>
    public class Run
    {
        public Form Form;
        public PluginStepCodegenControl Control;
        public Probe Probe;
        public SlowService Service;
        public string Folder;
        public string ShotDir;

        /// <summary>Everything the run has to answer for, findings and crashes alike.</summary>
        public readonly List<string> Failures = new List<string>();

        /// <summary>Captions of every dialog the tool put up, in order, closed as they appeared.</summary>
        public readonly List<string> Dialogs = new List<string>();

        public Stopwatch Clock = new Stopwatch();

        public void Check(bool ok, string what)
        {
            if (!ok) Failures.Add(Clock.ElapsedMilliseconds + "ms  " + what);
        }

        /// <summary>
        /// Tells the control which environment it is connected to, the way XrmToolBox does when
        /// a connection is picked. The service is already wired; this is the name the tool files
        /// its memory of the environment under, and without it nothing is remembered at all.
        /// </summary>
        public void Connect(string environment)
        {
            Control.ConnectionDetail = new ConnectionDetail
            {
                ConnectionName = environment,
                EnvironmentId = environment
            };
        }

        /// <summary>
        /// Closes the tab and opens the tool again: a new control, on the same environment and
        /// in the same window. What the tool carries across is exactly what it wrote down.
        /// </summary>
        public void Reopen(string environment)
        {
            Form.Controls.Remove(Control);
            if (!Control.IsDisposed) Control.Dispose();

            Control = new PluginStepCodegenControl { Dock = DockStyle.Fill };
            Scenario.Connect(Control, Service);
            Probe = new Probe(Control);
            Form.Controls.Add(Control);
            Connect(environment);
        }
    }

    public class Scenario
    {
        public string Name;

        /// <summary>One line, printed beside the result: which finding this is about.</summary>
        public string Why;

        /// <summary>How the environment behaves, set before anything is pressed.</summary>
        public Action<SlowService> Wire;

        public readonly List<Beat> Beats = new List<Beat>();

        public Scenario At(int ms, string label, Action<Run> what)
        {
            Beats.Add(new Beat { At = ms, Label = label, Do = what });
            return this;
        }

        /// <summary>
        /// Runs the whole scenario in a window of its own, and returns what went wrong.
        ///
        /// Everything happens on the UI thread with a live message loop, because that is where
        /// the tool lives: WorkAsync marshals its callback back here, the debounce timers tick
        /// here, and a scenario that drove the control from anywhere else would be testing
        /// something nobody does.
        /// </summary>
        public List<string> Play(string root)
        {
            var run = new Run
            {
                ShotDir = Path.Combine(root, Name),
                // A source folder each, because one of these scenarios presses Write and the
                // rest read the marks a write would change. Thrown away and seeded again per
                // run, so a scenario opens on the same folder every time.
                Folder = Path.Combine(root, Name, "src")
            };

            if (Directory.Exists(run.ShotDir)) Directory.Delete(run.ShotDir, true);
            Directory.CreateDirectory(run.ShotDir);

            run.Service = SlowService.Sampled();
            if (Wire != null) Wire(run.Service);
            Sample.SeedSourceFolder(run.Folder, run.Service.Types);

            run.Control = new PluginStepCodegenControl { Dock = DockStyle.Fill };
            Connect(run.Control, run.Service);
            run.Probe = new Probe(run.Control);

            run.Form = new QuietForm
            {
                Text = "Plugin Step Codegen — " + Name,
                ClientSize = new Size(1280, 900)
            };
            run.Form.Controls.Add(run.Control);

            ThreadExceptionEventHandler onThreadException = (s, e) =>
                run.Failures.Add(run.Clock.ElapsedMilliseconds + "ms  the tool threw: " + Head(e.Exception));
            Application.ThreadException += onThreadException;

            var sweeper = new Dialogs(run);

            run.Form.Shown += (s, e) =>
            {
                run.Clock.Start();
                sweeper.Start();

                var queue = new Queue<Beat>(Beats.OrderBy(b => b.At));
                var shot = 0;
                var pump = new Timer { Interval = 25 };
                pump.Tick += (s2, e2) =>
                {
                    while (queue.Count > 0 && run.Clock.ElapsedMilliseconds >= queue.Peek().At)
                    {
                        var beat = queue.Dequeue();
                        try
                        {
                            beat.Do(run);
                        }
                        catch (Exception ex)
                        {
                            run.Failures.Add(run.Clock.ElapsedMilliseconds + "ms  \"" + beat.Label
                                             + "\" threw: " + Head(ex));
                        }

                        // A shot per beat, so a failure is something to look at rather than only
                        // a line to read. Named in order, because the order is the story.
                        try
                        {
                            Capture.Of(run.Form, Path.Combine(run.ShotDir,
                                (++shot).ToString("00") + "-" + Slug(beat.Label) + ".png"));
                        }
                        catch (Exception)
                        {
                            // A shot that cannot be taken is not worth failing a scenario over.
                        }
                    }

                    if (queue.Count > 0) return;

                    pump.Stop();
                    sweeper.Stop();
                    run.Form.Close();
                };
                pump.Start();
            };

            try
            {
                Application.Run(run.Form);
            }
            finally
            {
                Application.ThreadException -= onThreadException;
                sweeper.Stop();
                if (!run.Control.IsDisposed) run.Control.Dispose();
                run.Form.Dispose();
            }

            return run.Failures;
        }

        /// <summary>
        /// Hands the control its connection, the way XrmToolBox does. The setter is protected,
        /// because outside a host nothing has any business setting one - so a harness that is
        /// standing in for the host reaches it the way it reaches everything else here.
        /// </summary>
        internal static void Connect(PluginStepCodegenControl control, Microsoft.Xrm.Sdk.IOrganizationService service)
        {
            const BindingFlags any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
                                     | BindingFlags.DeclaredOnly;

            for (var type = control.GetType(); type != null; type = type.BaseType)
            {
                var property = type.GetProperty("Service", any);
                var setter = property == null ? null : property.GetSetMethod(true);
                if (setter != null)
                {
                    setter.Invoke(control, new object[] { service });
                    return;
                }

                var backing = type.GetField("<Service>k__BackingField", any);
                if (backing != null)
                {
                    backing.SetValue(control, service);
                    return;
                }
            }

            throw new MissingMemberException("PluginControlBase", "Service");
        }

        private static string Head(Exception ex)
        {
            var line = ex.GetBaseException().ToString();
            var stop = line.IndexOf('\n');
            return (stop < 0 ? line : line.Substring(0, stop)).Trim();
        }

        private static string Slug(string label)
        {
            var sb = new StringBuilder();
            foreach (var c in label ?? "beat")
            {
                sb.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-');
            }

            return sb.ToString().Trim('-');
        }
    }

    /// <summary>
    /// Closes whatever modal the tool puts up, and writes down that it did.
    ///
    /// Nothing here is a workaround for the tool being awkward: a message box is a perfectly good
    /// way to report a write, and a run with nobody at the keyboard has to answer it or hang. The
    /// captions are worth keeping either way - "one dialog, not two" is exactly the assertion a
    /// double press is caught by.
    /// </summary>
    internal class Dialogs
    {
        private const int WM_CLOSE = 0x0010;

        [DllImport("user32.dll")]
        private static extern bool EnumThreadWindows(uint threadId, EnumThreadDelegate callback, IntPtr param);

        private delegate bool EnumThreadDelegate(IntPtr hwnd, IntPtr param);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hwnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int count);

        [DllImport("user32.dll")]
        private static extern IntPtr PostMessage(IntPtr hwnd, int message, IntPtr wparam, IntPtr lparam);

        private readonly Run _run;
        private readonly Timer _timer;

        public Dialogs(Run run)
        {
            _run = run;
            // Faster than a person, slower than the message loop. A modal dialog runs its own
            // loop and pumps WM_TIMER, which is the whole reason this works from inside one.
            _timer = new Timer { Interval = 120 };
            _timer.Tick += (s, e) => Sweep();
        }

        public void Start() { _timer.Start(); }

        public void Stop() { _timer.Stop(); _timer.Dispose(); }

        private void Sweep()
        {
            var mine = _run.Form.IsDisposed ? IntPtr.Zero : _run.Form.Handle;
            EnumThreadWindows(GetCurrentThreadId(), (hwnd, param) =>
            {
                if (hwnd == mine || !IsWindowVisible(hwnd)) return true;

                var caption = new StringBuilder(260);
                GetWindowText(hwnd, caption, caption.Capacity);
                var text = caption.ToString();
                if (text.Length == 0) return true;

                _run.Dialogs.Add(text);
                PostMessage(hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                return true;
            }, IntPtr.Zero);
        }
    }
}
