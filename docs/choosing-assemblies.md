# Choosing assemblies

An environment carries dozens of plugin assemblies and one or two of yours. **Load
Assemblies** loads all of them and then shows you the short list, because the long one is
not a list anybody can work with.

## What the list shows by default

The **unmanaged** assemblies. That is the state a plugin is in while somebody is writing
it: built, registered straight into a development environment with the Plugin Registration
Tool or a build step, source open in the editor. It is what this tool exists for, so it is
what opens.

Managed means the assembly arrived inside a solution — it was *shipped*, by Microsoft, by
an ISV, or by you in a build that is no longer the one on disk. Its source is not the tree
you are about to write to.

## The two switches

Each carries a count of what it is holding back, so the list is never quietly short:

```
[Load Assemblies]  ☐ Microsoft's (65)  ☐ Managed (3)
```

**Microsoft's** — the first party assemblies. They are told apart by their **strong name
signature**, not their name: plugin assemblies must be signed, `31bf3856ad364e35` is a key
nobody outside Microsoft can sign with, and it covers Power Pages, Field Service and the
rest of the optional apps whatever they call themselves and whichever of Microsoft's
several publishers shipped them. Assembly names were tried first and were not good enough:
first party apps ship under their own names, and an assembly of yours called
`Microsoft.Contoso.Extensions` is not Microsoft's.

**Managed** — everything else that came in a solution. An ISV's app, or your own, shipped.

The two govern **separate sets** rather than stacking. Every one of Microsoft's assemblies
is managed too, and if both tests applied to the same row then ticking **Microsoft's**
while **Managed** was off would show nothing at all and the switch would look broken. So a
row that is Microsoft's is counted and shown by the Microsoft switch alone, and the Managed
count is everything managed that is *not* Microsoft's.

Documenting a managed assembly is a real thing to want — the only environment you can reach
is not always the one you develop in — which is why the switch exists rather than the rows
simply being gone. It is the exception, though, so it starts off.

## The filter box

No signature test settles every environment. An ISV's app is neither Microsoft's nor yours,
and nothing on the record says which is which. Typing your own name in the **Filter** box
is the answer that never needs one, and it composes with the switches rather than
overriding them: filter for `Contoso` with **Managed** on and you get the Contoso
assemblies that shipped as well as the ones that did not.

Filtering only hides rows. **It never unticks one**, because a filter is typed a letter at
a time and losing a selection to a keystroke would be unforgivable. So you can narrow the
list, tick **All**, clear the box, narrow it again, and tick more. Anything ticked but out
of sight is counted on the status line:

```
3 assemblies · 12 of 12 classes · 1 out of view
```

The same is true of the switches: turning **Managed** back off hides rows you have ticked
rather than unticking them, and their classes stay in the class list under their own
heading.

The **All** box speaks for the rows on screen, not for the environment, so filtering to
your own name and hitting **All** means all of yours. When some but not all of the visible
rows are ticked it goes indeterminate rather than checked.

## The Source column

The assembly's share of what the source folder scan found, as `matched/total` and the worst
thing among them — `8/8`, `7/8 · 1 not found`, `6/8 · 2 ambiguous`, `8/8 · 1 stale`. Green
means there is nothing to say, amber that something needs a decision, red that a class has
no file at all. Hovering the row gives the whole breakdown rather than the worst of it.

Three of its states are empty rather than a count, and the row's tooltip says which:

| Cell | Means |
|---|---|
| *(blank)* | Not ticked yet, or no source folder chosen |
| `…` | Its classes are still being read |
| `—` | Registered, but no step is registered against anything in it |

## Isolation

There is no column for it. Dataverse online forces sandbox isolation on everything that is
not Microsoft's, so the column said `Sandbox` down every row you were shown while the
exceptions sat behind the **Microsoft's** switch. An assembly registered in full trust —
which happens on-premises — says so in its tooltip instead.

## What is remembered

The ticks on both lists and the source folder, per environment. The first **Load
Assemblies** after opening the tool puts them back: the assemblies you had ticked are ticked
again, the classes you had unticked stay unticked, and the folder box is filled in unless
you have already typed one. An assembly that has gone from the environment since is simply
not ticked back.

Only that first load restores. Pressing **Load Assemblies** again in the same session starts
over, as it always has, and **Refresh** keeps whatever you have. The memory is written as
you go rather than when the tab closes, so a crash costs nothing.

Anything on either list that was not there the last time you had the environment open is
**marked with a small green sparkle** after its name, with the reason in the row's tooltip
and a count on the status line:

```
3 assemblies · 6 of 6 classes · 1 new
```

For assemblies that means any row the environment lists now and did not list then. For
classes it means one you had not seen, in an assembly whose classes you had looked at
before — or in an assembly that is itself new. Ticking an assembly for the first time does
not mark its whole group; every class in it is unseen, and marking them all would say
nothing.

The count is of rows on screen, so it always matches what a scroll would find. A new
assembly behind the **Managed** switch is counted once the switch is on, and not before.
The marks stay for the session, and are gone the next time you open the environment.

One file per environment, beside the tool's other settings in XrmToolBox's settings folder,
named `PluginStepCodegen_memory-<environment>.xml`. Delete it to forget an environment.

## What is never listed

Assemblies flagged `ishidden` — internal plumbing the platform registers for itself. Steps
on those are not yours to document and there is no source anywhere to write them into.
