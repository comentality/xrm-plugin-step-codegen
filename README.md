# ![](https://raw.githubusercontent.com/comentality/xrm-plugin-step-codegen/main/PluginStepCodegen/icon.png) Plugin Step Codegen

[![NuGet](https://img.shields.io/nuget/v/Comentality.PluginStepCodegen)](https://www.nuget.org/packages/Comentality.PluginStepCodegen)

An [XrmToolBox](https://www.xrmtoolbox.com/) tool that documents your Dataverse plugin
step registrations **in your C# source**.

It reads the steps and images registered in the connected environment and writes them
back into your plugin classes as [Xrm Tools](https://github.com/rezanid/xrmtools)
compatible `[Plugin]`, `[Step]` and `[Image]` attributes.

Your registration stops living only in an environment you have to go look at, and starts
living in the code review, the diff and the git history.

![Plugin Step Codegen: assemblies and classes on the left, the attributes it would write on the right](https://raw.githubusercontent.com/comentality/xrm-plugin-step-codegen/main/assets/ui-attributes.png)

## Why

Registration lives in the environment, source lives in git, and nothing keeps them
honest. The only existing tool that closes the gap is `spkl instrument`, a CLI buried in
the largely dormant SparkleXrm framework. This does the same job from inside XrmToolBox,
against the modern attribute model.

## Built for Xrm Tools — go and look at it

The attribute model is **[Xrm Tools](https://github.com/rezanid/xrmtools)**', and being
compatible with it is this tool's whole premise: what gets written here is what Xrm Tools
reads back, down to the constructor overload it binds and the order the attributes are
in. Every release is compiled and evaluated against the real
[`XrmTools.Meta.Attributes`](https://www.nuget.org/packages/XrmTools.Meta.Attributes)
package at four versions to keep that true.

If you are not using it yet, it is worth the detour. It is a Visual Studio extension —
*"all the missing features for Power Platform, one release at a time"* — that turns those
same attributes into the registration itself: one-click assembly deploy and step
registration, Dataverse-aware IntelliSense over your metadata, typed entity and plugin
generation, and a FetchXML designer, with no build-time codegen and no telemetry.

The two run in opposite directions and meet in the middle. Xrm Tools takes the attributes
in your source and makes the environment match them; this takes an environment somebody
already registered by hand and writes it back into your source in the same shape — which
is exactly what you want on the day you inherit a plugin project with no attributes in it
at all.

## Install

**Tool Library** in XrmToolBox → search for **Plugin Step Codegen** → **Install**. Nothing
else to set up, and nothing to configure.

The Tool Library installs it from nuget, where it lives as
[`Comentality.PluginStepCodegen`](https://www.nuget.org/packages/Comentality.PluginStepCodegen).

## What it does

1. **Load Assemblies** lists the unmanaged plugin assemblies in the connected environment —
   the ones somebody is writing. Microsoft's and everything else shipped in a solution are
   a switch away.
2. Ticking one — or **All** of them, for a project that ships an assembly per plugin —
   loads every plugin type that has at least one registered step, grouped by assembly.
3. **Write** chooses the output: *Xrm Tools attributes* or a *readable summary comment*.
4. The preview pane shows exactly what would be written, and follows the ticks and the
   mode as you change them.
5. **Write to Files** finds the `.cs` file declaring each class and splices the output
   in above the class declaration.
6. **Create Attribute Definitions File** drops a dependency-free
   `XrmToolsMetaAttributes.cs` into your project so the emitted attributes compile
   without the NuGet package or the Visual Studio extension.

Files are rewritten in place with no backup copy — your source control is the undo — and
nothing is ever written to the environment.

## Output

Two shapes, chosen with the **Write** toggle. They are independent: each replaces only
its own block, so switching modes never deletes the other one's work, and a class can
carry both.

### Xrm Tools attributes

```csharp
/// <summary>Handles account writes.</summary>
[Obsolete("your own attributes are left alone")]
[Plugin(Description = "Keeps account data consistent.")]
[Step("Create", "account", Stages.PreOperation, ExecutionMode.Synchronous)]
[Image(ImageTypes.PostImage, "name")]
[Step("Update", "account", "name,address1_line1", Stages.PostOperation, ExecutionMode.Asynchronous,
    Name = "Recalculate rollups",
    ExecutionOrder = 25,
    Description = "Runs after the write completes.",
    AsyncAutoDelete = true)]
[Image(ImageTypes.PreImage, "name", Name = "Before", EntityAlias = "Before")]
[Step("Associate", Stages.PreValidation, ExecutionMode.Synchronous)]
public partial class AccountManager : IPlugin
```

Style follows the [`XrmTools.Meta.Attributes`](https://github.com/rezanid/xrmtools)
README: the widest positional constructor the step's data supports, remaining facts as
named properties, wrapping one argument per line only when the line gets long.
**Attribute order is load bearing** — `[Image]` binds to the nearest preceding `[Step]`,
so steps are written in execution order with their own images following them, which is
the order Xrm Tools reads them back in and not ours to rearrange.

### Readable summary comment

The same registration as prose, for the reader rather than the compiler:

```csharp
/// <summary>Handles course history.</summary>
/// <remarks>
/// Register:
/// Sync Pre-Delete of ilac_class (order 1, disabled, As SYSTEM)
///     PreImage: (all columns)
/// Sync Post-Create of mshied_coursehistory (order 3): ilac_suggestedesllevel
///     PreImage:
///         mshied_academicperioddetailsid, ilac_class, mshied_courseid, ilac_currentlevel,
///         ilac_enddate, ilac_exitlevel, ilac_isstudentleaving, ilac_sessiontype
/// Sync Post-Update of mshied_coursehistory (order 1):
///     ilac_enddate, mshied_enrollmentstatus, ilac_startdate, ilac_suggestedesllevel
/// </remarks>
public partial class CourseHistoryHandler : IPlugin
```

Because nothing here has to compile, the comment carries two facts no attribute can
express: a **disabled** step, and the user a step impersonates, as `As <name>`.

![The same classes with the readable summary comment selected](https://raw.githubusercontent.com/comentality/xrm-plugin-step-codegen/main/assets/ui-comment.png)

## Documentation

| | |
|---|---|
| [Getting started](https://github.com/comentality/xrm-plugin-step-codegen/blob/main/docs/getting-started.md) | Install it, connect, and do a first run. |
| [Choosing assemblies](https://github.com/comentality/xrm-plugin-step-codegen/blob/main/docs/choosing-assemblies.md) | Why the list starts short, what the two switches hold, how the filter behaves, and what is remembered between sessions. |
| [What gets written](https://github.com/comentality/xrm-plugin-step-codegen/blob/main/docs/output.md) | Both output modes in full: what is emitted, what is suppressed, and in what order. |
| [Writing to files](https://github.com/comentality/xrm-plugin-step-codegen/blob/main/docs/writing-files.md) | How a class is matched to a file, what is replaced, and the report. |
| [Attribute definitions file](https://github.com/comentality/xrm-plugin-step-codegen/blob/main/docs/attribute-definitions.md) | Making the emitted attributes compile, with or without the NuGet package. |
| [Limits and troubleshooting](https://github.com/comentality/xrm-plugin-step-codegen/blob/main/docs/limits.md) | What the tool cannot express, and what to do when a run does not go as expected. |

## Building

```powershell
.\build.ps1     # build Debug and copy the DLL into your local XrmToolBox Plugins folder
.\deploy.ps1    # copy the existing Debug DLL without rebuilding
.\publish.ps1   # build Release, pack, and push to NuGet.org
```

## Testing

`tests/` holds an end to end suite: six assemblies of empty plugin classes, under two
publishers, registered every way this tool has to describe — four in managed solutions and
two by hand, the way a plugin you are writing is registered — driven entirely by `pac`.
Several assemblies rather than one because that is where the interesting failures live:
a class name that is not unique across a source tree, an assembly whose source is missing,
an assembly named Microsoft that is nothing of the sort.

```powershell
cd tests
.\register.ps1      # build the assemblies, pack three solutions, import them
.\verify.ps1        # confirm the environment matches the test matrix
.\write.ps1         # run the real emit and write path over sandbox copies of the fixtures
.\compat.ps1        # check what is emitted against the real XrmTools package from nuget
.\perf.ps1          # time the scan and the write over generated repositories
.\unregister.ps1    # take it all away again
.\xtb.ps1           # build the tool and open it in an XrmToolBox of its own
.\ui.ps1            # screenshot the layout without XrmToolBox or a connection
```

`write.ps1`, `compat.ps1` and `perf.ps1` need no environment and no connection.

`perf.ps1` generates plugin repositories of 250, 1000 and 4000 files and times the real
scan and the real write over them, judged against what reading the folder once costs on
the same machine. It exists because the answer to "why is this slow" was never the disk:
a phase costing many times a read is a phase reading the folder once per class.

`compat.ps1` is the one that keeps the tool's premise honest. It builds a corpus hitting
every decision the emitter makes, compiles it against both the generated definitions file
and the real `XrmTools.Meta.Attributes` package at four versions, and checks that the two
do not merely both compile but mean the same thing — same constructor bound, same value in
every property of the constructed attribute, defaults included.

`xtb.ps1` puts a private XrmToolBox in `tests\.xtb` holding nothing but this tool, connects
it to the same environment and opens it, so testing a change is one command and cannot
disturb the XrmToolBox you work in.

Then compare what the tool writes with the expected output in
[tests/README.md](https://github.com/comentality/xrm-plugin-step-codegen/blob/main/tests/README.md),
which spells out, class by class, exactly what both output modes should produce.

## License

MIT
