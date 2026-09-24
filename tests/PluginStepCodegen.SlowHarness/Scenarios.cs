using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using PluginStepCodegen.Harness;
using PluginStepCodegen.Logic;

namespace PluginStepCodegen.SlowHarness
{
    /// <summary>
    /// One scenario per thing a slow link does to this tool. Each is a sequence of ordinary
    /// gestures at ordinary intervals, against an environment that takes seconds rather than
    /// milliseconds, and a handful of questions asked of the window in between.
    ///
    /// None of them is about what the tool emits - that is what write.ps1 and the fixtures are
    /// for. They are about the window while the answer is still on its way: which buttons a
    /// person can still press, what the status lines are claiming, and whether the answer that
    /// lands last is the answer to the question asked last.
    /// </summary>
    public static class Scenarios
    {
        private const string Contoso = "Contoso.Plugins";
        private const string Integration = "Contoso.Integration.Plugins";
        private const string Fabrikam = "Fabrikam.Shared.Plugins";

        /// <summary>
        /// A link where one kind of question is slow and the rest are merely remote. Latency is
        /// put on the first query of each fetch rather than spread over all four, so a scenario's
        /// clock is readable: a fetch takes about what its number says.
        /// </summary>
        private static Func<Call, int> Slow(string entity, params int[] perCall)
        {
            return call =>
            {
                if (call.Entity != entity) return 20;
                return perCall[Math.Min(call.Nth - 1, perCall.Length - 1)];
            };
        }

        public static List<Scenario> All()
        {
            return new List<Scenario>
            {
                CloseDuringLoad(),
                RefreshOvertakesLoad(),
                TickWhileLoading(),
                WriteGuarded(),
                PartialTruth(),
                LoadTwice(),
                Cancel(),
                ErrorUnderLatency(),
                LoadFails(),
                WriteFails(),
                Memory(),
            };
        }

        /// <summary>
        /// The tool is used, closed, and opened again on the same environment - twice. In
        /// between, somebody registers a class and an assembly from the IDE.
        ///
        /// Not a slow-link question, but the same bench answers it: the memory is written on the
        /// way and read back on the next load, both of which land in async callbacks, and the
        /// second opening is a fresh control against the same fake environment. What is asked is
        /// what the next opening starts with, and whether it can tell what was not there before.
        /// </summary>
        private static Scenario Memory()
        {
            const string Environment = "slow-harness-memory";
            const string Late = "Contoso.Late.Plugins";

            return new Scenario
            {
                Name = "memory",
                Why = "the next opening must start where this one left off, and say what is new",
                Wire = s => s.Latency = Slow("plugintype", 300)
            }
                .At(0, "connect and point at the folder", r =>
                {
                    r.Connect(Environment);
                    r.Probe.TypeFolder(r.Folder);
                })
                .At(100, "press Load", r => r.Probe.PressLoad())
                .At(600, "tick Contoso.Plugins", r => r.Probe.Tick(Contoso))
                .At(2000, "untick AccountNumberGenerator", r =>
                {
                    r.Check(r.Probe.ClassRow("AccountNumberGenerator") != null,
                        "the class list should have Contoso's classes by now");
                    r.Probe.TickClass("AccountNumberGenerator", false);
                })
                .At(2500, "close the tab", r =>
                {
                    r.Form.Controls.Remove(r.Control);
                    r.Control.Dispose();
                })
                .At(2600, "register a class and an assembly from the IDE", r =>
                {
                    var contoso = r.Service.Assemblies.First(a => a.Name == Contoso);
                    r.Service.Types[contoso.Id].Add(Sample.NewType(contoso,
                        "Contoso.Plugins.Accounts.LateArrival",
                        Sample.NewStep("Create", "account", 40, 0)));

                    var late = Sample.NewAssembly(Late, "a1b2c3d4e5f60718", 2);
                    r.Service.Assemblies.Add(late);
                    r.Service.Types[late.Id] = new List<PluginTypeInfo>
                    {
                        Sample.NewType(late, "Contoso.Late.Plugins.Newcomer",
                            Sample.NewStep("Update", "contact", 40, 0))
                    };
                })
                .At(2700, "open the tool again", r => r.Reopen(Environment))
                .At(2800, "press Load", r => r.Probe.PressLoad())
                .At(5000, "read what came back", r =>
                {
                    var contoso = r.Probe.Row(Contoso);
                    r.Check(contoso != null && contoso.Checked, Contoso + " should be ticked again");
                    var integration = r.Probe.Row(Integration);
                    r.Check(integration != null && !integration.Checked, Integration + " should not be");
                    r.Check(string.Equals(r.Probe.Folder.Text, r.Folder, StringComparison.OrdinalIgnoreCase),
                        "the folder should be back in the box, and is \"" + r.Probe.Folder.Text + "\"");

                    var unticked = r.Probe.ClassRow("AccountNumberGenerator");
                    r.Check(unticked != null && !unticked.Checked, "AccountNumberGenerator should still be unticked");
                    var ticked = r.Probe.ClassRow("AccountPreValidation");
                    r.Check(ticked != null && ticked.Checked, "AccountPreValidation should still be ticked");

                    r.Check(Probe.IsNew(r.Probe.Row(Late)), Late + " was not there last time and should be marked new");
                    r.Check(!Probe.IsNew(contoso), Contoso + " was there last time and should not be marked new");
                    r.Check(Probe.IsNew(r.Probe.ClassRow("LateArrival")), "LateArrival was not there last time and should be marked new");
                    r.Check(!Probe.IsNew(ticked), "AccountPreValidation was there last time and should not be marked new");

                    var lateRow = r.Probe.Row(Late);
                    r.Check(lateRow != null && lateRow.ToolTipText.Contains("New since"),
                        "the new assembly's tooltip should say why it is marked");
                    r.Check(r.Probe.Status.Contains("1 new"),
                        "the status line should count the new assembly, and reads \"" + r.Probe.Status + "\"");
                })
                .At(5200, "close and open once more", r => r.Reopen(Environment))
                .At(5300, "press Load", r => r.Probe.PressLoad())
                .At(7500, "nothing is new now", r =>
                {
                    var contoso = r.Probe.Row(Contoso);
                    r.Check(contoso != null && contoso.Checked, Contoso + " should be ticked a third time");
                    r.Check(r.Probe.Assemblies.Items.Cast<ListViewItem>().All(i => !Probe.IsNew(i)),
                        "everything on the assembly list was there last time; none of it should be marked new");
                    r.Check(r.Probe.Classes.Items.Cast<ListViewItem>().All(i => !Probe.IsNew(i)),
                        "everything on the class list was there last time; none of it should be marked new");
                    r.Check(!r.Probe.Status.Contains("new"),
                        "the status line should count nothing new, and reads \"" + r.Probe.Status + "\"");
                });
        }

        /// <summary>
        /// Somebody gives up and closes the tab while the environment is still thinking. The
        /// answer arrives afterwards and is handed to a control that no longer exists.
        ///
        /// The scan already defends itself against exactly this, and the fetches did not: a
        /// callback that renders into a disposed ListView reaches its Handle, and a disposed
        /// control has none. On a fast link the window is a few milliseconds wide. This is what
        /// makes it seconds wide.
        /// </summary>
        private static Scenario CloseDuringLoad()
        {
            return new Scenario
            {
                Name = "close-during-load",
                Why = "closing the tab mid-fetch must not throw",
                Wire = s => s.Latency = Slow("pluginassembly", 3000)
            }
                .At(0, "point at the folder", r => r.Probe.TypeFolder(r.Folder))
                .At(200, "press Load", r => r.Probe.PressLoad())
                .At(1000, "close the tab", r =>
                {
                    r.Form.Controls.Remove(r.Control);
                    r.Control.Dispose();
                })
                .At(5000, "let the answer land on nobody", r =>
                    r.Check(r.Service.Log("pluginassembly").Count == 1,
                        "the load should have gone out exactly once"));
        }

        /// <summary>
        /// Refresh is pressed while a fetch for the same assembly is still out. Two answers to two
        /// different questions are then in the air at once, and nothing says which is which.
        ///
        /// A class registered between the two is what makes the difference visible: whichever
        /// answer the tool ends up believing either has it or does not. Refresh is the loop this
        /// tool exists for - register from the IDE, come back, refresh, write - so believing the
        /// older of the two is not a curiosity.
        /// </summary>
        private static Scenario RefreshOvertakesLoad()
        {
            return new Scenario
            {
                Name = "refresh-overtakes-load",
                Why = "the answer that lands last must not be the question asked first",
                Wire = s =>
                {
                    s.Latency = call =>
                        call.Entity == "pluginassembly" ? 150 :
                        call.Entity == "plugintype" ? (call.Nth == 1 ? 3500 : 300) : 20;
                }
            }
                .At(0, "point at the folder", r => r.Probe.TypeFolder(r.Folder))
                .At(200, "press Load", r => r.Probe.PressLoad())
                .At(900, "tick Contoso.Plugins", r => r.Probe.Tick(Contoso))
                .At(1500, "register a class from the IDE", r =>
                {
                    var id = r.Probe.IdOf(Contoso);
                    var assembly = r.Service.Assemblies.First(a => a.Id == id);
                    r.Service.Types[id].Add(Sample.NewType(assembly,
                        "Contoso.Plugins.Accounts.LateArrival",
                        Sample.NewStep("Create", "account", 40, 0)));
                })
                .At(1700, "press Refresh", r => r.Probe.PressRefresh())
                .At(7000, "read the list", r =>
                {
                    var id = r.Probe.IdOf(Contoso);
                    List<PluginTypeInfo> fetched;
                    r.Check(r.Probe.Fetched.TryGetValue(id, out fetched)
                            && fetched.Any(t => t.ClassName == "LateArrival"),
                        "the refresh's own answer should be the one kept");
                    r.Check(r.Probe.ClassNames().Contains("LateArrival"),
                        "the class list should show what the refresh found");
                });
        }

        /// <summary>
        /// Three assemblies ticked one after another while the first is still loading. What is
        /// already on its way is invisible to the code that decides what to ask for, so each tick
        /// asks again for everything ticked so far.
        ///
        /// On a fast link that is three overlapping queries nobody notices. On a slow one it is
        /// the same rows fetched over and over on a link that was the problem to begin with.
        /// </summary>
        private static Scenario TickWhileLoading()
        {
            return new Scenario
            {
                Name = "tick-while-loading",
                Why = "an assembly already on its way must not be asked for again",
                Wire = s => s.Latency = Slow("plugintype", 2500)
            }
                .At(0, "point at the folder", r => r.Probe.TypeFolder(r.Folder))
                .At(200, "press Load", r => r.Probe.PressLoad())
                .At(800, "tick Contoso.Plugins", r => r.Probe.Tick(Contoso))
                .At(1400, "tick Contoso.Integration.Plugins", r => r.Probe.Tick(Integration))
                .At(2000, "tick Fabrikam.Shared.Plugins", r => r.Probe.Tick(Fabrikam))
                .At(10000, "count the round trips", r =>
                {
                    var asked = r.Service.Log("plugintype").SelectMany(c => c.Ids).ToList();
                    r.Check(asked.Count == asked.Distinct().Count(),
                        "no assembly should be asked for twice, and "
                        + string.Join(", ", asked.GroupBy(g => g).Where(g => g.Count() > 1)
                            .Select(g => g.Count() + "x")) + " were");
                    r.Check(!r.Service.Overlapped(),
                        "the tool should have one question out at a time");

                    foreach (var name in new[] { Contoso, Integration, Fabrikam })
                    {
                        r.Check(r.Probe.Fetched.ContainsKey(r.Probe.IdOf(name)),
                            name + " should have been fetched by the end");
                    }
                });
        }

        /// <summary>
        /// Write is offered while the environment still owes an answer, and again while the write
        /// it already started is running. The first writes a half-loaded list with a report that
        /// reads like a complete one; the second puts two writers over the same files at once.
        /// </summary>
        private static Scenario WriteGuarded()
        {
            return new Scenario
            {
                Name = "write-guarded",
                Why = "Write must be dead while anything is outstanding, including its own write",
                Wire = s => s.Latency = Slow("plugintype", 2500)
            }
                .At(0, "point at the folder", r => r.Probe.TypeFolder(r.Folder))
                .At(200, "press Load", r => r.Probe.PressLoad())
                .At(800, "tick Contoso.Plugins", r => r.Probe.Tick(Contoso))
                .At(4500, "everything has landed", r =>
                    r.Check(r.Probe.Write.Enabled, "Write should be live once the list is complete"))
                .At(4700, "tick a second assembly", r => r.Probe.Tick(Integration))
                .At(5200, "while it is loading", r =>
                {
                    r.Check(!r.Probe.Write.Enabled,
                        "Write should be dead while an assembly is still loading");
                    r.Check(r.Probe.WriteHint.IndexOf("loading", StringComparison.OrdinalIgnoreCase) >= 0,
                        "the hint should say what it is waiting for, and says \"" + r.Probe.WriteHint + "\"");
                })
                .At(8500, "press Write", r =>
                {
                    r.Check(r.Probe.Write.Enabled, "Write should be live again once both have landed");
                    r.Probe.PressWrite();
                    // Still on the UI thread, so the write cannot have finished and reported
                    // back: whatever the button says now is what a second press would meet.
                    r.Check(!r.Probe.Write.Enabled, "a second press should be impossible mid-write");
                })
                .At(11000, "count the reports", r =>
                    r.Check(r.Dialogs.Count(d => d.IndexOf("Write", StringComparison.OrdinalIgnoreCase) >= 0) == 1,
                        "one write, one report, and there were " + r.Dialogs.Count + " dialogs: "
                        + string.Join(" | ", r.Dialogs)));
        }

        /// <summary>
        /// The counts and the ledger while a fetch is out. "2 assemblies · 5 of 5 classes" is a
        /// complete-sounding sentence about a list that is missing an assembly, and every class of
        /// the assembly still loading is filed, in words, under "In folder, not registered".
        ///
        /// Both un-say themselves when the network catches up, which is the worst way for a tool
        /// to be wrong: it is only wrong while somebody is waiting and looking at it.
        /// </summary>
        private static Scenario PartialTruth()
        {
            return new Scenario
            {
                Name = "partial-truth",
                Why = "the panes must not state as fact what has not arrived",
                Wire = s => s.Latency = Slow("plugintype", 2500)
            }
                .At(0, "point at the folder", r => r.Probe.TypeFolder(r.Folder))
                .At(200, "press Load", r => r.Probe.PressLoad())
                .At(800, "tick Contoso.Plugins", r => r.Probe.Tick(Contoso))
                .At(4200, "settled, and ErpOrderSync really is unregistered", r =>
                    r.Check(r.Probe.SourceRows("In folder, not registered")
                            .Any(row => row.IndexOf("ErpOrderSync", StringComparison.OrdinalIgnoreCase) >= 0),
                        "with only Contoso.Plugins fetched, ErpOrderSync is a genuine finding"))
                .At(4400, "tick the assembly that registers it", r => r.Probe.Tick(Integration))
                .At(5000, "while its answer is still out", r =>
                {
                    r.Check(r.Probe.Status.IndexOf("loading", StringComparison.OrdinalIgnoreCase) >= 0,
                        "the status line should own up to the fetch, and says \"" + r.Probe.Status + "\"");
                    r.Check(!r.Probe.SourceRows("In folder, not registered")
                            .Any(row => row.IndexOf("ErpOrderSync", StringComparison.OrdinalIgnoreCase) >= 0),
                        "nothing can be called unregistered while the assembly that registers it is loading");
                })
                .At(8500, "and once it lands", r =>
                {
                    r.Check(r.Probe.SourceRows("Matched")
                            .Any(row => row.IndexOf("ErpOrderSync", StringComparison.OrdinalIgnoreCase) >= 0),
                        "ErpOrderSync should end up matched");
                    r.Check(r.Probe.Status.IndexOf("loading", StringComparison.OrdinalIgnoreCase) < 0,
                        "and the status line should stop saying it is waiting");
                });
        }

        /// <summary>
        /// Load Assemblies pressed again because the first press did not appear to do anything.
        /// It is the most natural thing in the world on a slow link, and it costs a second full
        /// query - whose callback then clears everything ticked since the first.
        /// </summary>
        private static Scenario LoadTwice()
        {
            return new Scenario
            {
                Name = "load-twice",
                Why = "Load must be dead while it is loading",
                Wire = s => s.Latency = Slow("pluginassembly", 2500)
            }
                .At(0, "point at the folder", r => r.Probe.TypeFolder(r.Folder))
                .At(200, "press Load", r => r.Probe.PressLoad())
                .At(800, "press it again", r =>
                {
                    r.Check(!r.Probe.Load.Enabled, "Load should be dead while it is loading");
                    r.Probe.PressLoad();
                })
                .At(1600, "and again", r => r.Probe.PressLoad())
                .At(5000, "count the round trips", r =>
                {
                    r.Check(r.Service.Log("pluginassembly").Count == 1,
                        "three presses, one query, and there were "
                        + r.Service.Log("pluginassembly").Count);
                    r.Check(r.Probe.Load.Enabled, "and Load should be live again afterwards");
                });
        }

        /// <summary>
        /// The panel's Cancel button, which the tool never offered because no fetch was marked
        /// cancelable. On a slow link the only way out of a fetch was closing the tab, which is
        /// what close-during-load is about.
        ///
        /// A query already on the wire cannot be recalled, so what cancelling buys is the three
        /// round trips after it: a plugin type fetch is types, then steps, then images, then the
        /// columns. Stopping between them is most of the wait on a link where the wait is the
        /// round trips rather than the rows.
        /// </summary>
        private static Scenario Cancel()
        {
            return new Scenario
            {
                Name = "cancel",
                Why = "a fetch must be abandonable, and must leave nothing stuck behind",
                Wire = s => s.Latency = Slow("plugintype", 3000, 800)
            }
                .At(0, "point at the folder", r => r.Probe.TypeFolder(r.Folder))
                .At(200, "press Load", r => r.Probe.PressLoad())
                .At(800, "tick Contoso.Plugins", r => r.Probe.Tick(Contoso))
                .At(1600, "give up", r => r.Probe.PressCancel())
                .At(5500, "nothing more went out", r =>
                {
                    r.Check(r.Service.Log("sdkmessageprocessingstep").Count == 0,
                        "cancelling should stop the round trips that had not happened yet");
                    r.Check(!r.Probe.Fetched.ContainsKey(r.Probe.IdOf(Contoso)),
                        "a cancelled fetch should record nothing");
                    r.Check(r.Probe.Status.IndexOf("cancel", StringComparison.OrdinalIgnoreCase) >= 0,
                        "and should say so, rather than looking like an empty assembly: \""
                        + r.Probe.Status + "\"");
                })
                .At(5700, "untick", r => r.Probe.Tick(Contoso, false))
                .At(6000, "and tick again", r => r.Probe.Tick(Contoso))
                .At(9000, "the same assembly can be asked for again", r =>
                {
                    r.Check(r.Service.Log("plugintype").Count == 2,
                        "the cancelled assembly should be askable again, and "
                        + r.Service.Log("plugintype").Count + " queries went out");
                    r.Check(r.Probe.Fetched.ContainsKey(r.Probe.IdOf(Contoso)),
                        "and should land this time");
                });
        }

        /// <summary>
        /// The link gives up rather than answering. A timeout is what a slow network does when it
        /// is slow enough, and it is the one path here that ends in a dialog somebody has to read.
        ///
        /// What matters afterwards is that the tool is not left holding a half-truth: the panes
        /// should say the fetch failed rather than keeping whatever they had, and the assembly
        /// should be askable again rather than remembered as having been asked.
        /// </summary>
        private static Scenario ErrorUnderLatency()
        {
            return new Scenario
            {
                Name = "error-under-latency",
                Why = "a fetch that fails must say so, and must leave nothing stuck behind",
                Wire = s =>
                {
                    s.Latency = Slow("plugintype", 1500, 400);
                    s.Fails = call => call.Entity == "plugintype" && call.Nth == 1
                        ? new TimeoutException("The request channel timed out while waiting for a reply.")
                        : null;
                }
            }
                .At(0, "point at the folder", r => r.Probe.TypeFolder(r.Folder))
                .At(200, "press Load", r => r.Probe.PressLoad())
                .At(800, "tick Contoso.Plugins", r => r.Probe.Tick(Contoso))
                .At(4000, "read what is left", r =>
                {
                    r.Check(!r.Probe.Fetched.ContainsKey(r.Probe.IdOf(Contoso)),
                        "a failed fetch should record nothing");
                    r.Check(r.Probe.Status.IndexOf("could not", StringComparison.OrdinalIgnoreCase) >= 0
                            || r.Probe.Status.IndexOf("failed", StringComparison.OrdinalIgnoreCase) >= 0,
                        "the status line should carry the failure once the dialog is gone: \""
                        + r.Probe.Status + "\"");
                    r.Check(r.Probe.Load.Enabled, "and the tool should be usable again");
                })
                .At(4200, "untick", r => r.Probe.Tick(Contoso, false))
                .At(4500, "and tick again", r => r.Probe.Tick(Contoso))
                .At(7000, "the same assembly can be asked for again", r =>
                {
                    r.Check(r.Service.Log("plugintype").Count == 2,
                        "a failure should not be remembered as an answer");
                    r.Check(r.Probe.Fetched.ContainsKey(r.Probe.IdOf(Contoso)),
                        "and the retry should land");
                });
        }

        /// <summary>
        /// The other half of error-under-latency, and the one with teeth: the *assembly* fetch
        /// failing rather than the step fetch.
        ///
        /// Load and Refresh are both held down for the duration of a load, so the flag that
        /// holds them down has to come back up on the way out of a failure as surely as on the
        /// way out of a success. If it does not, both buttons are dead for the rest of the
        /// session and the only way on is closing the tab - a tool bricked by one timeout.
        /// </summary>
        private static Scenario LoadFails()
        {
            return new Scenario
            {
                Name = "load-fails",
                Why = "a load that fails must give the tool back, not brick it",
                Wire = s =>
                {
                    s.Latency = Slow("pluginassembly", 1500, 200);
                    s.Fails = call => call.Entity == "pluginassembly" && call.Nth == 1
                        ? new TimeoutException("The request channel timed out while waiting for a reply.")
                        : null;
                }
            }
                .At(0, "point at the folder", r => r.Probe.TypeFolder(r.Folder))
                .At(200, "press Load", r => r.Probe.PressLoad())
                .At(3000, "the tool is still a tool", r =>
                {
                    r.Check(r.Probe.Load.Enabled, "Load must come back, or there is no way on from here");
                    r.Check(r.Probe.Status.IndexOf("could not", StringComparison.OrdinalIgnoreCase) >= 0,
                        "and the status line should say why the list is empty: \"" + r.Probe.Status + "\"");
                })
                .At(3200, "press Load again", r => r.Probe.PressLoad())
                .At(5000, "and this time it lands", r =>
                {
                    r.Check(r.Probe.Assemblies.Items.Count > 0, "the second load should fill the list");
                    r.Check(r.Probe.Refresh.Enabled, "and Refresh should be live behind it");
                    r.Check(r.Probe.Status.IndexOf("could not", StringComparison.OrdinalIgnoreCase) < 0,
                        "with the failure gone from the status line");
                });
        }

        /// <summary>
        /// A write that throws outright rather than reporting per-class failures. The folder is
        /// taken away underneath it, which is what actually happens: a share drops, a sync client
        /// moves something, and the path the marks were drawn against is no longer a folder.
        ///
        /// The same shape as load-fails and the same stakes: Write is held down for the duration
        /// of its own write, so a failure that did not release it would be a Write button dead
        /// for the rest of the session.
        /// </summary>
        private static Scenario WriteFails()
        {
            return new Scenario
            {
                Name = "write-fails",
                Why = "a write that throws must give the tool back, and say the folder is gone",
                Wire = s => s.Latency = Slow("plugintype", 300)
            }
                .At(0, "point at the folder", r => r.Probe.TypeFolder(r.Folder))
                .At(200, "press Load", r => r.Probe.PressLoad())
                .At(800, "tick Contoso.Plugins", r => r.Probe.Tick(Contoso))
                .At(2500, "everything has landed", r =>
                    r.Check(r.Probe.Write.Enabled, "Write should be live"))
                .At(2700, "the folder stops being a folder", r =>
                {
                    // Not merely deleted: the walk steps over a missing directory by design, and
                    // would report every class as "no file" rather than throwing. A file where
                    // the folder was is what the enumeration cannot make sense of at all.
                    Directory.Delete(r.Folder, true);
                    File.WriteAllText(r.Folder, "not a folder any more");
                })
                .At(2900, "press Write", r =>
                {
                    r.Probe.PressWrite();
                    r.Check(r.Probe.Busy != null, "the write should have taken the folder");
                })
                .At(5000, "the tool comes back", r =>
                {
                    r.Check(r.Probe.Busy == null,
                        "a failed write must release the folder, and it still says \"" + r.Probe.Busy + "\"");
                    r.Check(r.Dialogs.Any(d => d.IndexOf("fail", StringComparison.OrdinalIgnoreCase) >= 0),
                        "and should have said so: " + string.Join(" | ", r.Dialogs));
                })
                .At(6500, "and notices what happened to the folder", r =>
                    r.Check(r.Probe.WriteHint.IndexOf("No folder", StringComparison.OrdinalIgnoreCase) >= 0,
                        "the rescan should find the folder gone, and the hint says \""
                        + r.Probe.WriteHint + "\""));
        }
    }
}
