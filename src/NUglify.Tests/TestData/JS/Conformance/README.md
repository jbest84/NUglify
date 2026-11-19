# JavaScript conformance tests

This corpus extends NUglify's NUnit tests with pinned upstream syntax fixtures,
portable engine regressions, reviewed output expectations, and context/newline
matrices. CI remains .NET-only: JavaScript is input text, with no Node, browser,
JavaScript engine, extra parser, or upstream download required.

## Running and current results

From the repository root, the ordinary command includes all conformance tests:

```powershell
dotnet test
```

To run only the conformance category:

```powershell
dotnet test src/NUglify.Tests/NUglify.Tests.csproj -c Release --filter TestCategory=JavaScriptConformance
```

| Suite | NUnit executions | Last verified failures |
| --- | ---: | ---: |
| Test262, golden outputs, and harness controls | 404 | 0 |
| Parser-only pairs and adapted V8/SpiderMonkey cases | 84 | 0 |
| Context and newline matrices | 164 | 0 |
| Generated grammar properties (1,200 sample runs) | 6 | 0 |
| Total | 658 | 0 |

The last complete Debug and Release runs each reported 1,831 passed, zero failed,
and two existing skips (`Bug78`, `Collision`), with exit code 0. Both builds had
zero warnings and errors. These recorded results do not establish complete
ECMAScript conformance.

Every mismatch fails its NUnit test and makes `dotnet test` return a nonzero
exit code. There are no known-failure baselines suppressing failures. Multiple
checks can expose the same bug; source files, expanded cases, and NUnit
executions are different counts.

## Harness and assertions

The implementation lives in [JavaScript/Conformance](../../../JavaScript/Conformance).
The existing project copies `TestData` to its output directory and needs no
additional CI setup. Existing regression and source-map tests retain their own
precise assertions.

| Check | Required result |
| --- | --- |
| Valid input | The complete input parses without error diagnostics. |
| Invalid syntax or early error | Error diagnostics are reported; warnings alone do not qualify. |
| Output syntax | Output from valid input reparses in the corresponding script/module mode and strictness. |
| Golden output | Output matches a reviewed expectation for the named settings profile. |
| Original/explicit source pairs | Each form matches its reviewed output expectation with optimization disabled. |

Each invocation uses fresh parser/settings state. The profiles are
`NoOptimization` (`MinifyCode = false`) and `Minify`. Both disable debug stripping,
preprocessor defines, conditional compilation, and embedded ASP.NET extensions.
Both run context validation on the original resolved tree before optimization.
Input strictness comes from source directives and source mode; strict-variant
output is reparsed with strict mode enabled. Disabling optimization still allows
scope resolution and context validation.

Invalid input is never printed or executed. Rejection of valid input fails at
that check and prevents output validation. Exceptions and Debug assertions are
failures, not syntax rejections. Messages identify the case, variant/profile,
check, expected/actual outcome, and diagnostics. Upstream diagnostic wording need
not match NUglify's; focused regression tests can assert specific error codes.

An independent parser is optional: upstream classifications and golden outputs
supply explicit expectations. Output reparse can share the original parser's
mistakes and does not prove behavior preservation: `(o?.a).b` and `o?.a.b` both
parse yet behave differently. Golden checks cover grouping, arrow/IIFE boundaries,
template raw text, default-parameter side effects, and lexical scope. JavaScript
execution and general optimized-AST equivalence remain outside current coverage.

## Corpora and provenance

| Test definitions | Contents |
| --- | --- |
| [ConformanceCorpus.cs](../../../JavaScript/Conformance/ConformanceCorpus.cs) | Test262 paths, groups, syntax expectations, and strictness/module flags. |
| [BoundaryCorpus.cs](../../../JavaScript/Conformance/BoundaryCorpus.cs) | Parser-only directory discovery and a small engine-case table. |
| [GrammarMatrixTests.cs](../../../JavaScript/Conformance/GrammarMatrixTests.cs) | Context wrappers, validity tables, separators, and exact newline outputs. |
| [GeneratedGrammarTests.cs](../../../JavaScript/Conformance/GeneratedGrammarTests.cs) | Bounded, seeded FsCheck cases and focused structural assertions. |

| Upstream | Pinned commit |
| --- | --- |
| [tc39/test262](https://github.com/tc39/test262) | `2e58f87d8350ad51224617a041c6e041d52f8444` |
| [tc39/test262-parser-tests](https://github.com/tc39/test262-parser-tests) | `0e808c74fbec780646434cad17bb22dc52461003` |
| [v8/v8](https://github.com/v8/v8) | `6bdae82da05dc6b85541a9db412153842633d280` |
| [mozilla-firefox/firefox](https://github.com/mozilla-firefox/firefox) | `889724fd195b636bc6685a867c2bc1d34573293e` |

### Test262

The 77 tests expand into 141 source-goal/strictness variants and 282 profile
cases. Coverage includes arrows, optional chaining, templates, destructuring,
`for...of`, class static blocks, parameter defaults, `yield`, modules, and raw
source. This is a curated subset of Test262.

The C# case table retains the syntax-relevant metadata from the original
Test262 frontmatter. Imported files retain their complete frontmatter and notices.
No metadata parser, JSON manifest, or file-hash inventory is required at test time.

Ordinary inputs expand into non-strict and strict variants; strict variants get
the Test262 directive prefix. Honor `onlyStrict`, `noStrict`, `module`, and `raw`;
raw source remains unchanged. Scripts use `SourceMode.Program`, modules use
`SourceMode.Module` and are implicitly strict. `_FIXTURE` dependencies are
not listed as standalone cases: they support module resolution, which these
syntax-only tests do not perform.

Parse negatives require syntax/early errors. Runtime and resolution negatives
require valid top-level syntax; includes and runtime assertions are not executed.
Parsing an `eval` or `Function` string does not test the syntax inside it.
`ScriptVersion`'s ES5/ES6 values do not fully describe modern feature support,
so review features and exclusions explicitly.

### Parser-only pairs and engine adaptations

The boundary corpus contains 24 parser-only files: eight original/explicit
pairs, four syntax-invalid cases, and four early errors. It also contains 14
portable engine cases (ten V8 and four SpiderMonkey). Parser-only classifications
come from `pass`, `pass-explicit`, `fail`, and `early`; `.module.js` selects the
module goal.

The eight pairs have explicit output expectations in `BoundaryCorpusTests.cs`.
Both the original and fully parenthesized forms are checked with optimization
disabled, preserving constants, comma operands, grouping, and operator boundaries.
These are focused output regressions, not a general AST-equivalence comparison.
Both forms also retain syntax and output-reparse checks under both profiles.

Adaptations remove assertions and unrelated setup while retaining relevant syntax.
Their headers record the upstream path, revision, section, and extraction notes.
Licenses live under `Boundary/Upstream`; portable inputs live under
`Boundary/Adapted`. Original engine source files are identified by the paths and
pinned revisions in the adaptation headers rather than archived locally. The
V8 C++ cases originate from the
[pinned parser tests](https://github.com/v8/v8/blob/6bdae82da05dc6b85541a9db412153842633d280/test/unittests/parser/parsing-unittest.cc).
Tests read local adapted inputs and do not download or validate upstream artifacts.

[Boundary/ParserTests/licenses.md](Boundary/ParserTests/licenses.md) retains the
five applicable project notices and lists all 24 imported fixtures, including
explicit pairs. It links to the full upstream index and omits unrelated paths.
These per-fixture notices complement the Test262, V8, and SpiderMonkey licenses.

### Context and newline matrices

Four snippets (`return`, `yield`, `await`, `with`) run in seven contexts: script,
strict script, module, function, strict function, async function, and generator
function. Each has an explicit expectation; async/generator functions are not
assumed to be automatically strict.

Six newline rules run with LF, CRLF, CR, U+2028, U+2029, and four multiline-comment
variants. Valid cases assert exact output for both profiles, including automatic
semicolon insertion, arrow statement/call continuation, and prefix/postfix
separation. C# separator strings contain actual line terminators after escape processing.

### Generated grammar checks

`GeneratedGrammarTests.cs` uses the existing FsCheck dependency. Each of three
families runs 200 samples at each of two fixed replay seeds, `(104729, 20261118)`
and `(130363, 20261118)`: six NUnit tests, 1,200 sample runs, and 2,400 profile
checks per configuration. Inputs use bounded identifiers, trivia, and a depth
parameter of 0..3 (up to four inner functions). Every valid sample also emits and
reparses under both profiles; every invalid sample must produce error diagnostics.

| Family | Grammar forms | Valid samples | Invalid samples |
| --- | ---: | ---: | ---: |
| Operator precedence and associativity | 9 | 400 | 0 |
| Optional chains, nullish operators, targets, and templates | 16 | 242 | 158 |
| Restricted newlines and function contexts | 16 | 199 | 201 |

All 41 forms are exercised at both fixed seeds.

Run only these properties with:

```powershell
dotnet test src/NUglify.Tests/NUglify.Tests.csproj -c Release --filter TestCategory=JavaScriptGenerated
```

Shrinking preserves the grammar form and expected validity while reducing depth,
trivia, and identifier length. Failures save the seed, shrunk source, diagnostics,
and FsCheck result under the test output's `TestResults/Conformance/*.txt` and
attach the report to the NUnit result. CI uploads these reports on failure.

Replay with the same generator version and the seed from the report:

```powershell
$env:NUGLIFY_CONFORMANCE_SEED = '104729'
dotnet test src/NUglify.Tests/NUglify.Tests.csproj -c Release --filter FullyQualifiedName~GeneratedGrammarTests.OperatorPrecedenceAndAssociativity
Remove-Item Env:NUGLIFY_CONFORMANCE_SEED
```

Minimize any real parser failure into a permanent syntax or golden regression
before fixing it, retaining the replay seed in the regression's comment. Do not
suppress failing generated samples or change their expectations to make CI pass.

### Structural comparison and remaining coverage

The structural evaluation uses direct assertions for the generated productions:
operator/operand trees, exponent associativity, optional-chain termination, and
operand-free `return` after a newline. Ordinary grouping is ignored only within
the operator productions; optional-chain boundary grouping is asserted explicitly.
The checks run on original and reparsed output trees with optimization disabled.
They require no general AST projection, fingerprint format, or comparison helper
framework. Optimized trees are allowed to differ and retain syntax/golden checks.

The measured fixed-seed run has zero acceptance/rejection, emission/reparse, or
modeled-structure mismatches. Remaining coverage limits are:

- Generated contexts are scripts. Curated fixtures cover modules, but arbitrary
  module graphs, resolution, and generated module combinations are not exercised.
- Templates, regex/Unicode lexing, declarations, and scopes have selected
  regressions rather than exhaustive grammar generation. The 41 generated forms
  do not establish complete ECMAScript conformance.
- Parsing and output goldens do not execute code or prove arbitrary optimization
  semantics, runtime side effects, template cooked values, or dynamic `eval`.
  Source-map correctness remains in the existing dedicated tests.

An execution engine or independent managed parser could address some of these
limits, but adds dependencies and its own supported-syntax differences. Consider
additional validation when minimized bugs demonstrate a specific coverage gap.
CI remains .NET-only and requires no Node installation.

## Regression coverage

The corpus and focused regressions cover these parser and output boundaries:

| Area | Covered boundaries |
| --- | --- |
| Input and output | Hashbang anchoring; tagged/untagged template escapes; bare `yield` in substitutions; `new.target` optional access; exported-class boundaries; `for...of` member targets; nested-arrow regexp boundaries. |
| Grammar restrictions | Assignment/update/destructuring targets; optional-chain targets and tags; `for...of` initializers; nullish/logical mixtures; forbidden newlines before `=>` and after `throw`. |
| Context and strictness | Strict/non-simple parameters and identifiers; module declarations; `super`/`new.target` contexts; static-block restrictions; duplicate bindings; control flow; `await`, `yield`, `return`, and `with` contexts. |

Adjacent controls also cover directive printing, computed-method-key resolution,
legal loop-body shadowing, catch-binding cycles, and empty-export Debug assertions.
Document new gaps here with minimized regressions; every mismatch must continue
to fail CI.

## Updating the corpus

Updates are deliberate developer work; CI has no authoring or download step.

1. Choose an exact upstream commit, review source/license changes, and record the
   revision above. Copy original bytes, paths, frontmatter, and notices unchanged.
   Scoped `-text` attributes preserve imported line endings on Windows.
2. Add Test262 files to `ConformanceCorpus.Cases`. Review the original frontmatter
   for strictness/module/raw flags and negative phase. Only parse negatives expect
   rejection; runtime/resolution negatives still expect valid syntax. Leave
   `_FIXTURE` dependencies out of the case table.
3. Put parser-only inputs under `pass`, `pass-explicit`, `fail`, or `early`;
   those directories determine expectations. Import both members of valid pairs
   under matching filenames. A `.module.js` suffix selects module mode. Add
   reviewed original/explicit output expectations to `BoundaryCorpusTests.cs`.
4. Add portable engine inputs to `Boundary/Adapted` and the engine-case table in
   `BoundaryCorpus.cs`. Retain copyright/license notices and headers identifying
   the original path/revision and removed assertions, flags, setup, or wrappers.
   Extract syntax inside `eval` strings explicitly if it is to be tested.
5. Edit context/newline cases and exact outputs in `GrammarMatrixTests.cs`.
   Keep an explicit outcome for every context and preserve actual line terminators
   in separator strings. Review output expectations for operator precedence,
   grouping, evaluation order, and call boundaries.
6. Preserve applicable licenses. Update the parser license index's fixture paths
   when its subset changes, retaining each complete copyright/license notice.
7. Review staged and unstaged source/test changes together, plus untracked files:

   ```powershell
   git status --short
   git diff --stat HEAD
   git diff HEAD -- src/NUglify.Tests/JavaScript/Conformance src/NUglify.Tests/TestData/JS/Conformance
   ```

8. Run the category and full suite in both configurations:

   ```powershell
   dotnet test src/NUglify.Tests/NUglify.Tests.csproj -c Debug --filter TestCategory=JavaScriptConformance
   dotnet test src/NUglify.Tests/NUglify.Tests.csproj -c Release --filter TestCategory=JavaScriptConformance
   dotnet test src/NUglify.Tests/NUglify.Tests.csproj -c Debug
   dotnet test src/NUglify.Tests/NUglify.Tests.csproj -c Release
   ```

   Verify mismatches against upstream/spec expectations, minimize reproductions,
   and fix parser defects against those expectations. Report actual pass/fail/skip
   counts, exit codes, timings, exclusions, and any input rejection that blocked
   output checking. Update documented coverage and verified results after validation.

## Further references

- [Interpreting Test262 tests](https://github.com/tc39/test262/blob/2e58f87d8350ad51224617a041c6e041d52f8444/INTERPRETING.md)
- [V8 parser and preparser architecture](https://chromium.googlesource.com/v8/v8/+/main/docs/parsing/parser-and-ast.md)
- [SpiderMonkey test suites](https://firefox-source-docs.mozilla.org/js/test.html)
- [ECMAScript automatic semicolon insertion](https://tc39.es/ecma262/#sec-automatic-semicolon-insertion)
