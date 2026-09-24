# Writing to files

**Write to Files** takes what is in the preview and puts it in your source. This page is
what it does to a file, and what it will refuse to do.

## Finding the file for a class

The tool searches the **source folder** and everything under it for a `.cs` file that
declares the class, matching on the **short class name** — `AccountManager`, not
`Contoso.Plugins.Accounts.AccountManager`.

Skipped while searching:

- anything under a `\bin\` or `\obj\` folder
- `*.g.cs` and `*.designer.cs`

Exactly one match is required.

- **No match** — the class is reported under *No matching .cs file* and nothing is
  written. This is normal for an assembly whose source is not in the folder you chose.
- **Several matches** — the **registered namespace** breaks the tie: when exactly one of
  the files declares the namespace the type was registered under, that file is the one
  written. Two projects in the same tree that both define a class of the same short name
  therefore resolve themselves, as long as their namespaces differ.
- **Several matches the namespace cannot settle** — reported as *Ambiguous, several files
  declare the class*, and **no file is modified**. A partial class legitimately spans
  files in one namespace, and guessing wrong means writing a registration into the wrong
  class. It is left for you to decide; narrowing the source folder to one project usually
  does it.

The namespace is matched against `namespace X.Y` declarations as written, block or file
scoped. A namespace declared in nested form — `namespace X { namespace Y {` — is not
recognised, and the tie stays unresolved rather than the file being ruled out.

## What is replaced, and what is not

The output is spliced in immediately above the class declaration, at the declaration's own
indentation, in the file's existing line ending and encoding.

The tool replaces **only what it owns**:

| In the file | What happens |
|---|---|
| `[Plugin]`, `[Step]`, `[Image]` above the class | replaced, when writing attribute mode |
| a `<remarks>` block whose first line is `Register:` | replaced, when writing comment mode |
| your `<summary>`, any other `<remarks>` | untouched |
| `[Obsolete]`, `[CrmPluginRegistration]`, any other attribute | untouched, and kept in place |
| the class body | untouched |

Writing one mode never disturbs the other, so a class can carry both, and switching the
toggle does not silently delete the work of the mode you switched away from.

The comment block sits **above** the attributes, so that it stays contiguous with a hand
written `<summary>` and the two read as one doc comment.

## No backups

Files are rewritten in place, in their original encoding, and no copy is kept. The tool
assumes your source is under version control: the write shows up as a diff, and reverting
it is the undo.

A file whose content would not change is not rewritten at all; it is reported as *Already
up to date*.

## The report

The preview is replaced by a tally of what happened, and a dialog gives the same counts:

```
// Updated (6)
//   AccountPreValidation
//   ...

// Already up to date (1)
//   ErpOrderSync

// No matching .cs file (2)
//   Bravo
//   Ghost

// Ambiguous, several files declare the class (1)
//   Worker (2 files)
```

*Failed* appears for anything that threw — a file locked by another process, a read-only
file, a permission problem — with the message beside the class name.

A class that appears twice under *Updated* is a class registered in two ticked assemblies
and resolving to one file. Both registrations were written, in assembly order, and the
second is the one now in the file. Nothing is lost silently: it is named twice, once per
write.

## What is never written

The environment. The tool issues no create, update or delete against Dataverse — it reads
the registration and writes source. Fixing a registration is still the Plugin Registration
Tool's job.
