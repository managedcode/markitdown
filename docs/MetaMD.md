# MetaMD (MMD)

MetaMD is a Markdown profile that layers structured metadata and citation-aware rendering on top of CommonMark. Files typically use the `.metamd` extension (optionally `.metamd.md`) and begin with a JSON front matter block delimited by `+++` fences.

## Front Matter Schema

```json
{
  "title": "Document title",
  "abstract": "Optional abstract text.",
  "contributors": ["Name", "Name"],
  "affiliations": ["Organisation"],
  "keywords": ["term", "term"],
  "references": [
    {
      "id": "unique-id",
      "title": "Reference title",
      "authors": ["Author"],
      "url": "https://example.com/reference"
    }
  ]
}
```

All properties are optional. Unknown properties are ignored by the converter.

## Reference Syntax

Inline citations use `[@id]`. During conversion each citation is replaced with a Markdown link if a URL is present, or bold text when the reference has no URL. Referenced entries are collected and emitted in a `## References` section at the end of the document, preserving author lists and links.

## Diagram Blocks

MetaMD supports lightweight diagram embedding via custom blocks:

```
:::diagram type="mermaid"
<diagram body>
:::
```

The converter rewrites these blocks as fenced code blocks using the requested diagram type (e.g., `mermaid`, `dot`, `plantuml`).

## Compatibility

Because MetaMD is a superset of Markdown, downstream tools that do not recognise the front matter or diagram directives still render the body content. The .NET converter automatically recognises `.metamd` and `.metamd.md` files, extracts metadata into headings, and normalises references for consistent Markdown output.
