# CSS conformance testing

The CSS NUnit tests combine independently sourced syntax/recovery cases, explicit
output expectations, and reproducible generated inputs. CI requires only .NET.

## Approach

- Keep CI .NET-only. No browser, Node, Rust, Python, or independent parser is
  required to execute the tests. Upstream tools are authoring-time options only.
- Use 40 curated cases from the CSS parsing corpus. Add other sources
  only for specific gaps found by the coverage review.
- Reuse the existing NUnit project, CSS input/expected file conventions, and
  FsCheck dependency. Use small C# case tables and focused assertions.
- Keep upstream revisions and attribution here and in fixture comments, plus
  the applicable licenses. Avoid new manifests, per-file hash inventories,
  generic AST comparison frameworks, and archived browser source files.
- Every unexpected diagnostic, recovery result, or output mismatch must fail
  `dotnet test`. Do not introduce known-failure baselines or hide parser defects
  by changing expectations or dropping failing imported cases.

Existing integration points include [Nesting.cs](../../../Css/Nesting.cs),
[MathFunctions.cs](../../../Css/MathFunctions.cs), and the CSS
[TestHelper.cs](../../../Css/Common/TestHelper.cs). Keep larger snippets in the
existing `TestData/CSS/Input` and `Expected` conventions, with small cases inline
where that is clearer.

## Sources

| Priority | Source | Intended use |
| --- | --- | --- |
| First | [CSS parsing tests](https://github.com/CourtBouillon/css-parsing-tests) | Core CSS Syntax cases: stylesheets, declaration lists, blocks, component values, and recovery. |
| Reference | [CSS Syntax Level 3](https://www.w3.org/TR/css-syntax-3/) | Resolve syntax/recovery expectations and differences between generic parsing and property-specific validity. |
| Supplement | [Web Platform Tests](https://github.com/web-platform-tests/wpt/tree/master/css) | Portable parsing cases for gaps such as nesting, selectors, custom properties, math, and query syntax. |
| Optional | [Chromium Blink parser tests](https://chromium.googlesource.com/chromium/src/+/refs/heads/main/third_party/blink/renderer/core/css/parser/) and [Firefox style tests](https://github.com/mozilla-firefox/firefox) | Small, targeted regressions not already covered by the corpus or WPT. |

Start with the CSS parsing corpus, then decide whether it is enough for the
selected scope. Its files pair source inputs with expected token/tree results;
those results guide expectations but do not match NUglify's output API directly.
The corpus README also identifies the original SimonSapin repository; confirm
the source revision, applicable license, and specification expectations on import.

For browser-specific additions, Chromium's CSS source is Blink, not V8. Use
Firefox's style/parser tests rather than SpiderMonkey's JavaScript tests. Extract
portable CSS inputs and assertions; do not run browser, DOM, CSSOM, rendering, or
C++ test infrastructure in CI. Review any expected-result normalization performed
by the upstream browser harness before translating it into a NUglify assertion.

The corpus is pinned to `203ce36bffd617db7f118c551e32794561fb273d`. Its complete
[CC0 dedication](LICENSE) is retained locally. Case comments record source files,
zero-based input/output pair indexes, and adaptations. Preserve decoded
source text and significant line endings; document wrappers and other adaptations.
Keep attribution outside snippets whose first character or byte sequence matters.

## Curated syntax and recovery cases

The [conformance fixture](../../../Css/ConformanceTests.cs) contains 40 source
cases: 25 stylesheets/wrapped blocks and 15 declaration lists, each under Minify
and Pretty (80 NUnit executions). The selection was fixed before probing NUglify:

| Upstream file | Selected pairs | Adaptation |
| --- | ---: | --- |
| `stylesheet.json` | 8 | Original stylesheet inputs. |
| `declaration_list.json` | 6 | Append recovery sentinels to malformed/bang-valued examples. |
| `one_declaration.json` | 9 | Use DeclarationList mode; add sentinels or custom-property names where noted. |
| `blocks_contents.json` | 8 | Wrap in a .x rule, with following declarations for recovery cases. |
| `component_value_list.json` | 9 | Embed values in custom properties or use the selector with a sentinel declaration. |

Eight cases intentionally contain malformed selectors/declarations. Their
expected diagnostics are asserted separately from corrected recovery output.
Excluded scope: standalone token/AST JSON APIs, byte-encoding detection, browser
property-value validation, unrelated color algorithms, and unbounded malformed
block combinations. The boundary cases below extend the feature/context coverage.
No source was excluded because its first NUglify result failed. Serialization may
retain the valid EOF spelling `@foo` and ignored top-level CDC text; neither
changes the upstream parse result. Pretty bang values omit a redundant space.

The profiles use fresh settings with comments removed, ColorNames.NoSwap,
AbbreviateHexColor=false, DecodeEscapes=false, RemoveEmptyBlocks=false,
MinifyExpressions=false, FixIE8Fonts=false, AllowEmbeddedAspNetBlocks=false,
IgnoreRazorEscapeSequence=true, and WarningLevel=1. Both severity-0 and severity-1
errors are asserted; compatibility warnings at severity 2+ are outside this
fixture's diagnostic contract.
Pretty retains its multiline formatting and terminal semicolons. Both profiles
are serialization/minification profiles, not independent parse-only engines.

Seventeen adjacent local regressions cover numeric replay through nesting,
unknown dimension units, priority/value boundaries, recovery sentinels, speech
units, and the distinct EOF-string versus newline-bad-string recovery paths.

## Boundary cases and source audit

The [boundary cases](../../../Css/ConformanceBoundaryTests.cs) use the same runner
and profiles: 51 table cases under Minify and Pretty, two math rejection checks,
and ten fixed controls from generated failures. The initial 41 source/context
choices were fixed before probing; ten adjacent table cases protect the resulting
fixes. Eleven table cases cover context,
ordering, and recovery: declaration-list versus stylesheet input, declarations
versus nested pseudo rules, @font-face/@page declaration bodies, and supported
@media/@supports/@container scopes. The remaining cases target token boundaries
and modern syntax where exact diagnostics/output assertions add coverage.

The existing Nesting and MathFunctions fixtures already cover ordinary nesting,
selector functions, range/style queries, math function families, and variable
substitution boundaries. Their broad tests are retained. These additions focus
on duplicate/fallback ordering, empty/nested var() fallbacks, opaque custom-property
blocks/functions, comment-only token boundaries, hex escapes and their whitespace
terminators, quoted/unquoted URLs, and LF/CR/CRLF/form-feed strings. Five table rows
enable DecodeEscapes, covering rewriting as well as retained escapes.

The pinned parsing corpus supplies generic Syntax token/tree cases but does not
cover modern nesting order or var() fallback semantics. Eight small supplemental
cases use [WPT](https://github.com/web-platform-tests/wpt) at
`0eefed92899544e837b8263db492a6235fa858a1`:

| WPT source | Cases | Adaptation and assertion |
| --- | ---: | --- |
| `css/css-syntax/serialize-consecutive-tokens.html` | 4 | Embed foo/bar, foo/bar(), 123/bar, and 123/% token pairs in custom properties with comment boundaries. Check serialization, without running variable substitution or computed-style code. |
| `css/css-nesting/mixed-declarations-rules.html` | 1 | Retain background-color declarations before, within, and after @media; omit unrelated width/height. Assert order/scope rather than computed color. |
| `css/css-variables/variable-declaration-07.html` | 1 | Map the style block to DeclarationList mode. Preserve duplicate declarations and the empty var() fallback, without evaluating it. |
| `css/selectors/parsing/parse-is-where.html` | 2 | Wrap the valid combinator-list input in :is() and :where() rules with sentinel declarations. No forgiving-list/property-validity framework. |

Fixture comments identify the precise upstream calls/style blocks and wrappers.
The complete pinned [WPT BSD license](WPT-LICENSE.md) is retained. Cameron
McCormack's `variable-declaration-07.html` instead explicitly dedicates its
copyright to the public domain under [CC0](https://creativecommons.org/publicdomain/zero/1.0/):
“Any copyright is dedicated to the Public Domain.” This exception and original
author attribution are retained here; the existing parsing-corpus CC0 dedication
remains in [LICENSE](LICENSE).

The corpus, existing tests, local boundary matrices, and these four WPT sources
cover the named gaps. Blink and Firefox style/parser sources were considered as
the next source tier; no remaining named gap justifies importing engine-specific
regressions. No browser source archives or browser/DOM/CSSOM harnesses are included.

Ten adjacent minimized cases (20 profile executions) cover decoded CRLF escape
terminators, ordinary-value/selector comments, and opaque-component mismatch,
bad-string, and EOF recovery, plus right-hand/later var() references in math.
Two further checks distinguish comment-only math
boundaries from the real whitespace required around binary operators. The latter
must report errors and must not become valid expressions merely by printing them.

The fixes preserve comment-separated tokens with minimal separator comments;
these separators do not count as actual whitespace when validating math. Hex
escapes consume their own optional terminator, including CRLF, and leave real
following whitespace distinct. String scanning handles form feed as a newline
and advances over ordinary control-character content. Escaped identifiers whose
decoded text resembles punctuation remain values. Custom-property functions and
simple blocks preserve opaque balanced components and raw numeric spelling;
`--name:` values take precedence over nested-selector lookahead. Malformed
components discard their declaration while preserving later content, and EOF
closes recovered components with an explicit truncation diagnostic. Math checks
defer until the outer function ends, so any var() leaves value validation pending
substitution without adding a new parser/runtime dependency.

### Updating the selection

1. Identify a concrete syntax/recovery gap against the existing fixtures. Select
   source inputs and independently review token/recovery/order expectations before
   probing NUglify; record the source file/call, adaptation, and immutable revision.
2. Retain the applicable full license and attribution. Keep only the portable CSS
   snippets and explicit expectations; do not import a browser runner or source archive.
3. Run the category and investigate every mismatch. Correct equivalent formatting
   goldens only after review; fix parser/output defects and add minimized controls.
   Never suppress a failing imported case or introduce a known-failure baseline.
4. Run Debug/Release solution builds and full suites, then update measured counts,
   runtimes, source coverage, and remaining scope here. No downloads occur in tests.

## Reproducible generated checks

The existing 14 [Nesting properties](../../../Css/Nesting.cs) retain their scanner,
nesting, order, recovery, whitespace, containment, and lookahead assertions. Each
now runs with seeds `104729` and `130363` (second seed component `20261120`),
100 samples per seed, and their original FsCheck size range of 1..100. Explicit
existing nesting bounds, including the depth 1..128 check, are retained. These are
28 NUnit cases and 2,800 samples per normal run.

The [boundary generators](../../../Css/ConformanceGeneratedTests.cs) add 12 NUnit
cases with the same seeds and sample count, and FsCheck size 0..32: 1,200 samples,
each checked under Minify and Pretty, then reparsed. Every seed must exercise all
forms in its family and all four contexts: declaration list, style rule,
@font-face body, and a style rule inside @media. This reports sampled coverage,
not an exhaustive cross product.

| Generator family | Forms | Primary oracle |
| --- | ---: | --- |
| Comment token pairs | 13 | Exact separators retain identifier, function, number, hash, at-keyword, and delimiter tokens. |
| Escape terminators | 4 | Optional escape whitespace remains distinct from real whitespace, with rewriting on/off and space/LF/CRLF/form-feed terminators. |
| Escaped code points | 7 | Invalid scalar escapes resolve independently to replacement values; valid astral escapes work. |
| Opaque components | 4 | Functions, brackets, braces, and parentheses preserve modeled values and raw numeric spelling, at 1..4 levels. |
| Declaration recovery | 5 | Missing-colon and LF/CR/CRLF/form-feed bad-string inputs report exact errors and preserve surrounding declarations in order. |
| Math whitespace | 4 | Real whitespace, comment-only invalid operators, and right-hand/later var() references have distinct diagnostics and output. |

Expected strings and diagnostics are constructed from these small productions,
independently of NUglify output. Reparse and idempotence supplement those oracles.
The shrinker retains the production and parsing context while reducing depth,
names, and comment text. It never turns a comment boundary into real whitespace
or removes the bad-string newline/invalid-code-point condition.

The first generated run failed `dotnet test` with exit 1 and produced attached
seed/counterexample reports; FsCheck reduced failing inputs by two or three steps
while retaining their production/context. Review corrected one formatting-only
@font-face Pretty suffix (a final newline). Real defects were promoted into ten
fixed regression checks: invalid scalar/surrogate escapes, six-digit zero with a
following hex digit, slash before a block comment, and ordinary // extension use.

Decoding now replaces surrogate and out-of-range escapes instead of throwing or
combining adjacent surrogate escapes. Escaped zero retains its token-equivalent
`\0` spelling for existing IE hacks, with a terminator when a following hex digit
could join it. A slash before a CSS block-comment opener is preserved rather than
consumed by the optional // extension. Two existing encoding snapshots changed
only the paired-surrogate value to two replacements; actual astral characters and
the IE-hack snapshot stay unchanged.

Failures use the shared [runner](../../../Css/GeneratedChecks.cs) to attach a text
report containing the replay seed, bounds, minimized CSS, mode/settings, expected
and actual output, and diagnostics or exception. Existing Nesting labels and the
FsCheck result supply their original property inputs and assertions. Reports go to
`TestResults/Conformance/CssGenerated-*.txt` beneath the test output directory;
the existing CI failure-artifact upload collects both CSS and JavaScript reports.

Set `NUGLIFY_CSS_SEED` to a positive integer to replay or explore a seed, then
filter by `CssGenerated` or the method named in the report. Clear it to restore
the two checked-in seeds. Exploratory seeds `8675309` and `99991` each passed all
40 generated NUnit cases (about one second per seed).

These checks cover selected token/recovery boundaries, not exhaustive CSS,
browser property-value validation, substitution/rendering, or arbitrary malformed
nesting. Existing lexical extensions remain supported. No independent runtime
validator or additional CI dependency is warranted for this bounded scope.

## Validation

The conformance category contains 251 NUnit cases: 80 corpus executions, 17 local
syntax/recovery controls, 104 boundary checks, ten minimized generated-failure
controls, and 40 seeded property tests. Those property tests run 4,000 samples per
normal suite; the new boundary families perform 2,400 input-profile checks plus
output reparse checks. Sample counts are distinct from NUnit test counts.

| Check | Passed | Failed | Skipped | Test execution time |
| --- | ---: | ---: | ---: | ---: |
| Generated checks and both Bug384 modes, Debug | 42 | 0 | 0 | 1 s |
| Full suite, Debug | 2,069 | 0 | 2 | 52 s |
| Full suite, Release | 2,069 | 0 | 2 | 29 s |

Both solution builds passed across configured targets with zero warnings/errors
(Debug 6.12 s, Release 5.70 s). The two existing skips are Bug78 and Collision.
The focused generated checks remain below the two-minute additional CI budget.
No source was dropped to hide a defect, and no known-failure baseline is used.

## Assertions and settings

CSS recovers from malformed input. A stylesheet containing an error is not
automatically an all-or-nothing rejection case. Test the expected diagnostic
behavior and the declarations/rules that survive recovery separately.

| Case | Required assertion |
| --- | --- |
| Well-formed syntax | Expected diagnostics and reviewed output under the selected settings. |
| Malformed syntax | Expected diagnostics, discarded content, recovered output, and preserved following content. |
| Token boundaries | Comments, escapes, whitespace, numbers, and delimiters retain their intended token meanings after printing. |
| Ordering and scope | Duplicate declarations, fallbacks, nested rules, and at-rule boundaries preserve their required order and association. |
| Output reparse | Emitted CSS reparses under the same `CssType`; valid output has no unexpected errors. |
| Idempotence | Where appropriate, minifying the emitted output again is stable; this supplements explicit expectations. |

Unknown properties or future syntax are not automatically syntax errors. A
minifier is not a browser's property-value validator. Select expectations that
match the supported parser entry point and retain content needed for forward
compatibility. Resolve corpus/specification disagreements before importing a case.

Use fresh settings and parser state for every case. Audit `new CssSettings()` and
`CssSettings.Pretty()` as the initial profiles, with explicit expectations for
each. Pretty output is not a parse-only or optimization-disabled profile.
Cover both `CssType.FullStyleSheet` and `CssType.DeclarationList` where applicable.
Make comment handling, escape decoding, color rewriting, empty-block removal,
legacy font fixes, replacement tokens, and embedded-language extensions explicit.
Use focused comment-removal cases to verify that removing trivia cannot merge tokens.

The existing snapshot helper returns an exit code without universally asserting
success. New cases must explicitly assert the expected diagnostics as well as
output. Crashes, exceptions, and hangs are never acceptable recovery outcomes.
Reparse/idempotence checks alone cannot prove preservation of CSS meaning.

## Running the tests

The existing CSS tests already run through the ordinary repository command:

```powershell
dotnet test
```

Use the category filter for the curated cases and the full project for regression
coverage. `CssGenerated` selects the reproducible property tests:

```powershell
dotnet test src/NUglify.Tests/NUglify.Tests.csproj -c Debug --filter TestCategory=CssConformance
dotnet test src/NUglify.Tests/NUglify.Tests.csproj -c Release --filter TestCategory=CssConformance
dotnet test src/NUglify.Tests/NUglify.Tests.csproj -c Release --filter TestCategory=CssGenerated
dotnet test src/NUglify.Tests/NUglify.Tests.csproj -c Debug
dotnet test src/NUglify.Tests/NUglify.Tests.csproj -c Release
```

Generated cases carry both `CssConformance` and `CssGenerated` categories. Keep
explicit expectations, retained licenses/provenance, failures visible to CI, and
passing full suites in both configurations when updating the selection.
