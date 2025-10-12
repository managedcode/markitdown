#set page(width: 140mm, height: 200mm)
= Helios Navigation Overview (Typst)

#let assets = (
  "AsciiDoc": "celestial-navigation-notes.adoc",
  "Telemetry": "telemetry-events.jsonl",
  "Flowchart": "mission-flowchart.mermaid",
  "Bibliography": "orbital-research.bib"
)

#table(
  columns: 2,
  [Asset], [File],
  ..for (name, path) in assets { [*#name*], [`#path`] }
)

#section("Process Summary")
The maneuver plan references the plain LaTeX derivation in `navigation-theory.tex` and the operational procedure manual `mission-operations.creole`. Each approved burn results in a summary entry within `mission-summary.metamd` and the wiki (`mission-wiki.wiki`).

#section("Dependencies")
- Resource ledger: `resource-allocation.tsv`
- Communication diagrams: `mission-network.dot`, `mission-network.gv`
- Training link: metadata stored in `youtube-solid-principles.json`

== Data Flow
```
nav -> telemetry -> archive -> knowledge-base
```
