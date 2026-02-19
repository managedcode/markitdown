---
name: dotnet-strict
description: Apply strict .NET/C# coding standards to a project or solution. Adds Roslynator and Meziantou analyzers, a comprehensive .editorconfig with 80+ diagnostic rules, naming conventions, and performance warnings. Use when the user wants to enforce strict code quality, set up analyzers, or add an .editorconfig to a .NET project.
disable-model-invocation: true
argument-hint: "[solution or project path]"
---

## What This Does

Applies a strict, production-grade coding standard to a .NET project by:

1. Adding analyzer NuGet packages to the project(s)
2. Creating a comprehensive `.editorconfig` at the solution root
3. Verifying the setup compiles

## Step 1: Determine Target

If `$ARGUMENTS` is provided, use it as the solution/project path. Otherwise, look for a `.sln` or `.csproj` in the current directory.

Identify all `.csproj` files that should get analyzers (typically `src/` projects, not test projects).

## Step 2: Add Analyzer Packages

Add these analyzer packages to each source project (not test projects):

```bash
dotnet add <project.csproj> package Roslynator.Analyzers
dotnet add <project.csproj> package Meziantou.Analyzer
```

These should be added as development-only dependencies. After adding, verify the package references include `PrivateAssets="all"` so they don't flow to consumers:

```xml
<PackageReference Include="Roslynator.Analyzers" Version="*">
  <PrivateAssets>all</PrivateAssets>
  <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
</PackageReference>
<PackageReference Include="Meziantou.Analyzer" Version="*">
  <PrivateAssets>all</PrivateAssets>
  <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
</PackageReference>
```

## Step 3: Create .editorconfig

Create a `.editorconfig` file at the solution root (next to the `.sln` file, or at the repo root). If one already exists, merge the rules — do not overwrite existing customizations.

The .editorconfig should contain the following rules:

```ini
; Strict .NET coding standards
; Based on ASYNKRON production configuration
root = true

[*]
indent_style = space
charset = utf-8
trim_trailing_whitespace = true
insert_final_newline = true

[*.cs]
indent_size = 4
dotnet_sort_system_directives_first = true

; --- Code Style ---

; Don't use 'this.' qualifier
dotnet_style_qualification_for_field = false:suggestion
dotnet_style_qualification_for_property = false:suggestion

; Use int over Int32
dotnet_style_predefined_type_for_locals_parameters_members = true:warning
dotnet_style_predefined_type_for_member_access = true:warning

; Require var
csharp_style_var_for_built_in_types = true:warning
csharp_style_var_when_type_is_apparent = true:warning
csharp_style_var_elsewhere = true:suggestion

; Disallow throw expressions
csharp_style_throw_expression = false:suggestion

; Braces on new lines (Allman style)
csharp_new_line_before_open_brace = all
csharp_new_line_before_else = true
csharp_new_line_before_catch = true
csharp_new_line_before_finally = true
csharp_new_line_before_members_in_object_initializers = true
csharp_new_line_before_members_in_anonymous_types = true

; File-scoped namespaces
csharp_style_namespace_declarations = file_scoped

; Always use braces
csharp_prefer_braces = true

; Modifier order
csharp_preferred_modifier_order = public, private, protected, internal, static, extern, new, virtual, abstract, sealed, override, readonly, unsafe, volatile, async:suggestion

; No multiple blank lines
dotnet_style_allow_multiple_blank_lines_experimental = false
dotnet_diagnostic.IDE2000.severity = warning

; Unused parameters (non-public only)
dotnet_code_quality_unused_parameters = non_public

; --- Naming Conventions ---

; Constants: PascalCase
dotnet_naming_rule.constant_fields_should_be_pascal_case.severity = suggestion
dotnet_naming_rule.constant_fields_should_be_pascal_case.symbols = constant_fields
dotnet_naming_rule.constant_fields_should_be_pascal_case.style = pascal_case_style
dotnet_naming_symbols.constant_fields.applicable_kinds = field
dotnet_naming_symbols.constant_fields.required_modifiers = const
dotnet_naming_style.pascal_case_style.capitalization = pascal_case

; Static fields: PascalCase
dotnet_naming_rule.pascal_case_for_static_fields.severity = suggestion
dotnet_naming_rule.pascal_case_for_static_fields.symbols = static_fields
dotnet_naming_rule.pascal_case_for_static_fields.style = pascal_case_style
dotnet_naming_rule.pascal_case_for_static_fields.priority = 1
dotnet_naming_symbols.static_fields.applicable_kinds = field
dotnet_naming_symbols.static_fields.applicable_accessibilities = public, private, internal
dotnet_naming_symbols.static_fields.required_modifiers = static

; Private/internal fields: _camelCase
dotnet_naming_rule.camel_case_for_private_internal_fields.severity = suggestion
dotnet_naming_rule.camel_case_for_private_internal_fields.symbols = private_internal_fields
dotnet_naming_rule.camel_case_for_private_internal_fields.style = camel_case_underscore_style
dotnet_naming_rule.camel_case_for_private_internal_fields.priority = 3
dotnet_naming_symbols.private_internal_fields.applicable_kinds = field
dotnet_naming_symbols.private_internal_fields.applicable_accessibilities = private, internal
dotnet_naming_style.camel_case_underscore_style.required_prefix = _
dotnet_naming_style.camel_case_underscore_style.capitalization = camel_case

; --- Roslynator Rules ---

; Resource can be disposed asynchronously
dotnet_diagnostic.RCS1261.severity = warning
; File contains no code
dotnet_diagnostic.RCS1093.severity = error
; Implement exception constructors (noisy — disabled)
dotnet_diagnostic.RCS1194.severity = none
; Add parentheses when necessary (noisy — disabled)
dotnet_diagnostic.RCS1123.severity = none
; Unused parameter
dotnet_diagnostic.RCS1163.severity = warning
; Unused type parameter
dotnet_diagnostic.RCS1164.severity = warning
; Unused 'this' parameter
dotnet_diagnostic.RCS1175.severity = warning
; Remove unused member declaration
dotnet_diagnostic.RCS1213.severity = warning

; --- Meziantou Rules ---

; Use explicit enum value instead of 0
dotnet_diagnostic.MA0099.severity = warning
; Local variables should not hide other symbols
dotnet_diagnostic.MA0084.severity = warning
; Specify the parameter name in ArgumentException
dotnet_diagnostic.MA0015.severity = warning
; Method is too long
dotnet_diagnostic.MA0051.severity = warning
; Use String.Equals instead of equality operator
dotnet_diagnostic.MA0006.severity = warning
; Avoid implicit culture-sensitive methods
dotnet_diagnostic.MA0074.severity = warning
; IEqualityComparer<string> or IComparer<string> is missing
dotnet_diagnostic.MA0002.severity = warning

; --- IDE Rules ---

; Remove unnecessary usings
dotnet_diagnostic.IDE0005.severity = none
; Add braces
dotnet_diagnostic.IDE0011.severity = warning
; Use pattern matching
dotnet_diagnostic.IDE0020.severity = warning
; Use coalesce expression (non-nullable)
dotnet_diagnostic.IDE0029.severity = warning
; Use coalesce expression (nullable)
dotnet_diagnostic.IDE0030.severity = warning
; Use null propagation
dotnet_diagnostic.IDE0031.severity = warning
; Remove unreachable code
dotnet_diagnostic.IDE0035.severity = warning
; Order modifiers
dotnet_diagnostic.IDE0036.severity = warning
; Use pattern matching
dotnet_diagnostic.IDE0038.severity = warning
; Format string contains invalid placeholder
dotnet_diagnostic.IDE0043.severity = warning
; Make field readonly
dotnet_diagnostic.IDE0044.severity = warning
; Remove unused private members
dotnet_diagnostic.IDE0051.severity = warning
; Fix formatting
dotnet_diagnostic.IDE0055.severity = suggestion
; Unnecessary assignment
dotnet_diagnostic.IDE0059.severity = warning
; Make local function static
dotnet_diagnostic.IDE0062.severity = warning
; Convert to file-scoped namespace
dotnet_diagnostic.IDE0161.severity = warning
; Remove unnecessary lambda expression
dotnet_diagnostic.IDE0200.severity = warning

; --- Compiler Warnings ---

; Unreachable code detected
dotnet_diagnostic.CS0162.severity = warning
; Field is never used
dotnet_diagnostic.CS0169.severity = warning

; --- Microsoft Code Analysis (CA) Rules ---

; Performance
dotnet_diagnostic.CA1822.severity = warning
dotnet_code_quality.CA1822.api_surface = private, internal
dotnet_diagnostic.CA1825.severity = warning
dotnet_diagnostic.CA1826.severity = warning
dotnet_diagnostic.CA1827.severity = warning
dotnet_diagnostic.CA1828.severity = warning
dotnet_diagnostic.CA1829.severity = warning
dotnet_diagnostic.CA1830.severity = warning
dotnet_diagnostic.CA1831.severity = warning
dotnet_diagnostic.CA1832.severity = warning
dotnet_diagnostic.CA1833.severity = warning
dotnet_diagnostic.CA1834.severity = warning
dotnet_diagnostic.CA1835.severity = warning
dotnet_diagnostic.CA1836.severity = warning
dotnet_diagnostic.CA1837.severity = warning
dotnet_diagnostic.CA1838.severity = warning
dotnet_diagnostic.CA1839.severity = warning
dotnet_diagnostic.CA1840.severity = warning
dotnet_diagnostic.CA1841.severity = warning
dotnet_diagnostic.CA1842.severity = warning
dotnet_diagnostic.CA1843.severity = warning
dotnet_diagnostic.CA1844.severity = warning
dotnet_diagnostic.CA1845.severity = warning
dotnet_diagnostic.CA1846.severity = warning
dotnet_diagnostic.CA1847.severity = warning
dotnet_diagnostic.CA1852.severity = warning
dotnet_diagnostic.CA1854.severity = warning
dotnet_diagnostic.CA1855.severity = warning
dotnet_diagnostic.CA1856.severity = error
dotnet_diagnostic.CA1857.severity = warning
dotnet_diagnostic.CA1858.severity = warning

; Unused code
dotnet_diagnostic.CA1801.severity = warning
dotnet_diagnostic.CA1811.severity = warning
dotnet_diagnostic.CA1823.severity = warning
dotnet_diagnostic.CA1802.severity = warning
dotnet_diagnostic.CA1805.severity = warning
dotnet_diagnostic.CA1810.severity = warning
dotnet_diagnostic.CA1821.severity = warning

; Correctness
dotnet_diagnostic.CA1018.severity = warning
dotnet_diagnostic.CA1047.severity = warning
dotnet_diagnostic.CA1305.severity = warning
dotnet_diagnostic.CA1507.severity = warning
dotnet_diagnostic.CA1510.severity = warning
dotnet_diagnostic.CA1511.severity = warning
dotnet_diagnostic.CA1512.severity = warning
dotnet_diagnostic.CA1513.severity = warning
dotnet_diagnostic.CA1725.severity = suggestion
dotnet_diagnostic.CA2008.severity = warning
dotnet_diagnostic.CA2009.severity = warning
dotnet_diagnostic.CA2011.severity = warning
dotnet_diagnostic.CA2012.severity = warning
dotnet_diagnostic.CA2013.severity = warning
dotnet_diagnostic.CA2014.severity = warning
dotnet_diagnostic.CA2016.severity = warning
dotnet_diagnostic.CA2022.severity = warning
dotnet_diagnostic.CA2200.severity = warning
dotnet_diagnostic.CA2201.severity = warning
dotnet_diagnostic.CA2208.severity = warning
dotnet_diagnostic.CA2245.severity = warning
dotnet_diagnostic.CA2246.severity = warning
dotnet_diagnostic.CA2249.severity = warning

[*.{xml,config,*proj,nuspec,props,resx,targets,yml,tasks}]
indent_size = 2

[*.json]
indent_size = 2

[*.sh]
indent_size = 4
end_of_line = lf

[tests/**/*.cs]
; Allow parameter capture in test constructors
dotnet_diagnostic.CS9107.severity = silent
```

## Step 4: Build and Verify

```bash
dotnet build <solution-or-project>
```

The first build after adding analyzers will likely produce many warnings. This is expected — it shows the analyzers are working.

Report a summary of the warnings found, grouped by category:
- How many performance warnings (CA18xx)
- How many unused code warnings (RCS1163, RCS1213, CA1811, etc.)
- How many style warnings (IDE0xxx)
- How many Meziantou warnings (MA0xxx)

## Step 5: Optional — Auto-fix

Suggest running roslynator to auto-fix what it can:

```bash
roslynator fix <solution-or-project>
```

## What the Rules Cover

**Performance (CA18xx):** Use Span/Memory over substring, prefer Count property over Count(), use char overloads, seal internal types, prefer TryGetValue, avoid zero-length arrays.

**Dead Code (RCS1163, RCS1213, CA1811, IDE0051):** Unused parameters, unused type parameters, unused members, uncalled private code.

**Correctness (CA2xxx):** Rethrow to preserve stack, don't raise reserved exceptions, forward CancellationToken, avoid infinite recursion, use ValueTask correctly.

**Culture Safety (MA0074, MA0006, CA1305):** Avoid implicit culture-sensitive methods, use String.Equals, specify IFormatProvider.

**Modern C# Style:** File-scoped namespaces, pattern matching, null propagation, coalesce expressions, var everywhere, braces always.

**Naming:** Constants PascalCase, static fields PascalCase, private fields _camelCase.
