# Limits and troubleshooting

## What the attribute model cannot say

These come from the [Xrm Tools](https://github.com/rezanid/xrmtools) attribute model, not
from this tool — and staying inside it is deliberate, because an attribute Xrm Tools
cannot read back is worth nothing in your source. The summary comment mode is not bound by
any of them, which is part of why it exists.

- **Isolation mode is assembly-level.** `[assembly: PluginAssembly(IsolationMode = ...)]`
  has no per-step equivalent, unlike spkl.
- **Step state is not written in attribute mode.** A registered plugin is a registered
  plugin, so steps are emitted whether or not they are active. The comment marks a disabled
  step, because a comment has no such constraint.
- **`StepAttribute.State` and `StepAttribute.SupportedDeployment` can never be emitted.**
  They are declared as nullable enums upstream, which C# rejects as attribute named
  arguments (`CS0655`). See [Attribute definitions file](attribute-definitions.md).
- **Impersonation is only in the summary comment.** `StepAttribute` carries it as
  `ImpersonatingUserFullname`, a plain string that resolves by name, so a step could point
  at a different user in a different environment. The comment states it as a fact instead
  of trying to redeploy it.

## What the tool does not read

- **Custom API handlers and workflow activities.** Both are plugin types with no
  `sdkmessageprocessingstep`, so they never reach the class list.
- **Assemblies delivered as a plugin package.** The NuGet route registers a
  `pluginpackage`, which nothing here queries.
- **Hidden assemblies.** Platform plumbing, filtered out and not offered.
- **Service endpoint steps.** A step whose event handler is a service endpoint rather than
  a plugin type is not a plugin registration.

## Troubleshooting

**My assembly is not in the list.**
It is probably behind a switch. If it arrived in a solution, turn **Managed** on; if it is
called `Microsoft.*`, turn **Microsoft's** on. See
[Choosing assemblies](choosing-assemblies.md).

**The assembly is ticked but no classes appeared.**
The assembly has plugin types but no registered steps. The status line says so — `· 1 with
no steps` — because an empty group draws nothing in the list. Nothing is wrong, and there
is nothing to write.

**A class I expected is missing from the list.**
Only classes with at least one registered step are listed. A base class, or a type
registered but never given a step, has nothing to document. If its `.cs` file is under the
source folder it appears in the middle column under **Registered, no steps** — which is a
different finding from **In folder, not registered**, and asks for a different thing:
register a step, rather than register the class.

**Everything is reported as *No matching .cs file*.**
The source folder is pointing somewhere else, or at a folder that only holds compiled
output. Point it at the root of your plugin project or repository.

**A class is reported as ambiguous.**
Two files under the source folder declare a class of that name, and the registered
namespace could not settle it — usually because both declare the same namespace, as a
partial class does. Narrow the source folder to a single project, or rename one of the
classes. Nothing is written for an ambiguous class, so no harm has been done.

**The write dialog says files were skipped.**
Read the report in the preview pane — it names every class and the reason. *Failed* entries
carry the exception message, which is usually a locked or read-only file.

**The emitted attributes do not compile.**
Either the attribute types are missing — add the package or the definitions file — or they
are there twice, which is `CS0101` and means you have both. See
[Attribute definitions file](attribute-definitions.md).

**I want the old file back.**
From source control. The tool keeps no copy of its own; it assumes your source is under
version control, and the write is a diff you can revert like any other.

**Something threw.**
Errors are shown with their detail rather than swallowed. If it looks like a bug in the
tool, [open an issue](https://github.com/comentality/xrm-plugin-step-codegen/issues) with the
message and what was ticked at the time.
