using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Xrm.Sdk.WebServiceClient;
using Microsoft.Xrm.Tooling.Connector;
using PluginProfiler.Library;
using PluginStepCodegen.Logic;

namespace PluginStepCodegen.ProfileHarness
{
    /// <summary>
    /// A profiled step, read the way the tool reads it, has to come out exactly as it did before
    /// anyone profiled it.
    ///
    /// Start Profiling in the plugin registration tool does not leave the step alone. It renames
    /// it "&lt;name&gt; (Profiled)", switches it off, and registers a second step in its place on
    /// the profiler's own plugin type - and moves the step's images onto that one. A tool that
    /// reads the records at face value documents a disabled step with a strange name and no
    /// images, which is a description of somebody's debugging session rather than of the plugin.
    ///
    /// So this profiles every step of the fixture with the registration tool's own library, the
    /// same calls the button makes, reads the fixture through <see cref="RegistrationQuery"/>
    /// before and after, and renders both reads down to every field and every line the two
    /// emitters write. The two renderings have to be identical. Then it stops profiling and
    /// checks that the fixture is back where it started.
    ///
    ///   profileharness.exe &lt;output folder&gt;
    ///
    /// The environment and an access token for it arrive in PSCG_DATAVERSE_URL and
    /// PSCG_DATAVERSE_TOKEN; run it through profile.ps1, which gets both from pac. The exit code
    /// is the number of findings.
    /// </summary>
    internal static class Program
    {
        private const string FixturePrefix = "9a5b3c10-0000-4a00-9000-";
        private const string ProfilerAssembly = "PluginProfiler.Plugins";
        private const string ProfilerType = "PluginProfiler.Plugins.ProfilerPlugin";

        private static readonly List<string> Findings = new List<string>();

        private static int Main(string[] args)
        {
            try
            {
                Run(Path.GetFullPath(args.Length > 0 ? args[0] : ".profile"));
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                return 100;
            }

            Console.WriteLine();
            if (Findings.Count == 0)
            {
                Console.WriteLine("Profiled steps read exactly as unprofiled ones.");
            }
            else
            {
                Console.WriteLine(Findings.Count + " finding(s):");
                foreach (var finding in Findings)
                {
                    Console.WriteLine("  " + finding);
                }
            }

            return Findings.Count;
        }

        private static void Run(string output)
        {
            Directory.CreateDirectory(output);
            var client = Connect();

            var fixture = RegistrationQuery.GetAssemblies(client)
                .Where(a => a.Id.ToString().StartsWith(FixturePrefix, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (fixture.Count == 0)
            {
                throw new InvalidOperationException("The fixture is not registered. Run register.ps1 first.");
            }

            var fixtureIds = fixture.Select(a => a.Id).ToList();
            Console.WriteLine("Fixture: " + string.Join(", ", fixture.Select(a => a.Name)));

            // A run that died half way leaves steps profiled, and a profiled step cannot be
            // profiled again. Put the fixture back first, whatever state it was left in.
            var leftovers = ProfilerStepsFor(client, FixtureSteps(client, fixtureIds, false).Select(e => e.Id).ToList());
            if (leftovers.Count > 0)
            {
                Console.WriteLine("Stopping " + leftovers.Count + " profiler step(s) an earlier run left behind.");
                StopProfiling(client, leftovers);
            }

            var baseline = Render(RegistrationQuery.GetPluginTypes(client, fixtureIds), true);
            File.WriteAllText(Path.Combine(output, "baseline.txt"), baseline);

            Console.WriteLine("Making sure the profiler is installed (an import, the first time).");
            ProfilerManagementUtility.InstallProfiler(client, AppDomain.CurrentDomain.BaseDirectory);

            var profilerAssembly = RegistrationQuery.GetAssemblies(client).FirstOrDefault(a => a.Name == ProfilerAssembly);
            if (profilerAssembly == null)
            {
                throw new InvalidOperationException("The profiler installed, but there is no " + ProfilerAssembly + " assembly to be seen.");
            }

            var profiled = new List<Guid>();
            try
            {
                foreach (var step in FixtureSteps(client, fixtureIds, true))
                {
                    try
                    {
                        profiled.Add(ProfilerManagementUtility.EnablePlugin(client, step.Id));
                    }
                    catch (Exception ex)
                    {
                        Findings.Add("Could not profile " + step.GetAttributeValue<string>("name") + ": " + ex.Message);
                    }
                }

                Console.WriteLine("Profiled " + profiled.Count + " step(s).");
                CheckTheWire(client, fixtureIds, profiled.Count);

                var afterProfiling = Render(RegistrationQuery.GetPluginTypes(client, fixtureIds), true);
                File.WriteAllText(Path.Combine(output, "profiled.txt"), afterProfiling);
                Compare("profiled", baseline, afterProfiling);

                // With the profiler's own assembly in the batch too. Its steps exist only because
                // somebody pressed Start Profiling, so they are not registrations of its type:
                // unprofiled, the profiler is a type nobody registered a step against.
                var withProfiler = RegistrationQuery.GetPluginTypes(client,
                    fixtureIds.Concat(new[] { profilerAssembly.Id }).ToList());
                foreach (var type in withProfiler.Types.Where(t => t.AssemblyId == profilerAssembly.Id))
                {
                    Findings.Add(type.TypeName + " is listed with " + type.Steps.Count + " step(s); profiler steps are not registrations.");
                }

                var fixtureOnly = new TypeFetch
                {
                    Types = withProfiler.Types.Where(t => fixtureIds.Contains(t.AssemblyId)).ToList()
                };
                Compare("profiled, profiler assembly in the batch",
                    Render(RegistrationQuery.GetPluginTypes(client, fixtureIds), false),
                    Render(fixtureOnly, false));
            }
            finally
            {
                Console.WriteLine("Stopping profiling.");
                StopProfiling(client, profiled);
            }

            var restored = Render(RegistrationQuery.GetPluginTypes(client, fixtureIds), true);
            File.WriteAllText(Path.Combine(output, "restored.txt"), restored);
            Compare("profiling stopped", baseline, restored);
        }

        private static CrmServiceClient Connect()
        {
            var url = Environment.GetEnvironmentVariable("PSCG_DATAVERSE_URL");
            var token = Environment.GetEnvironmentVariable("PSCG_DATAVERSE_TOKEN");
            if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(token))
            {
                throw new InvalidOperationException("PSCG_DATAVERSE_URL and PSCG_DATAVERSE_TOKEN are not set. Run profile.ps1.");
            }

            // The registration tool's library wants a CrmServiceClient, and the connector this
            // version ships predates the token callback. A web proxy carrying the bearer token
            // pac already holds is the one door both of them accept. The library's early bound
            // types travel as plain entities only once the proxy knows the assembly they live in;
            // without it, profiling a step with images is refused by the server.
            var proxy = new OrganizationWebProxyClient(
                new Uri(url.TrimEnd('/') + "/XRMServices/2011/Organization.svc/web?SdkClientVersion=9.2.49.14828"),
                typeof(OrganizationTypes.SdkMessageProcessingStepImage).Assembly)
            {
                HeaderToken = token
            };
            proxy.InnerChannel.OperationTimeout = TimeSpan.FromMinutes(10);

            var client = new CrmServiceClient(proxy);
            if (!client.IsReady)
            {
                throw new InvalidOperationException("Could not connect: " + client.LastCrmError);
            }

            return client;
        }

        private static List<Entity> FixtureSteps(IOrganizationService service, List<Guid> assemblyIds, bool enabledOnly)
        {
            var query = new QueryExpression("sdkmessageprocessingstep")
            {
                ColumnSet = new ColumnSet("name", "statecode"),
                Orders = { new OrderExpression("sdkmessageprocessingstepid", OrderType.Ascending) }
            };
            query.AddLink("plugintype", "plugintypeid", "plugintypeid")
                .LinkCriteria.AddCondition("pluginassemblyid", ConditionOperator.In, assemblyIds.Cast<object>().ToArray());
            if (enabledOnly)
            {
                query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
            }

            return service.RetrieveMultiple(query).Entities.ToList();
        }

        /// <summary>
        /// The profiler steps standing in for any of <paramref name="stepIds"/>, found the way the
        /// registration tool's own Stop Profiling finds them: by the original step the
        /// configuration names, read with the profiler's own deserializer.
        /// </summary>
        private static List<Guid> ProfilerStepsFor(IOrganizationService service, ICollection<Guid> stepIds)
        {
            var query = new QueryExpression("sdkmessageprocessingstep") { ColumnSet = new ColumnSet("configuration") };
            query.AddLink("plugintype", "plugintypeid", "plugintypeid")
                .LinkCriteria.AddCondition("typename", ConditionOperator.Equal, ProfilerType);

            var found = new List<Guid>();
            foreach (var step in service.RetrieveMultiple(query).Entities)
            {
                try
                {
                    var configuration = ProfilerManagementUtility.RetrieveConfiguration(step.GetAttributeValue<string>("configuration"));
                    if (configuration.EventHandler != null && stepIds.Contains(configuration.EventHandler.Id))
                    {
                        found.Add(step.Id);
                    }
                }
                catch (Exception)
                {
                    // Not ours to judge: somebody else's profiler step with a configuration
                    // the profiler cannot read either.
                }
            }

            return found;
        }

        private static void StopProfiling(CrmServiceClient client, IEnumerable<Guid> profilerSteps)
        {
            foreach (var id in profilerSteps)
            {
                try
                {
                    ProfilerManagementUtility.DisablePlugin(client, id);
                }
                catch (Exception ex)
                {
                    Findings.Add("Could not stop profiler step " + id + ": " + ex.Message);
                }
            }
        }

        /// <summary>
        /// That profiling happened, and happened the way this harness says it does. Without it a
        /// profiler that had quietly done nothing would leave the two renderings identical and the
        /// run green for the wrong reason.
        /// </summary>
        private static void CheckTheWire(IOrganizationService service, List<Guid> assemblyIds, int profiledCount)
        {
            var steps = FixtureSteps(service, assemblyIds, false);
            var renamed = steps.Count(e =>
                (e.GetAttributeValue<OptionSetValue>("statecode")?.Value ?? 0) == 1
                && (e.GetAttributeValue<string>("name") ?? "").EndsWith(ProfilerManagementUtility.ProfiledStepNameSuffix, StringComparison.Ordinal));
            if (renamed != profiledCount)
            {
                Findings.Add("Profiled " + profiledCount + " step(s) but " + renamed + " read back disabled and renamed.");
            }

            var images = new QueryExpression("sdkmessageprocessingstepimage") { ColumnSet = new ColumnSet(false) };
            images.Criteria.AddCondition("sdkmessageprocessingstepid", ConditionOperator.In, steps.Select(s => (object)s.Id).ToArray());
            var left = service.RetrieveMultiple(images).Entities.Count;
            Console.WriteLine("Images still on the fixture's own steps while profiled: " + left + ".");
        }

        private static void Compare(string what, string expected, string actual)
        {
            if (expected == actual)
            {
                Console.WriteLine("  ok    " + what);
                return;
            }

            var a = expected.Split('\n');
            var b = actual.Split('\n');
            var shown = 0;
            Console.WriteLine("  FAIL  " + what + " - reads differently from the unprofiled fixture:");
            for (var i = 0; i < Math.Max(a.Length, b.Length) && shown < 40; i++)
            {
                var x = i < a.Length ? a[i] : "<nothing>";
                var y = i < b.Length ? b[i] : "<nothing>";
                if (x == y)
                {
                    continue;
                }

                Console.WriteLine("        - " + x.TrimEnd('\r'));
                Console.WriteLine("        + " + y.TrimEnd('\r'));
                shown++;
            }

            Findings.Add(what + ": the fixture does not read the way it did unprofiled");
        }

        /// <summary>
        /// Everything the tool knows about a fetch, as text: every field of every step and image,
        /// and every line both emitters write from them. Equal text is the claim "the tool cannot
        /// tell", rather than "the fields this harness thought to look at agree".
        /// </summary>
        private static string Render(TypeFetch fetch, bool withStepless)
        {
            var text = new StringBuilder();
            foreach (var type in fetch.Types.OrderBy(t => t.TypeName, StringComparer.Ordinal).ThenBy(t => t.Id))
            {
                text.Append("type ").Append(type.TypeName).Append(' ').Append(type.Id).Append('\n');
                foreach (var step in type.Steps)
                {
                    text.Append("  step ").Append(step.Id)
                        .Append(" | ").Append(step.MessageName).Append(' ').Append(step.PrimaryEntityName)
                        .Append(" | stage ").Append(step.Stage).Append(" mode ").Append(step.Mode).Append(" rank ").Append(step.Rank)
                        .Append(" | disabled ").Append(step.IsDisabled)
                        .Append(" | autodelete ").Append(step.AsyncAutoDelete)
                        .Append(" | as ").Append(Quote(step.ImpersonatingUser)).Append('\n');
                    text.Append("    name ").Append(Quote(step.Name)).Append('\n');
                    text.Append("    filter ").Append(Quote(step.FilteringAttributes)).Append('\n');
                    text.Append("    description ").Append(Quote(step.Description)).Append('\n');
                    text.Append("    configuration ").Append(Quote(step.Configuration)).Append('\n');
                    foreach (var image in step.Images.OrderBy(i => i.ImageType).ThenBy(i => i.Name, StringComparer.Ordinal))
                    {
                        text.Append("    image ").Append(image.ImageType)
                            .Append(' ').Append(Quote(image.Name)).Append(' ').Append(Quote(image.EntityAlias))
                            .Append(' ').Append(Quote(image.MessagePropertyName)).Append(' ').Append(Quote(image.Attributes)).Append('\n');
                    }
                }

                foreach (var line in AttributeEmitter.Emit(type))
                {
                    text.Append("  | ").Append(line).Append('\n');
                }

                foreach (var line in RemarksEmitter.Emit(type))
                {
                    text.Append("  # ").Append(line).Append('\n');
                }
            }

            if (withStepless)
            {
                text.Append("stepless ").Append(string.Join(", ", fetch.Stepless.OrderBy(s => s, StringComparer.Ordinal))).Append('\n');
            }

            return text.ToString();
        }

        private static string Quote(string value)
        {
            return value == null ? "null" : "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n") + "\"";
        }
    }
}
