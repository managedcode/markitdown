---
name: profile
description: Profile .NET applications for CPU performance, memory allocations, lock contention, exceptions, heap analysis, and JIT inlining. Use when the user asks about performance bottlenecks, memory leaks, high CPU, slow code, lock contention, excessive exceptions, GC pressure, heap growth, or JIT compilation in .NET projects.
argument-hint: "[--cpu|--memory|--contention|--exception|--heap] [path]"
---

## Prerequisites

This tool requires .NET 10+ SDK (for `dnx` support).

Check if `dnx` is available:
```
dnx --help
```

The profiler also depends on `dotnet-trace` and `dotnet-gcdump`. Install them if missing:
```
dotnet tool install -g dotnet-trace
dotnet tool install -g dotnet-gcdump
```

## About Asynkron.Profiler

A CLI profiler for .NET that outputs structured text — no GUI needed. Designed for both human inspection and AI-assisted analysis. It wraps `dotnet-trace` and `dotnet-gcdump` and presents call trees, hot functions, allocation tables, and contention rankings as plain text.

Results are written to `profile-output/` in the current directory.

## Running via dnx (no install needed)

```
dnx asynkron-profiler [flags] -- [target]
```

On first run, `dnx` will prompt to download the package.

## Profiling Modes

### CPU Profiling (`--cpu`)
Sampled CPU profiling. Shows call trees and hot function tables.
```
dnx asynkron-profiler --cpu -- ./MyApp.csproj
dnx asynkron-profiler --cpu -- ./bin/Release/net10.0/MyApp
```

### Memory Allocation Profiling (`--memory`)
Tracks GC allocation tick events. Shows per-type allocation call trees and allocation sources.
```
dnx asynkron-profiler --memory -- ./MyApp.csproj
```

### Lock Contention Profiling (`--contention`)
Shows wait-time call trees and contended method rankings. Use for diagnosing lock congestion and thread starvation.
```
dnx asynkron-profiler --contention -- ./MyApp.csproj
```

### Exception Profiling (`--exception`)
Counts thrown exceptions, shows throw-site call trees. Filter by type with `--exception-type`.
```
dnx asynkron-profiler --exception -- ./MyApp.csproj
dnx asynkron-profiler --exception --exception-type InvalidOperationException -- ./MyApp.csproj
```

### Heap Snapshot (`--heap`)
Takes a GC heap snapshot using `dotnet-gcdump`. Shows retained objects by type and size.
```
dnx asynkron-profiler --heap -- ./MyApp.csproj
```

### JIT / Inlining Analysis
The profiler can capture JIT-to-native compilation events, showing what methods get JIT compiled and which calls get inlined. This is useful for understanding runtime code generation and verifying that hot paths are being optimized by the JIT.

## Analyzing Existing Traces

You can analyze previously captured trace files without re-running the app:
```
dnx asynkron-profiler --input /path/to/trace.nettrace
dnx asynkron-profiler --input /path/to/trace.speedscope.json --cpu
dnx asynkron-profiler --input /path/to/heap.gcdump --heap
```

## Key Flags

| Flag | Purpose |
|------|---------|
| `--cpu` | CPU profiling |
| `--memory` | Memory allocation profiling |
| `--contention` | Lock contention analysis |
| `--exception` | Exception profiling |
| `--heap` | Heap snapshot |
| `--root <text>` | Root call tree at first matching method |
| `--filter <text>` | Filter function tables by substring |
| `--exception-type <text>` | Filter exceptions by type name |
| `--calltree-depth <n>` | Max call tree depth (default: 30) |
| `--calltree-width <n>` | Max children per node (default: 4) |
| `--calltree-self` | Include self-time tree |
| `--calltree-sibling-cutoff <n>` | Hide siblings below X% (default: 5) |
| `--include-runtime` | Include runtime/framework frames |
| `--input <path>` | Analyze existing trace file |
| `--tfm <tfm>` | Target framework for .csproj/.sln |

## Supported Input Formats

| Mode | Formats |
|------|---------|
| CPU | `.speedscope.json`, `.nettrace` |
| Memory | `.nettrace`, `.etlx` |
| Exceptions | `.nettrace`, `.etlx` |
| Contention | `.nettrace`, `.etlx` |
| Heap | `.gcdump` |

---

## Profiling Methodology

Profiling is iterative. Follow this workflow to systematically identify and eliminate bottlenecks.

### Step 1: Build Release First

Always build Release before profiling. Debug builds have disabled optimizations, extra checks, and no inlining — profiling them gives misleading results.

```
dotnet build -c Release
```

### Step 2: Choose the Right Mode

| Symptom | Start with |
|---------|------------|
| High CPU / slow execution | `--cpu` |
| High memory / GC pressure | `--memory` |
| High latency but low CPU | `--contention` |
| Too many exceptions in logs | `--exception` |
| Memory keeps growing (leak) | `--heap` |
| Want to verify JIT optimization | JIT/inlining analysis |

### Step 3: Read the Hot Function Table

The profiler outputs a hot function table showing where time or allocations are concentrated:

```
=== HOT FUNCTIONS ===
   Time (ms)      Calls  Function
-------------------------------------------------
    38805.39      19533  MyApp.Core.ProcessItem...
    19769.23       9897  MyApp.Core.TransformData...
```

Focus on the top 3-5 entries. These are your optimization targets.

### Step 4: Read the Allocation Call Graph

For memory profiling, the allocation call graph shows *where* allocations originate:

```
CreateEnvironment
  Calls: 1048
  Allocated by:
    <- ProcessLoop (1048x, 100%)
         <- RunMain (4x)
```

This traces allocations back to their source — the method that triggered them, not just where `new` was called.

### Step 5: Iterate

1. Profile to find the top bottleneck
2. Fix it (optimize hot path, reduce allocations, remove contention)
3. Profile again to measure improvement
4. Repeat until performance targets are met

Track progress across rounds:
```
Round 1: 322 MB, 172 ms
Round 2: 173 MB, 150 ms  (pooling)
Round 3: 107 MB, 116 ms  (fast paths)
```

### Step 6: Use Manual Tracing for Deep Dives

When the profiler summary isn't enough, capture a detailed trace for manual analysis:

```bash
# Capture detailed GC trace
dotnet-trace collect \
  --profile gc-verbose \
  --format NetTrace \
  -o trace.nettrace \
  -- dotnet run -c Release --project ./MyApp

# Analyze with the profiler
dnx asynkron-profiler --input trace.nettrace --memory

# Or convert for external tools
dotnet-trace convert trace.nettrace --format Speedscope
```

### Common Optimization Patterns

**Reduce allocations in hot loops:**
- Use object pooling for frequently created/disposed objects
- Use `Span<T>` / `stackalloc` for short-lived buffers
- Avoid boxing value types (use generic overloads)

**Reduce CPU in hot paths:**
- Use `[MethodImpl(MethodImplOptions.AggressiveInlining)]` on small hot methods
- Split into fast path (inlined, common case) and slow path (`NoInlining`, rare case)
- Cache computed values instead of recomputing

**Reduce contention:**
- Use lock-free patterns (`Interlocked`, `ConcurrentDictionary`)
- Reduce lock scope — hold locks for the shortest time possible
- Use `ReaderWriterLockSlim` for read-heavy workloads

**Reduce exceptions:**
- Use `TryParse` / `TryGet` patterns instead of catching exceptions
- Exceptions are expensive — never use them for control flow

---

## JIT-Aware Optimization Patterns

These patterns work *with* the .NET JIT compiler to produce faster native code. Use the profiler's JIT/inlining analysis to verify these optimizations take effect.

### Fast/Slow Path Split

The most impactful pattern for hot methods. The JIT inlines small methods into their callers, eliminating call overhead and enabling further optimizations. But it won't inline large methods. The trick: keep the common case tiny and inlineable, push the rare case into a separate non-inlined method.

```csharp
[MethodImpl(MethodImplOptions.AggressiveInlining)]
private static Result HandleHotPath(Data data)
{
    // Fast path: ~20-30 lines max, handles the common case
    if (data.IsSimpleCase)
    {
        // Direct, minimal work
        return Result.From(data.Value);
    }

    // Rare/complex case — delegate to non-inlined method
    return HandleHotPathSlow(data);
}

[MethodImpl(MethodImplOptions.NoInlining)]
private static Result HandleHotPathSlow(Data data)
{
    // Complex logic: type coercion, error handling, edge cases
    // This can be as large as needed — it won't bloat the call site
}
```

**Why this works:**
- The JIT inlines the fast path directly into the hot loop
- The slow path stays as a regular call — it doesn't bloat the inlined code
- CPU branch prediction favors the fast path since it's the common case
- Instruction cache stays hot because the loop body is small

**When to apply:**
- Method shows up in profiler hot function table
- Method has a common fast case and rare complex case
- The fast case is under ~30 lines
- The method is called in a tight loop

**How to verify:** Use the profiler's JIT/inlining analysis to confirm the fast path is being inlined at the call site.

### Dispatch Tables vs Switch Statements

For hot dispatch (e.g., instruction interpreters, message handlers, event processors), a delegate array indexed by enum is faster than a switch:

```csharp
// Define handler signature
delegate Result Handler(Context ctx, Instruction instr);

// Build dispatch table once (static constructor)
private static readonly Handler[] _dispatch = new Handler[64];

static MyRunner()
{
    _dispatch[(int)Kind.Add] = HandleAdd;
    _dispatch[(int)Kind.Call] = HandleCall;
    _dispatch[(int)Kind.Branch] = HandleBranch;
    // ...
}

// Hot loop — direct delegate invocation, no switch overhead
while (running)
{
    var instr = instructions[pc];
    var result = _dispatch[(int)instr.Kind](ctx, instr);
    // ...
}
```

**Why this works:**
- Direct indexed lookup — O(1), no comparisons
- Each handler can have its own inlining attributes
- Scales better than switch as the number of cases grows

**When to apply:**
- Dispatch over an enum with 10+ cases
- Called in a tight loop (interpreter, event loop, message pump)
- Switch shows up in profiler as a hot function

### Object Pooling for Hot Allocations

When the profiler shows a type being allocated millions of times in a loop, pool it instead.

```csharp
// 1. Define what poolable objects look like
interface IRentable
{
    void Activate();  // Called when rented — initialize state
    void Reset();     // Called when returned — clear state for reuse
}

// 2. Lock-free pool using Interlocked.CompareExchange
class ObjectPool<T> where T : class, IRentable
{
    private readonly T?[] _items;
    private readonly Func<T> _factory;

    public T Rent()
    {
        for (int i = 0; i < _items.Length; i++)
        {
            var item = Interlocked.Exchange(ref _items[i], null);
            if (item is not null) { item.Activate(); return item; }
        }
        var created = _factory();
        created.Activate();
        return created;
    }

    public void Return(T item)
    {
        item.Reset();
        for (int i = 0; i < _items.Length; i++)
        {
            if (Interlocked.CompareExchange(ref _items[i], item, null) == null)
                return;
        }
        // Pool full — abandon to GC (graceful degradation)
    }
}

// 3. RAII wrapper ensures objects are returned
using var handle = pool.Rent();
var obj = handle.Value;
// ... use obj ...
// Automatically returned on dispose
```

**Impact:** A tight loop creating 1M scoped objects goes from 1M allocations to ~32 (pool size). Dramatically reduces GC pressure.

**When to apply:**
- A type shows up in the memory profiler's top allocations
- It's created and disposed in a tight loop
- Its lifetime is short and predictable
- The type can be cleanly reset for reuse

### Thread-Safe Lazy Initialization

For cached computed values that are expensive to create but read frequently:

```csharp
static TCache GetOrCreate<TCache>(ref TCache? field, Func<TCache> factory)
    where TCache : class
{
    var existing = Volatile.Read(ref field);
    if (existing is not null) return existing;

    var created = factory();
    var prior = Interlocked.CompareExchange(ref field, created, null);
    return prior ?? created;
}
```

**Why not `Lazy<T>`:** This pattern avoids the `Lazy<T>` allocation itself, and the `Volatile.Read` fast path is a single instruction on x86/ARM. The worst case (two threads create simultaneously) wastes one creation but is still correct — no locks needed.

---

## Guidelines

- Always build Release first (`dotnet build -c Release`) before profiling — profiling Debug builds gives misleading results
- Profile the compiled binary directly rather than via `dotnet run` for accurate measurements
- Use `--root` to focus on a specific call path when the tree is too broad
- Use `--filter` to narrow function tables to your own code, excluding framework noise
- Use `--calltree-sibling-cutoff` to hide insignificant branches
- For memory issues, start with `--memory` to find allocation sources, then `--heap` for retained object analysis
- For performance, start with `--cpu`, then drill into contention if CPU usage is low but latency is high
- For exception-heavy apps, use `--exception` with `--exception-type` to focus on specific exception categories
- Profile iteratively — fix one bottleneck at a time and measure the improvement before moving on
