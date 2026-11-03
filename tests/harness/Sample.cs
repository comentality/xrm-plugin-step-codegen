using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PluginStepCodegen.Logic;

namespace PluginStepCodegen.Harness
{
    /// <summary>
    /// An environment and a source folder that between them hold one of everything the tool has
    /// to draw: assemblies of three kinds, classes in every scan state, and a folder that agrees
    /// with them. Shared by the two harnesses because they are looking at the same tool from two
    /// sides - one photographs its layout, the other holds it up against a slow network - and a
    /// second copy of this would let the two disagree about what "the sample" means.
    ///
    /// Everything here is the shape <see cref="RegistrationQuery"/> returns. The UI harness hands
    /// it to the control directly; the slow harness projects it back down into the records a
    /// Dataverse would have answered with, so the query code runs too.
    /// </summary>
    public static class Sample
    {
        public static List<AssemblyInfo> Assemblies()
        {
            var own = new List<AssemblyInfo>
            {
                NewAssembly("Contoso.Crm.Sales.Plugins", "a1b2c3d4e5f60718", 2),
                NewAssembly("Contoso.Crm.Integration.ErpConnector.Plugins", "a1b2c3d4e5f60718", 2),
                // Nothing registered against it, and outside the sandbox besides.
                NewAssembly("Fabrikam.Dynamics.Shared.Common.Plugins", "0f2c9a1b7d3e4655", 1),
                NewAssembly("MsContoso.Crm.Extensions.Plugins", "9d8c7b6a5e4f3021", 2),
                // An ISV's app: neither Microsoft's nor yours, and the reason the Managed switch
                // needs a bucket of its own rather than being read off the signature.
                NewAssembly("Northwind.Traders.Suite.Plugins", "4c5d6e7f80912a3b", 2, managed: true),
            };

            // Enough of Microsoft's own to make the point the switch exists for: they outnumber
            // yours by an order of magnitude in any real environment.
            var microsoft = new[]
            {
                "Microsoft.Crm.ObjectModel", "Microsoft.Crm.Extensibility.MessageHandlers",
                "Microsoft.Dynamics.Sales.Plugins", "Microsoft.Dynamics.Sales.OrderProcessing",
                "Microsoft.Dynamics.Sales.Insights.Plugins", "Microsoft.PowerApps.Checker.Plugins",
                "Microsoft.Xrm.Portal.Plugins", "Microsoft.Dynamics.Solutions.AppProfileManager",
                "Microsoft.Dynamics.Field.Service.Plugins", "Microsoft.Crm.ScheduledJobs",
                "Microsoft.Dynamics.CustomerInsights.Plugins", "Microsoft.Dynamics.Forms.Plugins",
            };

            foreach (var name in microsoft)
            {
                own.Add(NewAssembly(name, "31bf3856ad364e35", 2, managed: true));
            }

            return own;
        }

        public static AssemblyInfo NewAssembly(string name, string key, int isolation, bool managed = false)
        {
            return new AssemblyInfo
            {
                Id = Guid.NewGuid(),
                Name = name,
                PublicKeyToken = key,
                IsolationMode = isolation,
                IsManaged = managed
            };
        }

        public static List<PluginTypeInfo> Types(AssemblyInfo assembly)
        {
            switch (assembly.Name)
            {
                case "Contoso.Crm.Sales.Plugins":
                    return new List<PluginTypeInfo>
                    {
                        NewType(assembly, "Contoso.Crm.Sales.Plugins.Accounts.PreValidation.AccountPreValidationPlugin",
                            NewStep("Create", "account", 10, 0)),
                        NewType(assembly, "Contoso.Crm.Sales.Plugins.Accounts.Numbering.AccountNumberGeneratorPlugin",
                            NewStep("Create", "account", 20, 0),
                            NewStep("Update", "account", 20, 0, "name,telephone1")),
                        NewType(assembly, "Contoso.Crm.Sales.Plugins.Contacts.Deduplication.ContactDeduplicationPlugin",
                            WithImage(NewStep("Create", "contact", 40, 0), "PostImage"),
                            NewStep("Update", "contact", 40, 1, "emailaddress1")),
                        // Registered as a type but with no step against it: nothing to document.
                        NewType(assembly, "Contoso.Crm.Sales.Plugins.Shared.Infrastructure.PluginBase"),
                        // The same, but its file is an ordinary class rather than a base: somebody
                        // registered it and has not written a step for it yet. The scan must not
                        // report that file as a class nobody registered, which is the opposite of
                        // what happened to it.
                        NewType(assembly, "Contoso.Crm.Sales.Plugins.Leads.Routing.LeadRoutingPlugin"),
                        NewType(assembly, "Contoso.Crm.Sales.Plugins.Opportunities.Closing.OpportunityCloseAuditPlugin",
                            NewStep("Win", "opportunity", 40, 1)),
                    };

                case "Contoso.Crm.Integration.ErpConnector.Plugins":
                    return new List<PluginTypeInfo>
                    {
                        NewType(assembly, "Contoso.Crm.Integration.ErpConnector.Plugins.Orders.Outbound.ErpOrderSyncPlugin",
                            WithImage(NewStep("Update", "salesorder", 40, 1, "statecode"), "PreImage")),
                        NewType(assembly, "Contoso.Crm.Integration.ErpConnector.Plugins.Webhooks.Retries.WebhookRetryHandlerPlugin",
                            NewStep("contoso_RetryFailedSync", "none", 30, 0)),
                    };

                default:
                    return new List<PluginTypeInfo>();
            }
        }

        public static PluginTypeInfo NewType(AssemblyInfo assembly, string typeName, params PluginStepInfo[] steps)
        {
            return new PluginTypeInfo
            {
                Id = Guid.NewGuid(),
                AssemblyId = assembly.Id,
                TypeName = typeName,
                FriendlyName = typeName,
                Steps = new List<PluginStepInfo>(steps)
            };
        }

        public static PluginStepInfo NewStep(string message, string entity, int stage, int mode, string filter = null)
        {
            return new PluginStepInfo
            {
                Id = Guid.NewGuid(),
                MessageName = message,
                PrimaryEntityName = entity,
                Stage = stage,
                Mode = mode,
                Rank = 1,
                FilteringAttributes = filter,
                Name = "Contoso: " + message + " of " + entity
            };
        }

        public static PluginStepInfo WithImage(PluginStepInfo step, string alias)
        {
            step.Images.Add(new PluginImageInfo
            {
                ImageType = alias == "PreImage" ? 0 : 1,
                EntityAlias = alias,
                Name = alias,
                Attributes = "name,accountid,ownerid"
            });
            return step;
        }

        /// <summary>
        /// One source file per state the scan can report, laid out the way a repository holding
        /// several plugin projects is: a folder per project, then folders that mirror the
        /// namespaces a few levels down. WebhookRetryHandlerPlugin deliberately gets no file (not
        /// found), OpportunityCloseAuditPlugin gets two in the same namespace (ambiguous), two
        /// classes exist that nothing registers, and LeadRoutingPlugin is registered with no step
        /// against it - a file in none of the three states, which is the point of it.
        /// </summary>
        public static void SeedSourceFolder(string folder, Dictionary<Guid, List<PluginTypeInfo>> typesByAssembly)
        {
            if (Directory.Exists(folder) && Directory.EnumerateFileSystemEntries(folder).Any())
            {
                return;
            }

            Directory.CreateDirectory(folder);
            var byName = typesByAssembly.Values.SelectMany(t => t).ToDictionary(t => t.ClassName);

            const string Sales = "Contoso.Crm.Sales.Plugins";
            const string Erp = "Contoso.Crm.Integration.ErpConnector.Plugins";

            Seed(folder, Sales + @"\Shared\Infrastructure\PluginBase.cs",
                PluginFile(Sales + ".Shared.Infrastructure", "PluginBase", "IPlugin", isAbstract: true));
            Seed(folder, Sales + @"\Accounts\PreValidation\AccountPreValidationPlugin.cs",
                WithAttributes(byName["AccountPreValidationPlugin"], stale: false));
            Seed(folder, Sales + @"\Accounts\Numbering\AccountNumberGeneratorPlugin.cs",
                WithAttributes(byName["AccountNumberGeneratorPlugin"], stale: true));
            Seed(folder, Sales + @"\Contacts\Deduplication\ContactDeduplicationPlugin.cs",
                PluginFile(Sales + ".Contacts.Deduplication", "ContactDeduplicationPlugin", "PluginBase"));
            Seed(folder, Sales + @"\Opportunities\Closing\OpportunityCloseAuditPlugin.cs",
                PluginFile(Sales + ".Opportunities.Closing", "OpportunityCloseAuditPlugin", "PluginBase"));
            Seed(folder, Sales + @"\Legacy\Opportunities\Closing\OpportunityCloseAuditPlugin.cs",
                PluginFile(Sales + ".Opportunities.Closing", "OpportunityCloseAuditPlugin", "PluginBase"));
            Seed(folder, Erp + @"\Orders\Outbound\ErpOrderSyncPlugin.cs",
                PluginFile(Erp + ".Orders.Outbound", "ErpOrderSyncPlugin", "PluginBase"));
            Seed(folder, Sales + @"\Contacts\Merging\ContactMergerPlugin.cs",
                PluginFile(Sales + ".Contacts.Merging", "ContactMergerPlugin", "IPlugin"));
            Seed(folder, Sales + @"\Leads\Scoring\LeadScoringPlugin.cs",
                PluginFile(Sales + ".Leads.Scoring", "LeadScoringPlugin", "PluginBase"));
            Seed(folder, Sales + @"\Leads\Routing\LeadRoutingPlugin.cs",
                PluginFile(Sales + ".Leads.Routing", "LeadRoutingPlugin", "PluginBase"));
        }

        private static void Seed(string folder, string relative, string content)
        {
            var path = Path.Combine(folder, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, content);
        }

        /// <summary>
        /// The registration's own attributes spliced in, exactly as a write would leave them -
        /// which is what "current" means. Stale trims the emission to one step first, so the
        /// file carries this tool's output and no longer matches.
        /// </summary>
        private static string WithAttributes(PluginTypeInfo type, bool stale)
        {
            var emitted = type;
            if (stale)
            {
                emitted = new PluginTypeInfo
                {
                    Id = type.Id,
                    AssemblyId = type.AssemblyId,
                    TypeName = type.TypeName,
                    Steps = type.Steps.Take(1).ToList()
                };
            }

            var bare = PluginFile(type.Namespace, type.ClassName, "PluginBase");
            return CodeFileWriter.Splice(bare, type.ClassName, null, AttributeEmitter.Emit(emitted));
        }

        public static string PluginFile(string ns, string className, string baseName, bool isAbstract = false)
        {
            return "using System;\r\n"
                   + "using Microsoft.Xrm.Sdk;\r\n"
                   + "\r\n"
                   + "namespace " + ns + "\r\n"
                   + "{\r\n"
                   + "    public " + (isAbstract ? "abstract " : "") + "class " + className + " : " + baseName + "\r\n"
                   + "    {\r\n"
                   + "        public void Execute(IServiceProvider serviceProvider)\r\n"
                   + "        {\r\n"
                   + "        }\r\n"
                   + "    }\r\n"
                   + "}\r\n";
        }
    }
}
