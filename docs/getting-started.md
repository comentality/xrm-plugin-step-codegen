# Getting started

## Install

Plugin Step Codegen is an [XrmToolBox](https://www.xrmtoolbox.com/) tool, so it installs the
way every other one does:

1. Open XrmToolBox.
2. **Tool Library** → search for **Plugin Step Codegen** → **Install**.
3. Restart XrmToolBox when it asks.

It needs XrmToolBox 1.2025.7 or later and the .NET Framework 4.8 runtime that XrmToolBox
itself requires. There is nothing else to install, and nothing to configure.

To install a build of your own instead, `.\build.ps1` in this repository compiles the tool
and copies the DLL into your local XrmToolBox `Plugins` folder.

## Connect

Open the tool and connect it to the environment your plugin is *registered in* — which is
usually your development environment rather than the one your code will end up in. The
tool reads:

- `pluginassembly` — the assembly list
- `plugintype` — the classes
- `sdkmessageprocessingstep` and `sdkmessageprocessingstepimage` — the registrations
- `sdkmessage`, `sdkmessagefilter` and `systemuser` — the names behind those

It issues no create, update or delete against the environment. A read-only security role is
enough.

## A first run

![The tool with three assemblies ticked and the attribute output in the preview](https://raw.githubusercontent.com/comentality/xrm-plugin-step-codegen/main/assets/ui-attributes.png)

**1. Load Assemblies.** The list fills with the unmanaged assemblies in the environment —
the ones somebody is writing rather than the ones somebody shipped. If yours is not there,
see [Choosing assemblies](choosing-assemblies.md); it is usually one switch away.

**2. Tick what you are documenting.** Ticking an assembly loads its plugin types, and the
class list below groups them under the assembly they were registered from. Classes with no
registered step are not listed at all — there is nothing to write for them — but they are
counted on the status line, so an assembly that turns out to be empty says so rather than
silently contributing nothing.

The status line under the filter box is the summary of the whole left pane:

```
3 assemblies · 7 of 7 classes · 1 with no steps
```

**3. Set the source folder.** Type it, paste it, or use **Browse…**. This is the folder the
tool searches for `.cs` files, so it should be the root of your plugin project — or of the
whole repository, if you have several projects and want one pass over all of them.

**4. Choose the output.** *Xrm Tools attributes* writes real attributes that compile;
*Readable summary comment* writes prose for a human. They are independent — a class can
carry both, and writing one never removes the other. The preview shows exactly what would
go into the files, and updates as you tick, untick and switch modes.

**5. Write to Files.** Every class the tool could match is updated in place, and the
preview is replaced by a report of what happened:

```
// Updated (6)
//   AccountPreValidation
//   AccountNumberGenerator
//   ...

// Already up to date (1)
//   ErpOrderSync
```

Files are rewritten in place, with no backup copy: review the change in your source
control's diff, and revert there if you do not like it. Run it twice and the
second run reports everything as already up to date: the output is stable, so it is safe to
put in a habit.

## Then what

The point of all this is that the registration is now in the diff. Commit it, and the next
person to read the class finds out what it is registered on without opening a tool.

If you want the emitted attributes to *compile*, you need either the `XrmTools.Meta.Attributes`
package or the definitions file this tool can write for you — see
[Attribute definitions file](attribute-definitions.md). The summary comment mode needs
neither: it is a comment.

And if the attributes are new to you, meet the tool they belong to:
**[Xrm Tools](https://github.com/rezanid/xrmtools)**, a Visual Studio extension that reads
them back the way this one writes them — deploying and registering the assembly from the
source, with Dataverse-aware IntelliSense, typed entity generation and a FetchXML designer
alongside. Documenting an existing registration is the half of the round trip that lives
here; the other half is worth having.
