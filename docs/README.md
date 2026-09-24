# Plugin Step Codegen documentation

The tool reads the plugin steps and images registered in a Dataverse environment and
writes them into your C# source, either as [Xrm Tools](https://github.com/rezanid/xrmtools)
attributes or as a readable summary comment.

The attributes are Xrm Tools', and compatibility with it is the premise: what this writes
is what Xrm Tools reads back. It is a Visual Studio extension that turns those same
attributes into the registration — one-click deploy, Dataverse-aware IntelliSense, typed
entity and plugin generation, a FetchXML designer — and if you are not using it yet,
[it is worth a look](https://github.com/rezanid/xrmtools). This tool runs the other way:
from an environment somebody already registered, back into your source.

| | |
|---|---|
| [Getting started](getting-started.md) | Install it, connect, and do a first run. |
| [Choosing assemblies](choosing-assemblies.md) | Why the list starts short, what the two switches hold, how the filter behaves, and what is remembered between sessions. |
| [What gets written](output.md) | Both output modes in full: what is emitted, what is suppressed, and in what order. |
| [Writing to files](writing-files.md) | How a class is matched to a file, what is replaced, and the report. |
| [Attribute definitions file](attribute-definitions.md) | Making the emitted attributes compile, with or without the NuGet package. |
| [Limits and troubleshooting](limits.md) | What the tool cannot express, and what to do when a run does not go as expected. |

## The shape of a run

1. **Load Assemblies** — the unmanaged plugin assemblies in the environment, which is
   what a plugin you are writing is registered as.
2. Tick the assemblies you are documenting. Their classes appear below, grouped by
   assembly, and the preview fills in.
3. Point **Source folder** at the folder holding your `.cs` files.
4. Choose the output mode. The preview follows.
5. **Write to Files**.

Nothing is written to the environment at any point. The tool reads, and the only thing it
changes is your source, in place — source control is the undo.

## A note on the screenshots

Every screenshot in this documentation is the real control, rendered by
`tests\ui.ps1 -Docs`, with sample rows standing in for a connection. What you see is the
layout the tool actually produces; only the data is invented.
