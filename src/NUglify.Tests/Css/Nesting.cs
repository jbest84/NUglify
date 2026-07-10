using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using FsCheck;
using NUglify.Css;
using NUnit.Framework;

namespace NUglify.Tests.Css
{
    /// <summary>
    /// Property-based tests for CSS Nesting scanner behavior (Feature: css-nesting).
    /// These tests exercise the internal <c>CssScanner</c> directly via reflection so that
    /// tokenization can be observed without going through the parser.
    /// </summary>
    [TestFixture]
    public class Nesting
    {

        // ---- Reflection plumbing to reach the internal CssScanner / CssToken ----

        static readonly Assembly NUglifyAssembly = typeof(Uglify).Assembly;
        static readonly Type ScannerType = NUglifyAssembly.GetType("NUglify.Css.CssScanner", throwOnError: true);
        static readonly Type TokenType = NUglifyAssembly.GetType("NUglify.Css.CssToken", throwOnError: true);
        static readonly MethodInfo NextTokenMethod = ScannerType.GetMethod("NextToken", new[] { typeof(bool) });
        static readonly PropertyInfo TokenTypeProperty = TokenType.GetProperty("TokenType");

        /// <summary>
        /// Tokenizes <paramref name="css"/> and returns the number of tokens whose
        /// TokenType enum name equals <paramref name="tokenTypeName"/>.
        /// </summary>
        static int CountTokensOfType(string css, string tokenTypeName)
        {
            using (var reader = new StringReader(css ?? string.Empty))
            {
                var scanner = Activator.CreateInstance(
                    ScannerType,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    binder: null,
                    args: new object[] { reader },
                    culture: null);

                var count = 0;
                // Safety bound to avoid any accidental infinite loop.
                var maxIterations = (css?.Length ?? 0) * 4 + 16;
                for (var i = 0; i < maxIterations; i++)
                {
                    var token = NextTokenMethod.Invoke(scanner, new object[] { false });
                    if (token == null)
                        break;

                    var tokenType = TokenTypeProperty.GetValue(token);
                    if (tokenType != null && tokenType.ToString() == tokenTypeName)
                        count++;
                }

                return count;
            }
        }

        static void RunProperty(Property property)
        {
            // FsCheck's Quick configuration runs a minimum of 100 iterations by default
            // and throws (failing the NUnit test) on the first counterexample it finds.
            property.QuickCheckThrowOnFailure();
        }

        // Selector fragments that never contain '&', a quote, or a comment opener,
        // so any '&' in a generated string is guaranteed to be a bare nesting selector.
        static readonly string[] SafeSelectorFragments =
        {
            ".a", "#b", "div", ".x", ":hover", "[attr]", "*", " ", ">", "+", "~", ","
        };

        // ------------------------------------------------------------------
        // Feature: css-nesting, Property 1: Nesting selector token count -
        // For any selector-context string containing N unescaped '&' characters
        // (none inside strings or comments), the scanner produces exactly N
        // NestingSelector tokens, one per '&', none merged.
        // Validates: Requirements 1.1, 1.2, 1.3
        // ------------------------------------------------------------------
        [Test]
        public void Property1_NestingSelectorTokenCount()
        {
            // Each item is either a bare '&' or a safe fragment containing no '&'.
            var itemGen = Gen.Elements(SafeSelectorFragments.Concat(new[] { "&" }).ToArray());
            var stringGen = Gen.ListOf(itemGen).Select(items => string.Concat(items));

            var property = Prop.ForAll(Arb.From(stringGen), (string selector) =>
            {
                // Because no fragment other than "&" contributes an ampersand, and no
                // string/comment contexts are produced, the number of '&' characters in
                // the input equals the expected number of NestingSelector tokens.
                var expected = selector.Count(c => c == '&');
                var actual = CountTokensOfType(selector, "NestingSelector");
                return expected == actual;
            });

            RunProperty(property);
        }

        // ------------------------------------------------------------------
        // Feature: css-nesting, Property 2: Ampersand in a literal is never a nesting token -
        // For any CSS input where every '&' occurs inside a string literal or a
        // comment, the scanner produces zero NestingSelector tokens.
        // Validates: Requirement 1.4
        // ------------------------------------------------------------------
        [Test]
        public void Property2_AmpersandInLiteralIsNeverNestingToken()
        {
            // Contents that contain '&' but no delimiter that would terminate the
            // enclosing literal early (no '*/', no matching quote, no backslash).
            var literalInner = Gen.Elements(new[] { "&", "a&b", "x & y", "&&", " a & b " });

            var commentChunk = literalInner.Select(inner => "/*" + inner + "*/");
            var doubleQuotedChunk = literalInner.Select(inner => "\"" + inner + "\"");
            var singleQuotedChunk = literalInner.Select(inner => "'" + inner + "'");

            // Safe text that never contains '&', a quote, or a comment opener.
            var safeText = Gen.Elements(new[] { "a", "div", ".x", "#y", ":z", " ", "p", "b", "{color:red}" });

            var chunkGen = Gen.OneOf(new[] { commentChunk, doubleQuotedChunk, singleQuotedChunk, safeText });
            var stringGen = Gen.ListOf(chunkGen).Select(chunks => string.Concat(chunks));

            var property = Prop.ForAll(Arb.From(stringGen), (string css) =>
            {
                return CountTokensOfType(css, "NestingSelector") == 0;
            });

            RunProperty(property);
        }

        // ---- Model + generator for nested-selector emission (Property 5) ----

        // Combinator kinds joining two compound units of a complex nested selector.
        enum Comb { Descendant = 0, Child = 1, Adjacent = 2, Sibling = 3 }

        // One compound unit of a nested selector, together with the combinator that
        // precedes it (ignored for the first unit) and the incidental whitespace the
        // source placed around a symbol combinator (>, +, ~).
        sealed class Seg
        {
            public string Unit;
            public Comb Comb;
            public string WsBefore;
            public string WsAfter;
        }

        // Leading-safe compound units: every one begins with &, ., #, :, or [ so that
        // the block body can never mistake it for the start of a declaration. The pool
        // deliberately exercises & standalone, joined (&.bar / &:hover), doubled (&&),
        // and bare compound selectors (used to build ".parent &" style patterns).
        static readonly string[] NestedUnits =
        {
            "&", "&.bar", "&:hover", "&&", ".foo", "#id", ":hover", ".parent"
        };

        // Builds the (source, expected-minified) pair for a nested complex selector from
        // a list of segments, guaranteeing at least one '&' is present so the property is
        // always about the nesting selector.
        static (string source, string expected) BuildNestedSelector(IReadOnlyList<Seg> input)
        {
            // Cap the length so we never approach the line-break threshold.
            var segs = input.Take(8).ToList();

            // Guarantee at least one '&' by rewriting the last unit if none is present.
            // A fresh Seg is created so the shared generator pool is never mutated.
            if (!segs.Any(s => s.Unit.Contains('&')))
            {
                var last = segs[segs.Count - 1];
                segs[segs.Count - 1] = new Seg { Unit = "&", Comb = last.Comb, WsBefore = last.WsBefore, WsAfter = last.WsAfter };
            }

            var src = new StringBuilder();
            var exp = new StringBuilder();

            for (var i = 0; i < segs.Count; i++)
            {
                var s = segs[i];
                if (i > 0)
                {
                    switch (s.Comb)
                    {
                        case Comb.Descendant:
                            // Descendant combinator is whitespace; minified keeps exactly one space.
                            src.Append(' ');
                            exp.Append(' ');
                            break;
                        case Comb.Child:
                            src.Append(s.WsBefore).Append('>').Append(s.WsAfter);
                            exp.Append('>');
                            break;
                        case Comb.Adjacent:
                            src.Append(s.WsBefore).Append('+').Append(s.WsAfter);
                            exp.Append('+');
                            break;
                        case Comb.Sibling:
                            src.Append(s.WsBefore).Append('~').Append(s.WsAfter);
                            exp.Append('~');
                            break;
                    }
                }

                src.Append(s.Unit);
                exp.Append(s.Unit);
            }

            return (src.ToString(), exp.ToString());
        }

        static Gen<List<Seg>> NestedSelectorGen()
        {
            var segGen =
                from unit in Gen.Elements(NestedUnits)
                from comb in Gen.Elements(new[] { Comb.Descendant, Comb.Child, Comb.Adjacent, Comb.Sibling })
                from wsB in Gen.Elements(new[] { "", " " })
                from wsA in Gen.Elements(new[] { "", " " })
                select new Seg { Unit = unit, Comb = comb, WsBefore = wsB, WsAfter = wsA };

            return Gen.NonEmptyListOf(segGen).Select(items => items.ToList());
        }

        // ------------------------------------------------------------------
        // Feature: css-nesting, Property 5: Nesting selector emitted verbatim in position -
        // For any nested selector using & standalone, joined to a compound selector,
        // doubled (&&), repeated with combinators (& + &), or placed after another
        // selector (.parent &), the output preserves every & occurrence in source order
        // with the same combinators and with zero whitespace where the source had none.
        // Validates: Requirements 3.1, 3.2, 3.3, 3.4, 3.5
        // ------------------------------------------------------------------
        [Test]
        public void Property5_NestingSelectorEmittedVerbatimInPosition()
        {
            var property = Prop.ForAll(Arb.From(NestedSelectorGen()), (List<Seg> segs) =>
            {
                var (nestedSource, nestedExpected) = BuildNestedSelector(segs);

                // Wrap the nested selector in a parent rule whose own selector contains no
                // '&', so every '&' in the output originates from the nested selector.
                var source = ".p{" + nestedSource + "{color:#f00}}";
                var expected = ".p{" + nestedExpected + "{color:#f00}}";

                var result = Uglify.Css(source);

                return (!result.HasErrors && result.Code == expected)
                    .Label($"source=<{source}> code=<{result.Code}> expected=<{expected}> hasErrors={result.HasErrors}");
            });

            RunProperty(property);
        }

        // Parent selectors used to wrap a nested relative selector.
        static readonly string[] ParentSelectors = { ".parent", "div", "#main", ".a.b", "section" };

        // Leading combinators for relative nested selectors; "" means a bare compound selector.
        static readonly string[] LeadingForms = { "", ">", "+", "~" };

        // Bare compound selectors chosen so the minifier preserves them verbatim
        // (no transformations such as ::before -> :before).
        static readonly string[] CompoundSelectors =
        {
            ".child", ":hover", "div", "#id", "[data-x]", "span", ".a", "*", ".x.y"
        };

        // Declarations placed inside the nested rule body.
        static readonly string[] Declarations = { "color:red", "margin:0", "display:block" };

        // ------------------------------------------------------------------
        // Feature: css-nesting, Property 6: Relative nested selectors keep their leading form -
        // For any nested selector that begins with a combinator (>, +, ~) or a bare
        // compound selector, the output preserves the leading combinator or compound
        // selector unchanged and never inserts an explicit '&'.
        // Validates: Requirements 4.1, 4.2, 4.3, 4.4
        // ------------------------------------------------------------------
        [Test]
        public void Property6_RelativeNestedSelectorsKeepLeadingForm()
        {
            // Precompute every combination of parent / leading form / compound / declaration.
            // (plain System.Linq over arrays, then a single Gen.Elements over the array.)
            var combos =
                (from parent in ParentSelectors
                 from leading in LeadingForms
                 from compound in CompoundSelectors
                 from decl in Declarations
                 select Tuple.Create(parent, leading, compound, decl)).ToArray();

            var gen = Gen.Elements(combos);

            var property = Prop.ForAll(Arb.From(gen), (Tuple<string, string, string, string> t) =>
            {
                var parent = t.Item1;
                var leading = t.Item2;
                var compound = t.Item3;
                var decl = t.Item4;

                // A relative nested selector: either "<combinator> <compound>" or a bare "<compound>".
                var nestedSelector = leading.Length == 0 ? compound : leading + " " + compound;
                var source = parent + " {\n  " + nestedSelector + " {\n    " + decl + ";\n  }\n}";

                var result = Uglify.Css(source);
                var code = result.Code ?? string.Empty;

                // Minified form of the relative selector: leading combinator (whitespace removed)
                // directly followed by the compound selector, then the block opener.
                var expectedNested = leading + compound + "{";

                // 4.3 / 4.4: never insert an explicit '&'.
                var noExplicitAmpersand = !code.Contains("&");
                // 4.1 / 4.2: the leading combinator or compound selector is preserved unchanged.
                var leadingFormPreserved = code.Contains(expectedNested);

                return !result.HasErrors && noExplicitAmpersand && leadingFormPreserved;
            });

            RunProperty(property);
        }

        // ---- Model + generator for nested selector lists (Property 7) ----

        // Nested selectors that contain at least one '&'. Every entry's minified form is
        // byte-for-byte identical to its source form (no internal whitespace, no token the
        // minifier rewrites), so a per-selector source string is also its expected output.
        static readonly string[] AmpListSelectors =
        {
            "&", "&.bar", "&:hover", "&&", "&.a.b"
        };

        // Relative / bare compound nested selectors (no leading '&'). The parser leaves these
        // unchanged and never inserts an explicit '&'. Again source form == minified form.
        static readonly string[] BareListSelectors =
        {
            ".child", ":hover", "#id", "div", "[data-x]", "span", ".a.b"
        };

        // One entry of a nested selector list: the selector plus the incidental whitespace the
        // source placed on either side of the comma that precedes it (ignored for the first
        // entry, which has no preceding comma).
        sealed class ListSel
        {
            public string Sel;
            public string WsBefore;
            public string WsAfter;
        }

        static Gen<ListSel> ListSelGen(string[] pool)
        {
            return from sel in Gen.Elements(pool)
                   from wsB in Gen.Elements(new[] { "", " " })
                   from wsA in Gen.Elements(new[] { "", " " })
                   select new ListSel { Sel = sel, WsBefore = wsB, WsAfter = wsA };
        }

        // Produces a list of two or more distinct nested selectors mixing '&'-containing and
        // relative/bare compound selectors. The first entry is always an '&' selector and the
        // second a bare selector (guaranteeing both the "two or more" and "mix" requirements),
        // followed by up to three more drawn from either pool. Duplicates are removed while
        // preserving source order so the expected output list is unambiguous.
        static Gen<List<ListSel>> NestedSelectorListGen()
        {
            var combined = AmpListSelectors.Concat(BareListSelectors).ToArray();

            return from first in ListSelGen(AmpListSelectors)
                   from second in ListSelGen(BareListSelectors)
                   from extra in Gen.ListOf(ListSelGen(combined))
                   select DistinctBySelector(new[] { first, second }.Concat(extra.Take(3)));
        }

        static List<ListSel> DistinctBySelector(IEnumerable<ListSel> items)
        {
            var seen = new HashSet<string>();
            var result = new List<ListSel>();
            foreach (var item in items)
            {
                if (seen.Add(item.Sel))
                    result.Add(item);
            }
            return result;
        }

        // ------------------------------------------------------------------
        // Feature: css-nesting, Property 7: Nested selector list membership -
        // For any nested selector list of two or more selectors sharing one block, every
        // source selector appears in the output list in source order, separated by a single
        // comma with no surrounding whitespace in minified output.
        // Validates: Requirements 5.1, 5.2, 5.4, 8.2
        // ------------------------------------------------------------------
        [Test]
        public void Property7_NestedSelectorListMembership()
        {
            var property = Prop.ForAll(Arb.From(NestedSelectorListGen()), (List<ListSel> items) =>
            {
                var listSource = new StringBuilder();
                var listExpected = new StringBuilder();

                for (var i = 0; i < items.Count; i++)
                {
                    if (i > 0)
                    {
                        // Optional whitespace around the comma is allowed in the source (5.1);
                        // minified output collapses it to a single bare comma (5.2 / 8.2).
                        listSource.Append(items[i].WsBefore).Append(',').Append(items[i].WsAfter);
                        listExpected.Append(',');
                    }

                    listSource.Append(items[i].Sel);
                    listExpected.Append(items[i].Sel);
                }

                // Wrap the nested selector list in a parent rule whose own selector contains no
                // '&' and no comma, so the only selector list in the output is the nested one.
                var source = ".p{" + listSource + "{color:#f00}}";
                var expected = ".p{" + listExpected + "{color:#f00}}";

                var result = Uglify.Css(source);

                // Exact-equality of the whole rule confirms every source selector appears in the
                // output list, in source order, joined by a single comma with no surrounding
                // whitespace (source form of each selector equals its minified form by design).
                return (!result.HasErrors && result.Code == expected)
                    .Label($"source=<{source}> code=<{result.Code}> expected=<{expected}> hasErrors={result.HasErrors}");
            });

            RunProperty(property);
        }

        // Invalid nested selector lists: each contains an empty selector position
        // (leading comma, trailing comma, or doubled comma) or an otherwise invalid
        // selector. Every list embeds the sentinel token "leaksel" (and some a second
        // "leaktwo") so the assertion can detect any selector leaking into partial
        // output. Whitespace variants around commas exercise the "optional whitespace"
        // allowance while keeping the offending position empty/invalid.
        static readonly string[] InvalidNestedSelectorLists =
        {
            // Leading comma -> empty first position.
            ",&.leaksel",
            ", &.leaksel",
            ",.leaksel",
            // Trailing comma -> empty last position.
            "&.leaksel,",
            "&.leaksel ,",
            ".leaksel,",
            // Doubled comma -> empty middle position.
            "&.leaksel,,&.leaktwo",
            "&.leaksel, ,.leaktwo",
            ".leaksel,,.leaktwo",
            // Otherwise-invalid selector in the list.
            "&.leaksel,!bad",
            "&.leaksel,)(",
            "&.leaksel,,,",
        };

        // ------------------------------------------------------------------
        // Feature: css-nesting, Property 8: Invalid nested selector list fails atomically -
        // For any nested selector list containing an empty selector position (leading,
        // trailing, or doubled comma) or an invalid selector, the parser reports a parse
        // error and emits none of the list's selectors rather than partial output.
        // Validates: Requirements 5.5, 5.6
        // ------------------------------------------------------------------
        [Test]
        public void Property8_InvalidNestedSelectorListFailsAtomically()
        {
            var gen = Gen.Elements(InvalidNestedSelectorLists);

            var property = Prop.ForAll(Arb.From(gen), (string invalidList) =>
            {
                // Wrap the invalid nested selector list in a parent rule, sharing one block.
                var source = ".p{" + invalidList + "{color:red}}";

                var result = Uglify.Css(source);
                var code = result.Code ?? string.Empty;

                // 5.5 / 5.6: the invalid list must be reported as a parse error, and the
                // whole list must fail atomically - none of the list's selectors (tracked
                // via the sentinel tokens) may leak into the output.
                var reportsError = result.HasErrors;
                var noSelectorLeaked = !code.Contains("leaksel") && !code.Contains("leaktwo");

                return (reportsError && noSelectorLeaked)
                    .Label($"source=<{source}> code=<{code}> hasErrors={result.HasErrors}");
            });

            RunProperty(property);
        }

        // ---- Model + generator for source order preservation (Property 3) ----

        // Kind of an item appearing inside a parent rule's declaration block.
        enum ItemKind { Declaration = 0, NestedRule = 1 }

        // One item in a parent rule's block. Each item carries a unique two-letter marker
        // so its position in the output can be located unambiguously. Because every marker
        // is the same length and drawn without repetition, no item's search token is a
        // substring or prefix of another's.
        sealed class BodyItem
        {
            public ItemKind Kind;
            public string Marker;
        }

        // Distinct two-letter markers. Declarations use an unknown property named "p<m>"
        // (passed through verbatim and never merged, since every property name differs)
        // and nested rules use a selector ".s<m>", so the search tokens ("p<m>" / ".s<m>")
        // are all unique and non-overlapping.
        static readonly string[] OrderMarkers =
        {
            "aa", "ab", "ac", "ad", "ae", "af", "ag", "ah"
        };

        // The token that uniquely identifies an item's presence/position in the output.
        static string ItemToken(BodyItem item)
            => item.Kind == ItemKind.Declaration ? "p" + item.Marker : ".s" + item.Marker;

        // The source text contributed by an item: an unknown-property declaration
        // "p<m>:1;" (semicolon-terminated so adjacent items never merge) or a nested rule
        // ".s<m>{color:red}".
        static string ItemSource(BodyItem item)
            => item.Kind == ItemKind.Declaration
                ? "p" + item.Marker + ":1;"
                : ".s" + item.Marker + "{color:red}";

        static List<BodyItem> DistinctByMarker(IEnumerable<BodyItem> items)
        {
            var seen = new HashSet<string>();
            var result = new List<BodyItem>();
            foreach (var item in items)
            {
                if (seen.Add(item.Marker))
                    result.Add(item);
            }
            return result;
        }

        // Produces an arbitrary interleaving of declarations and nested rules. The first
        // two items are forced to be a declaration then a nested rule (guaranteeing a real
        // mixture of both kinds), followed by up to four more items of random kind. Markers
        // are de-duplicated while preserving source order so every item is uniquely locatable.
        static Gen<List<BodyItem>> BlockItemsGen()
        {
            var itemGen =
                from marker in Gen.Elements(OrderMarkers)
                from kind in Gen.Elements(new[] { ItemKind.Declaration, ItemKind.NestedRule })
                select new BodyItem { Marker = marker, Kind = kind };

            var first = new BodyItem { Kind = ItemKind.Declaration, Marker = "aa" };
            var second = new BodyItem { Kind = ItemKind.NestedRule, Marker = "ab" };

            return from extra in Gen.ListOf(itemGen)
                   select DistinctByMarker(new[] { first, second }.Concat(extra.Take(4)));
        }

        // Returns true when every token appears in the output in strictly increasing
        // position, i.e. all items are present and in the same relative order as in source.
        static bool OrderPreserved(string output, IReadOnlyList<string> tokens)
        {
            if (output == null)
                return false;

            var lastIndex = -1;
            foreach (var token in tokens)
            {
                var index = output.IndexOf(token, StringComparison.Ordinal);
                if (index <= lastIndex)
                    return false;
                lastIndex = index;
            }
            return true;
        }

        // ------------------------------------------------------------------
        // Feature: css-nesting, Property 3: Source order preservation -
        // For any declaration block containing an arbitrary interleaving of declarations
        // and nested rules, the relative order of those items in the minified and pretty
        // output matches their order in the source.
        // Validates: Requirements 2.2, 2.3, 8.6
        // ------------------------------------------------------------------
        [Test]
        public void Property3_SourceOrderPreservation()
        {
            var property = Prop.ForAll(Arb.From(BlockItemsGen()), (List<BodyItem> items) =>
            {
                var body = new StringBuilder();
                foreach (var item in items)
                    body.Append(ItemSource(item));

                // Wrap the interleaved items in a parent rule whose own selector (".p")
                // contains none of the item markers, so each token located in the output
                // originates from exactly one item.
                var source = ".p{" + body + "}";

                var expectedOrder = items.Select(ItemToken).ToList();

                var minified = Uglify.Css(source);
                var pretty = Uglify.Css(source, new CssSettings { OutputMode = OutputMode.MultipleLines });

                var minifiedOk = !minified.HasErrors && OrderPreserved(minified.Code, expectedOrder);
                var prettyOk = !pretty.HasErrors && OrderPreserved(pretty.Code, expectedOrder);

                return (minifiedOk && prettyOk)
                    .Label($"source=<{source}> minified=<{minified.Code}> pretty=<{pretty.Code}> " +
                           $"minErrors={minified.HasErrors} prettyErrors={pretty.HasErrors}");
            });

            RunProperty(property);
        }

        // ---- Generator for whole nested stylesheets (Property 4) ----

        // Builds the source text of a declaration block body: an interleaving of
        // declarations and (when depth > 0) further nested rules, so that generated
        // stylesheets exercise the parent/child association across multiple levels.
        // At least one item is always produced so no block is empty (empty blocks may
        // legitimately be dropped, which is not what this idempotence property tests).
        static Gen<string> BlockBodySourceGen(int depth)
        {
            var declGen = Gen.Elements(Declarations).Select(d => d + ";");

            Gen<string> itemGen = depth <= 0
                ? declGen
                : Gen.OneOf(declGen, NestedRuleSourceGen(depth - 1));

            return Gen.NonEmptyListOf(itemGen).Select(items => string.Concat(items.Take(5)));
        }

        // Builds a nested rule's source text at the given remaining depth. The nested
        // rule's selector reuses the Property 5 selector model (BuildNestedSelector),
        // which always includes at least one '&' and mixes combinators, compound units,
        // and doubling. When depth > 0 its body may contain further nested rules.
        static Gen<string> NestedRuleSourceGen(int depth)
        {
            var selectorGen = NestedSelectorGen().Select(segs => BuildNestedSelector(segs).source);

            return from selector in selectorGen
                   from body in BlockBodySourceGen(depth)
                   select selector + "{" + body + "}";
        }

        // Produces a whole valid nested stylesheet a few levels deep: a top-level rule
        // whose own selector contains no '&' wrapping a body that nests to depth 1..3
        // (up to four total levels), reusing the nested-selector and declaration pools
        // already defined above.
        static Gen<string> NestedStylesheetGen()
        {
            return from parent in Gen.Elements(ParentSelectors)
                   from depth in Gen.Choose(1, 3)
                   from body in BlockBodySourceGen(depth)
                   select parent + "{" + body + "}";
        }

        // ------------------------------------------------------------------
        // Feature: css-nesting, Property 4: Nesting structure round-trip -
        // For any valid nested stylesheet, parsing the source and then re-parsing the
        // parser's own output yields output identical to the first pass (the
        // transformation is idempotent and preserves the parent/child nesting
        // association and depth).
        // Validates: Requirements 2.1, 6.4, 7.4
        // ------------------------------------------------------------------
        [Test]
        public void Property4_NestingStructureRoundTrip()
        {
            var property = Prop.ForAll(Arb.From(NestedStylesheetGen()), (string source) =>
            {
                // First pass: minify the generated nested stylesheet.
                var first = Uglify.Css(source);

                // Second pass: re-parse the parser's own output.
                var second = Uglify.Css(first.Code ?? string.Empty);

                // Idempotence: the second pass must produce output identical to the first
                // and neither pass may report errors. Because the minifier preserves the
                // parent/child nesting association and depth, a correctly-nested first
                // output re-parses to itself unchanged.
                return (!first.HasErrors && !second.HasErrors && first.Code == second.Code)
                    .Label($"source=<{source}> first=<{first.Code}> second=<{second.Code}> " +
                           $"firstErrors={first.HasErrors} secondErrors={second.HasErrors}");
            });

            RunProperty(property);
        }

        // ---- Helpers for arbitrary depth preservation (Property 9) ----

        // Builds a stylesheet nested to exactly <paramref name="depth"/> levels: a
        // top-level rule with a short real selector (".a"), then depth-1 further levels
        // each introduced by a standalone '&' nesting selector, with a single declaration
        // ("color:red") at the innermost level. The resulting source has exactly <depth>
        // opening braces, one per nesting level. Selectors are kept intentionally short so
        // the generated stylesheet stays well below the line-break threshold even for large
        // depths (>= 64), so the descendant combinator / wrapping logic never interferes.
        static string BuildDeepNesting(int depth)
        {
            var sb = new StringBuilder();
            sb.Append(".a");
            for (var i = 1; i < depth; i++)
                sb.Append("{&");
            sb.Append("{color:red");
            for (var i = 0; i < depth; i++)
                sb.Append('}');
            return sb.ToString();
        }

        // Returns the maximum brace-nesting depth of <paramref name="s"/>, i.e. the deepest
        // point of balanced '{' ... '}' nesting. For a correctly-emitted stylesheet nested
        // to depth D this equals D.
        static int MaxBraceDepth(string s)
        {
            var depth = 0;
            var max = 0;
            foreach (var c in s)
            {
                if (c == '{')
                {
                    depth++;
                    if (depth > max)
                        max = depth;
                }
                else if (c == '}')
                {
                    depth--;
                }
            }
            return max;
        }

        // Returns the brace-nesting depth at the first occurrence of <paramref name="token"/>
        // in <paramref name="s"/>, or -1 if the token is absent. Used to confirm the innermost
        // declaration sits at the deepest nesting level.
        static int BraceDepthAtToken(string s, string token)
        {
            var index = s.IndexOf(token, StringComparison.Ordinal);
            if (index < 0)
                return -1;

            var depth = 0;
            for (var i = 0; i < index; i++)
            {
                if (s[i] == '{')
                    depth++;
                else if (s[i] == '}')
                    depth--;
            }
            return depth;
        }

        // ------------------------------------------------------------------
        // Feature: css-nesting, Property 9: Arbitrary depth is preserved -
        // For any nesting depth D (including D >= 64), a stylesheet nested to depth D parses
        // without a fixed-depth failure and the emitted output has nesting depth D: the output
        // contains exactly D opening braces, its maximum balanced brace depth is D, and the
        // innermost declaration sits at the deepest (D-th) level.
        // Validates: Requirements 6.2, 6.3, 6.4
        // ------------------------------------------------------------------
        [Test]
        public void Property9_ArbitraryDepthIsPreserved()
        {
            // Depths span 1..128 so the >= 64 case (arbitrary, non-fixed depth) is exercised
            // on roughly half of all iterations.
            var depthGen = Gen.Choose(1, 128);

            var property = Prop.ForAll(Arb.From(depthGen), (int depth) =>
            {
                var source = BuildDeepNesting(depth);

                var result = Uglify.Css(source);
                var code = result.Code ?? string.Empty;

                // 6.2 / 6.3: parsing must succeed at any depth (no fixed-depth failure).
                var noErrors = !result.HasErrors;
                // 6.4: the emitted nesting depth matches the source depth. One '{' per level,
                // maximum balanced brace depth equal to D, and the single declaration located
                // at the deepest level.
                var braceCount = code.Count(c => c == '{');
                var openBracesMatch = braceCount == depth;
                var maxDepthMatch = MaxBraceDepth(code) == depth;
                var declarationAtDeepest = BraceDepthAtToken(code, "color") == depth;

                return (noErrors && openBracesMatch && maxDepthMatch && declarationAtDeepest)
                    .Label($"depth={depth} code=<{code}> hasErrors={result.HasErrors} " +
                           $"braceCount={braceCount} maxDepth={MaxBraceDepth(code)} " +
                           $"declDepth={BraceDepthAtToken(code, "color")}");
            });

            RunProperty(property);
        }

        // ---- Model + generator for minified whitespace invariants (Property 11) ----

        // Whitespace fragments injected into "insignificant" source positions (around
        // braces, the declaration colon, selector-list commas, between items, and around a
        // nested selector). Every one is pure whitespace, so minified output must strip all
        // of it. "" exercises the already-tight case; the rest exercise collapse/removal.
        static readonly string[] WsFragments = { "", " ", "  ", "\t", "\n" };

        // Kind of an item inside the generated parent rule's block.
        enum WsItemKind { Declaration = 0, NestedRule = 1 }

        // One item of the parent block, carrying the randomized whitespace fragments used to
        // pad its source form. Nested rules additionally carry one or two nested-selector
        // segment lists (reusing the Property 5 selector model) and a flag choosing between a
        // single nested selector and a two-selector nested list (to exercise comma handling).
        sealed class WsItem
        {
            public WsItemKind Kind;
            public List<Seg> Selector;   // nested rule: first / only selector
            public List<Seg> Selector2;  // nested rule: second selector (list form only)
            public bool IsList;          // nested rule: emit a two-selector list
            public string[] Ws;          // whitespace fragments consumed by position
        }

        // A whole parent rule to render: a top-level selector (no '&', minifier-stable) whose
        // block holds an interleaving of declarations and nested rules.
        sealed class WsParentRule
        {
            public string Parent;
            public List<WsItem> Items;
        }

        static string WsAt(string[] ws, int index)
            => (ws != null && index >= 0 && index < ws.Length) ? ws[index] : string.Empty;

        static Gen<WsItem> WsDeclItemGen()
        {
            return from ws in Gen.ListOf(Gen.Elements(WsFragments))
                   select new WsItem { Kind = WsItemKind.Declaration, Ws = ws.ToArray() };
        }

        static Gen<WsItem> WsNestedItemGen()
        {
            return from segs1 in NestedSelectorGen()
                   from segs2 in NestedSelectorGen()
                   from isList in Gen.Elements(new[] { false, true })
                   from ws in Gen.ListOf(Gen.Elements(WsFragments))
                   select new WsItem
                   {
                       Kind = WsItemKind.NestedRule,
                       Selector = segs1,
                       Selector2 = segs2,
                       IsList = isList,
                       Ws = ws.ToArray()
                   };
        }

        // Inserts the guaranteed nested rule at a pseudo-random position so the block exercises
        // nested-rule-after-declaration, declaration-after-nested-rule, and nested-after-nested.
        static List<WsItem> InsertAt(List<WsItem> list, WsItem item, int pos)
        {
            var index = pos % (list.Count + 1);
            list.Insert(index, item);
            return list;
        }

        // Produces a parent rule containing at least one nested rule (guaranteeing the property
        // is always about "a parent rule containing nested rules") plus up to four further items
        // of arbitrary kind.
        static Gen<WsParentRule> WsParentRuleGen()
        {
            var itemGen = Gen.OneOf(WsDeclItemGen(), WsNestedItemGen());

            return from parent in Gen.Elements(ParentSelectors)
                   from nested in WsNestedItemGen()
                   from rest in Gen.ListOf(itemGen)
                   from pos in Gen.Choose(0, 8)
                   select new WsParentRule
                   {
                       Parent = parent,
                       Items = InsertAt(rest.Take(4).ToList(), nested, pos)
                   };
        }

        // Builds the (source, expected-minified) pair for a parent rule. The source pads every
        // insignificant position with arbitrary whitespace; the expected form is the tight
        // minified rule per the invariants: no whitespace inside/outside braces, none around the
        // declaration colon or selector-list commas, none around >/+/~ combinators, exactly one
        // space for the descendant combinator (both provided by BuildNestedSelector), and no
        // separator inserted after a nested rule's closing brace. Declarations use unique names
        // (p{i}/q{i}) and nested selectors get unique suffixes (.u{i}/.v{i}) so nothing is merged
        // or de-duplicated, keeping the expected output exact and unambiguous.
        static (string source, string expected) BuildWhitespaceParentRule(WsParentRule rule)
        {
            var items = rule.Items;
            var src = new StringBuilder();
            var exp = new StringBuilder();

            src.Append(rule.Parent).Append('{');
            exp.Append(rule.Parent).Append('{');

            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];
                var ws = item.Ws;
                var isLast = i == items.Count - 1;

                if (item.Kind == WsItemKind.Declaration)
                {
                    // p{i} : 1  -> a trailing ';' only when another item follows; whitespace is
                    // injected around the colon and after the item, and must all be removed.
                    src.Append(WsAt(ws, 0)).Append('p').Append(i)
                       .Append(WsAt(ws, 1)).Append(':').Append(WsAt(ws, 2)).Append('1')
                       .Append(isLast ? string.Empty : ";").Append(WsAt(ws, 3));

                    exp.Append('p').Append(i).Append(":1").Append(isLast ? string.Empty : ";");
                }
                else
                {
                    var (s1, e1) = BuildNestedSelector(item.Selector);
                    s1 += ".u" + i;
                    e1 += ".u" + i;

                    string selSrc, selExp, beforeBrace;
                    if (item.IsList)
                    {
                        var (s2, e2) = BuildNestedSelector(item.Selector2);
                        s2 += ".v" + i;
                        e2 += ".v" + i;

                        // Optional whitespace around the comma (5.1) collapses to a bare comma (8.2).
                        selSrc = s1 + WsAt(ws, 1) + "," + WsAt(ws, 2) + s2;
                        selExp = e1 + "," + e2;
                        beforeBrace = WsAt(ws, 3);
                    }
                    else
                    {
                        selSrc = s1;
                        selExp = e1;
                        beforeBrace = WsAt(ws, 1);
                    }

                    // No leading separator before the nested rule (8.6), whitespace around its
                    // braces and inner colon removed, and no separator after its closing brace.
                    src.Append(WsAt(ws, 0)).Append(selSrc).Append(beforeBrace).Append('{')
                       .Append(WsAt(ws, 5)).Append('q').Append(i)
                       .Append(WsAt(ws, 6)).Append(':').Append(WsAt(ws, 7)).Append('1')
                       .Append(WsAt(ws, 8)).Append('}');

                    exp.Append(selExp).Append('{').Append('q').Append(i).Append(":1").Append('}');
                }
            }

            src.Append('}');
            exp.Append('}');
            return (src.ToString(), exp.ToString());
        }

        // ------------------------------------------------------------------
        // Feature: css-nesting, Property 11: Minified output whitespace invariants -
        // For any parent rule containing nested rules, minified output contains no whitespace
        // immediately inside or outside braces, none around the declaration colon or
        // selector-list commas, no whitespace around >/+/~ combinators, and exactly one space
        // for the descendant combinator; a declaration following a nested rule's closing brace
        // has no separator inserted.
        // Validates: Requirements 8.1, 8.2, 8.3, 8.6
        // ------------------------------------------------------------------
        [Test]
        public void Property11_MinifiedOutputWhitespaceInvariants()
        {
            var property = Prop.ForAll(Arb.From(WsParentRuleGen()), (WsParentRule rule) =>
            {
                var (source, expected) = BuildWhitespaceParentRule(rule);

                var result = Uglify.Css(source);

                // Exact equality of the whole minified rule confirms every whitespace invariant:
                // the tight expected form removed all injected whitespace and inserted no
                // separator after any nested rule's closing brace.
                return (!result.HasErrors && result.Code == expected)
                    .Label($"source=<{source}> code=<{result.Code}> expected=<{expected}> hasErrors={result.HasErrors}");
            });

            RunProperty(property);
        }

        // ==================================================================
        // Task 7.3 - at-rule block error handling and containment on output.
        // Example-based tests confirming that (a) nested rules stay contained
        // within the at-rule braces in both minified and pretty output
        // (Requirement 7.4), and (b) malformed or EOF-truncated at-rule blocks
        // containing nested rules are reported as parse errors (Requirement 7.6).
        // ==================================================================

        // Each case wraps a style rule that itself contains a nested rule inside a
        // grouping/conditional at-rule. The nested selector uses a unique, minifier-stable
        // marker (".nmark") so its brace-nesting depth in the output can be located exactly.
        static readonly (string name, string source)[] AtRuleContainmentCases =
        {
            ("media",    "@media screen{.p{color:red;.nmark{color:red}}}"),
            ("supports", "@supports (display:grid){.p{color:red;.nmark{color:red}}}"),
            ("layer",    "@layer base{.p{color:red;.nmark{color:red}}}"),
            ("scope",    "@scope (.p) to (.q){.p{color:red;.nmark{color:red}}}"),
        };

        // ------------------------------------------------------------------
        // Requirement 7.4: nested rules inside an @media/@supports/@layer/@scope
        // block remain contained within that at-rule's braces in BOTH minified and
        // pretty output. The nested selector sits at brace depth 2 (inside the
        // at-rule block at depth 1 and inside its parent style rule at depth 2),
        // and the output braces stay balanced.
        // ------------------------------------------------------------------
        [Test]
        public void Task73_AtRuleContainmentPreservedMinifiedAndPretty()
        {
            foreach (var (name, source) in AtRuleContainmentCases)
            {
                var minified = Uglify.Css(source);
                var pretty = Uglify.Css(source, new CssSettings { OutputMode = OutputMode.MultipleLines });

                Assert.That(minified.HasErrors, Is.False, $"[{name}] minified reported errors for <{source}>");
                Assert.That(pretty.HasErrors, Is.False, $"[{name}] pretty reported errors for <{source}>");

                // The nested rule must still be present and nested two braces deep,
                // i.e. contained within the at-rule block (depth 1) and its parent rule (depth 2).
                Assert.That(BraceDepthAtToken(minified.Code, ".nmark"), Is.EqualTo(2),
                    $"[{name}] nested rule not contained in minified output <{minified.Code}>");
                Assert.That(BraceDepthAtToken(pretty.Code, ".nmark"), Is.EqualTo(2),
                    $"[{name}] nested rule not contained in pretty output <{pretty.Code}>");

                // Braces stay balanced and reach at least depth 3 (at-rule > parent > nested block).
                Assert.That(minified.Code.Count(c => c == '}'), Is.EqualTo(minified.Code.Count(c => c == '{')),
                    $"[{name}] unbalanced braces in minified output <{minified.Code}>");
                Assert.That(MaxBraceDepth(minified.Code), Is.GreaterThanOrEqualTo(3),
                    $"[{name}] nested block not opened inside at-rule in minified output <{minified.Code}>");
            }
        }

        // ------------------------------------------------------------------
        // Requirement 7.6: reaching end-of-input while an at-rule block that
        // contains a nested rule is still unterminated by a closing brace must be
        // reported as a parse error (not silently emitted).
        // ------------------------------------------------------------------
        [Test]
        public void Task73_UnterminatedAtRuleBlockWithNestedRuleAtEof_ReportsError()
        {
            // Every source is missing the closing brace(s) of the at-rule block that
            // contains a nested rule, so parsing hits EOF before the block is closed.
            var truncatedSources = new[]
            {
                "@media screen{.p{color:red;&:hover{color:red}}",
                "@supports (display:grid){.p{color:red;&:hover{color:red}}",
                "@layer base{.p{color:red;&:hover{color:red}}",
                "@scope (.p) to (.q){.p{color:red;&:hover{color:red}}",
            };

            foreach (var source in truncatedSources)
            {
                var result = Uglify.Css(source);
                Assert.That(result.HasErrors, Is.True,
                    $"expected a parse error for the EOF-truncated at-rule block <{source}> but got <{result.Code}>");
            }
        }

        // ------------------------------------------------------------------
        // Requirement 7.6: a malformed construct inside an at-rule block that
        // contains nested rules must be reported as a parse error.
        // ------------------------------------------------------------------
        [Test]
        public void Task73_MalformedConstructInsideAtRuleBlock_ReportsError()
        {
            // A stray, unbalanced ')' after a nested rule is neither a valid declaration,
            // a valid style rule, nor a valid nested at-rule inside the block.
            var malformedSources = new[]
            {
                "@media screen{.p{color:red;&:hover{color:red}}) }",
                "@supports (display:grid){.p{color:red;&:hover{color:red}}) }",
                "@layer base{.p{color:red;&:hover{color:red}}) }",
                "@scope (.p) to (.q){.p{color:red;&:hover{color:red}}) }",
            };

            foreach (var source in malformedSources)
            {
                var result = Uglify.Css(source);
                Assert.That(result.HasErrors, Is.True,
                    $"expected a parse error for the malformed at-rule block <{source}> but got <{result.Code}>");
            }
        }

        // ==================================================================
        // Task 8.1 - minified/pretty output whitespace + indentation for
        // nested rules. Example-based tests that lock in:
        //   * no whitespace immediately inside or outside braces (8.1/8.2)
        //   * no whitespace around the declaration colon or selector-list
        //     commas (8.2)
        //   * no whitespace around >/+/~ combinators, exactly one space for
        //     the descendant combinator (8.3)
        //   * no separator inserted between a nested rule's closing brace and
        //     the item that follows it (8.1/8.6)
        //   * pretty output indents each nested rule one level deeper than its
        //     parent rule using the existing indentation unit, with the nested
        //     rule's own declarations one level deeper again (8.7)
        // The dedicated property tests for these invariants are tasks 8.3/8.4.
        // ==================================================================

        // Each case is (source, expected-minified). Property values are already in their
        // minified form (#f00 / #00f / 0) so the assertions isolate whitespace behavior and
        // are not perturbed by value minification.
        static readonly (string source, string expected)[] MinifiedWhitespaceCases =
        {
            // Requirement 8.1 / 8.2: no whitespace inside/outside the parent or nested braces;
            // Requirement 8.3: descendant combinator keeps exactly one space.
            ("  .a  {  color : #f00 ;  & b  {  color : #00f  }  }  ", ".a{color:#f00;& b{color:#00f}}"),

            // Requirement 8.3: child / next-sibling / subsequent-sibling combinators lose all
            // surrounding whitespace.
            (".a{ & > .b { color:#f00 } }", ".a{&>.b{color:#f00}}"),
            (".a{ & + .b { color:#f00 } }", ".a{&+.b{color:#f00}}"),
            (".a{ & ~ .b { color:#f00 } }", ".a{&~.b{color:#f00}}"),

            // Requirement 8.3: descendant combinator collapses runs of whitespace to one space.
            (".a{ &      b { color:#f00 } }", ".a{& b{color:#f00}}"),

            // Requirement 8.2: no whitespace around selector-list commas.
            (".a{ &:hover , &:focus { color:#f00 } }", ".a{&:hover,&:focus{color:#f00}}"),

            // Requirement 8.1 / 8.6: a declaration immediately after a nested rule's closing
            // brace has no separator inserted before it.
            (".a{.b{color:#f00}color:#00f}", ".a{.b{color:#f00}color:#00f}"),

            // Requirement 8.6: declaration -> nested rule -> declaration all abut with only the
            // source-required ';' separators (none inserted around the nested rule braces).
            (".a{margin:0;.b{color:#f00}padding:0}", ".a{margin:0;.b{color:#f00}padding:0}"),

            // Requirement 8.6: two nested rules back to back have nothing inserted between the
            // first rule's closing brace and the second rule's selector.
            (".a{.b{color:#f00}.c{color:#00f}}", ".a{.b{color:#f00}.c{color:#00f}}"),

            // A relative nested selector (implied leading &) keeps its bare leading form with no
            // inserted '&' and no surrounding brace whitespace.
            (".a{ > .b { color:#f00 } }", ".a{>.b{color:#f00}}"),
        };

        // ------------------------------------------------------------------
        // Requirements 8.1, 8.2, 8.3, 8.6: minified output whitespace for nested rules.
        // ------------------------------------------------------------------
        [Test]
        public void Task81_MinifiedWhitespaceForNestedRules()
        {
            foreach (var (source, expected) in MinifiedWhitespaceCases)
            {
                var result = Uglify.Css(source);

                Assert.That(result.HasErrors, Is.False, $"unexpected errors for <{source}>");
                Assert.That(result.Code, Is.EqualTo(expected), $"minified output mismatch for <{source}>");
            }
        }

        // ------------------------------------------------------------------
        // Requirement 8.6: no separator character is inserted between a nested rule's closing
        // brace and the declaration that follows it. Verified structurally so the assertion does
        // not depend on the exact following-declaration text: the character immediately after the
        // nested rule's '}' is the first character of the next declaration, never a ';' or space.
        // ------------------------------------------------------------------
        [Test]
        public void Task81_NoSeparatorAfterNestedRuleClosingBrace()
        {
            var result = Uglify.Css(".a{.b{color:#f00}color:#00f}");
            Assert.That(result.HasErrors, Is.False);

            var code = result.Code;
            // Locate the nested rule's closing brace: it is the '}' that precedes "color".
            var declIndex = code.IndexOf("color:#00f", System.StringComparison.Ordinal);
            Assert.That(declIndex, Is.GreaterThan(0), $"following declaration missing in <{code}>");

            // The character immediately before the following declaration must be the nested
            // rule's closing brace, with nothing (no ';', no whitespace) inserted between them.
            Assert.That(code[declIndex - 1], Is.EqualTo('}'),
                $"a separator was inserted after the nested rule's closing brace in <{code}>");
        }

        // Normalizes line endings so pretty-output assertions are stable regardless of the
        // configured LineTerminator (default "\n").
        static string NormalizeNewLines(string s)
            => (s ?? string.Empty).Replace("\r\n", "\n").Replace("\r", "\n");

        // ------------------------------------------------------------------
        // Requirement 8.7: pretty output indents each nested rule one indentation level deeper
        // than its parent rule (i.e. at the same level as the parent's declarations), using the
        // existing four-space indentation unit, and the nested rule's own declarations are
        // indented one level deeper again.
        // ------------------------------------------------------------------
        [Test]
        public void Task81_PrettyOutputIndentsNestedRules()
        {
            var source = ".a{color:#f00;& b{color:#00f}}";
            var pretty = Uglify.Css(source, new CssSettings { OutputMode = OutputMode.MultipleLines });

            Assert.That(pretty.HasErrors, Is.False);

            // The parent's declaration and the nested rule's selector share indentation level 1
            // (four spaces); the nested rule's declaration sits at level 2 (eight spaces).
            var expected = string.Join("\n",
                ".a",
                "{",
                "    color: #f00;",
                "    & b",
                "    {",
                "        color: #00f",
                "    }",
                "}");

            Assert.That(NormalizeNewLines(pretty.Code), Is.EqualTo(expected), $"pretty output mismatch:\n<{pretty.Code}>");
        }

        // ------------------------------------------------------------------
        // Requirement 8.7: indentation nests one additional level per nesting depth, using the
        // same indentation unit as non-nested rules, for three explicit levels.
        // ------------------------------------------------------------------
        [Test]
        public void Task81_PrettyOutputIndentsDeepNesting()
        {
            var source = ".a{.b{.c{color:#f00}}}";
            var pretty = Uglify.Css(source, new CssSettings { OutputMode = OutputMode.MultipleLines });

            Assert.That(pretty.HasErrors, Is.False);

            var expected = string.Join("\n",
                ".a",
                "{",
                "    .b",
                "    {",
                "        .c",
                "        {",
                "            color: #f00",
                "        }",
                "    }",
                "}");

            Assert.That(NormalizeNewLines(pretty.Code), Is.EqualTo(expected), $"pretty output mismatch:\n<{pretty.Code}>");
        }

        // ==================================================================
        // Task 8.2: RemoveEmptyBlocks support for nested rules
        // Requirement 8.4: an empty nested rule is omitted from the output.
        // Requirement 8.5: a parent left with an empty block after nested-rule removal is
        //                  itself omitted (cascading to arbitrary depth).
        // ==================================================================

        // 8.4: an empty nested rule is dropped while its non-empty parent (and the parent's own
        // declaration) is kept, and the semicolon that separated the declaration from the dropped
        // nested rule is not left dangling before the closing brace.
        [Test]
        public void Task82_EmptyNestedRuleIsRemoved_ParentKept()
        {
            var result = Uglify.Css(".a{color:red;.b{}}");
            Assert.That(result.HasErrors, Is.False);
            Assert.That(result.Code, Is.EqualTo(".a{color:#f00}"), $"actual <{result.Code}>");
        }

        // 8.5: a parent whose only content is an empty nested rule becomes empty and is dropped
        // entirely, leaving no output.
        [Test]
        public void Task82_EmptyNestedRuleLeavesParentEmpty_ParentRemoved()
        {
            var result = Uglify.Css(".a{.b{}}");
            Assert.That(result.HasErrors, Is.False);
            Assert.That(result.Code, Is.EqualTo(string.Empty), $"actual <{result.Code}>");
        }

        // 8.5: multiple empty nested rules all drop, leaving the parent empty and thus dropped.
        [Test]
        public void Task82_MultipleEmptyNestedRulesRemoveParent()
        {
            var result = Uglify.Css(".a{.b{}.c{}}");
            Assert.That(result.HasErrors, Is.False);
            Assert.That(result.Code, Is.EqualTo(string.Empty), $"actual <{result.Code}>");
        }

        // 8.5: emptiness cascades through arbitrary depth - an innermost empty rule drops its
        // parent, which drops its parent, and so on up to the top-level rule.
        [Test]
        public void Task82_EmptinessCascadesThroughDepth()
        {
            var result = Uglify.Css(".a{.b{.c{}}}");
            Assert.That(result.HasErrors, Is.False);
            Assert.That(result.Code, Is.EqualTo(string.Empty), $"actual <{result.Code}>");
        }

        // 8.4/8.5: an empty nested rule whose selector uses '&' is dropped, and since it is the
        // parent's only content the parent is dropped too.
        [Test]
        public void Task82_EmptyAmpersandNestedRuleRemovesParent()
        {
            var result = Uglify.Css(".a{&:hover{}}");
            Assert.That(result.HasErrors, Is.False);
            Assert.That(result.Code, Is.EqualTo(string.Empty), $"actual <{result.Code}>");
        }

        // 8.4/8.5: only the empty sibling is dropped; the parent survives because it still
        // contains a non-empty nested rule.
        [Test]
        public void Task82_NonEmptySiblingKeepsParent()
        {
            var result = Uglify.Css(".a{.b{color:red}.c{}}");
            Assert.That(result.HasErrors, Is.False);
            Assert.That(result.Code, Is.EqualTo(".a{.b{color:#f00}}"), $"actual <{result.Code}>");
        }

        // 8.5 (pretty output): the same cascading removal applies in multi-line output mode.
        [Test]
        public void Task82_EmptyParentRemovedInPrettyOutput()
        {
            var pretty = Uglify.Css(".a{.b{}}", new CssSettings { OutputMode = OutputMode.MultipleLines });
            Assert.That(pretty.HasErrors, Is.False);
            Assert.That(NormalizeNewLines(pretty.Code), Is.EqualTo(string.Empty), $"actual <{pretty.Code}>");
        }

        // 8.4/8.5 (pretty output): an empty nested rule is dropped and its non-empty parent is
        // kept, with no dangling semicolon after the surviving declaration.
        [Test]
        public void Task82_EmptyNestedRuleRemovedParentKeptInPrettyOutput()
        {
            var pretty = Uglify.Css(".a{color:red;.b{}}", new CssSettings { OutputMode = OutputMode.MultipleLines });
            Assert.That(pretty.HasErrors, Is.False);

            var expected = string.Join("\n",
                ".a",
                "{",
                "    color: #f00",
                "}");
            Assert.That(NormalizeNewLines(pretty.Code), Is.EqualTo(expected), $"actual <{pretty.Code}>");
        }

        // With RemoveEmptyBlocks disabled, empty nested rules (and the parents that contain only
        // them) are preserved verbatim - the removal behavior is entirely opt-in.
        [Test]
        public void Task82_RemoveEmptyBlocksDisabledLeavesEmptyBlocksIntact()
        {
            var settings = new CssSettings { RemoveEmptyBlocks = false };

            var onlyEmpty = Uglify.Css(".a{.b{}}", settings);
            Assert.That(onlyEmpty.HasErrors, Is.False);
            Assert.That(onlyEmpty.Code, Is.EqualTo(".a{.b{}}"), $"actual <{onlyEmpty.Code}>");

            var mixed = Uglify.Css(".a{color:red;.b{}}", settings);
            Assert.That(mixed.HasErrors, Is.False);
            Assert.That(mixed.Code, Is.EqualTo(".a{color:#f00;.b{}}"), $"actual <{mixed.Code}>");
        }

        // ---- Model + generator for empty-block removal (Property 12) ----

        // One rule node in a generated stylesheet tree. Every node owns a unique integer id
        // (assigned in a pre-order walk) so its selector (".s{Id}") and its optional declaration
        // ("p{Id}:1") are globally unique and are never merged or de-duplicated by the minifier,
        // keeping the computed expected output exact and unambiguous. A node is "effectively
        // empty" (and therefore removed under RemoveEmptyBlocks) when it has no declaration and
        // all of its children are effectively empty.
        sealed class EbNode
        {
            public int Id;
            public bool HasDeclaration;
            public List<EbNode> Children;
        }

        // A raw generated shape (no ids yet): whether the rule carries its own declaration and
        // its child rules. Ids are assigned in a second pass so the same tree drives both the
        // source text and the computed expectation.
        sealed class EbShape
        {
            public bool HasDeclaration;
            public List<EbShape> Children;
        }

        // Generates a rule shape up to <paramref name="depth"/> further nesting levels. Children
        // counts are capped so generated stylesheets stay well below the line-break threshold.
        static Gen<EbShape> EbShapeGen(int depth)
        {
            var childrenGen = depth <= 0
                ? Gen.Constant(new List<EbShape>())
                : Gen.ListOf(EbShapeGen(depth - 1)).Select(l => l.Take(3).ToList());

            return from hasDecl in Gen.Elements(new[] { false, true })
                   from children in childrenGen
                   select new EbShape { HasDeclaration = hasDecl, Children = children };
        }

        // Generates a small forest of top-level rules nested up to three levels deep, mixing
        // empty rules, non-empty rules, and declarations at varying depths.
        static Gen<List<EbShape>> EbForestGen()
        {
            return from depth in Gen.Choose(1, 3)
                   from forest in Gen.NonEmptyListOf(EbShapeGen(depth))
                   select forest.Take(4).ToList();
        }

        // Assigns a unique id to every node in the forest via a pre-order walk (parent before
        // its children), converting raw shapes into id-bearing nodes.
        static List<EbNode> AssignIds(List<EbShape> forest)
        {
            var next = 0;
            EbNode Convert(EbShape shape)
            {
                var node = new EbNode { Id = next++, HasDeclaration = shape.HasDeclaration };
                node.Children = shape.Children.Select(Convert).ToList();
                return node;
            }
            return forest.Select(Convert).ToList();
        }

        // Renders the source text of a single rule: ".s{Id}{" then its own declaration (with a
        // trailing ';' when present) followed by each child rule's source, then the closing '}'.
        static void AppendNodeSource(StringBuilder sb, EbNode node)
        {
            sb.Append(".s").Append(node.Id).Append('{');
            if (node.HasDeclaration)
                sb.Append('p').Append(node.Id).Append(":1;");
            foreach (var child in node.Children)
                AppendNodeSource(sb, child);
            sb.Append('}');
        }

        // A node survives RemoveEmptyBlocks iff it has its own declaration or at least one of its
        // children survives.
        static bool EbSurvives(EbNode node)
            => node.HasDeclaration || node.Children.Any(EbSurvives);

        // Computes the exact minified output for a surviving node, pruning effectively-empty
        // descendants. The block body lists the node's own declaration first (matching source
        // order) followed by its surviving children; a ';' separator follows the declaration only
        // when another surviving item follows it, so a declaration left last after pruning drops
        // its trailing ';' (no dangling separator). Returns null when the node is removed.
        static string EbExpected(EbNode node)
        {
            if (!EbSurvives(node))
                return null;

            var survivingChildren = node.Children.Where(EbSurvives).ToList();
            var itemCount = (node.HasDeclaration ? 1 : 0) + survivingChildren.Count;

            var sb = new StringBuilder();
            sb.Append(".s").Append(node.Id).Append('{');

            var emitted = 0;
            if (node.HasDeclaration)
            {
                sb.Append('p').Append(node.Id).Append(":1");
                emitted++;
                if (emitted < itemCount)
                    sb.Append(';');
            }
            foreach (var child in survivingChildren)
            {
                sb.Append(EbExpected(child));
                emitted++;
            }

            sb.Append('}');
            return sb.ToString();
        }

        // ------------------------------------------------------------------
        // Feature: css-nesting, Property 12: Empty-block removal -
        // For any stylesheet, when RemoveEmptyBlocks is enabled, any nested rule whose block
        // becomes empty is omitted, and any parent rule left with an empty block after such
        // removal is itself omitted.
        // Validates: Requirements 8.4, 8.5
        // ------------------------------------------------------------------
        [Test]
        public void Property12_EmptyBlockRemoval()
        {
            var property = Prop.ForAll(Arb.From(EbForestGen()), (List<EbShape> shapes) =>
            {
                var forest = AssignIds(shapes);

                // Source: a forest of rules mixing empty rules, non-empty rules, and declarations
                // at varying depths.
                var src = new StringBuilder();
                foreach (var node in forest)
                    AppendNodeSource(src, node);
                var source = src.ToString();

                // Expected: prune every effectively-empty subtree (a rule with no declaration and
                // only effectively-empty children), cascading up so a parent left empty is dropped.
                var exp = new StringBuilder();
                foreach (var node in forest)
                    exp.Append(EbExpected(node) ?? string.Empty);
                var expected = exp.ToString();

                // RemoveEmptyBlocks is enabled by default; state it explicitly for clarity.
                var result = Uglify.Css(source, new CssSettings { RemoveEmptyBlocks = true });
                var code = result.Code ?? string.Empty;

                // Exact equality confirms every empty nested rule was omitted (8.4) and every
                // parent left empty after removal was itself omitted (8.5); the surviving rules
                // and declarations remain in source order. The output must also contain no empty
                // "{}" block, which is the direct observable signature of 8.4/8.5.
                var matches = code == expected;
                var noEmptyBlocks = !code.Contains("{}");

                return (!result.HasErrors && matches && noEmptyBlocks)
                    .Label($"source=<{source}> code=<{code}> expected=<{expected}> " +
                           $"hasErrors={result.HasErrors} noEmptyBlocks={noEmptyBlocks}");
            });

            RunProperty(property);
        }

        // ---- Model + generator for at-rule containment (Property 10) ----

        // At-rule preludes that open a block whose body holds a style rule with nested rules.
        // Each is minifier-stable and reuses the exact forms exercised by the Task 7.3
        // example-based containment tests. None contains a brace or the ".nmark" marker, so the
        // marker's brace-nesting depth in the output is unambiguous.
        static readonly string[] AtRulePreludes =
        {
            "@media screen",
            "@supports (display:grid)",
            "@layer base",
            "@scope (.p) to (.q)",
        };

        // Returns true when <paramref name="s"/> has an equal number of '{' and '}' characters,
        // i.e. its braces are balanced (a correctly-contained output never leaks a nested rule
        // outside its enclosing at-rule/parent braces).
        static bool BracesBalanced(string s)
            => s != null && s.Count(c => c == '{') == s.Count(c => c == '}');

        // Produces the source of a parent style rule - whose block contains generator-driven
        // nested rules (via BlockBodySourceGen) plus a guaranteed unique ".nmark" nested rule -
        // wrapped inside a randomly chosen at-rule block. The parent selector (from
        // ParentSelectors) contains no '&' and no ".nmark", and the ".nmark" rule is a direct
        // child of the parent, so in any correctly-contained output the marker sits exactly two
        // braces deep: inside the at-rule block (depth 1) and inside its parent rule (depth 2).
        // The generated parent body items are all balanced and end before the ".nmark" child, so
        // they never change the marker's depth while still exercising real nested content.
        static Gen<string> AtRuleContainmentGen()
        {
            return from prelude in Gen.Elements(AtRulePreludes)
                   from parent in Gen.Elements(ParentSelectors)
                   from depth in Gen.Choose(0, 2)
                   from parentBody in BlockBodySourceGen(depth)
                   select prelude + "{" + parent + "{" + parentBody + ".nmark{color:red}" + "}}";
        }

        // ------------------------------------------------------------------
        // Feature: css-nesting, Property 10: At-rule containment is preserved -
        // For any nested rules contained within an @media, @supports, @layer, or @scope block,
        // the output keeps those nested rules inside the braces of that at-rule block in both
        // minified and pretty output.
        // Validates: Requirements 7.1, 7.2, 7.3, 7.4, 7.5
        // ------------------------------------------------------------------
        [Test]
        public void Property10_AtRuleContainmentIsPreserved()
        {
            var property = Prop.ForAll(Arb.From(AtRuleContainmentGen()), (string source) =>
            {
                var minified = Uglify.Css(source);
                var pretty = Uglify.Css(source, new CssSettings { OutputMode = OutputMode.MultipleLines });

                // The nested ".nmark" rule is a direct child of the parent style rule, which is
                // itself directly inside the at-rule block, so a correctly-contained marker sits
                // exactly two braces deep in both output modes; braces must also stay balanced.
                const int expectedDepth = 2;

                var minDepth = BraceDepthAtToken(minified.Code, ".nmark");
                var prettyDepth = BraceDepthAtToken(pretty.Code, ".nmark");

                var minifiedContained = !minified.HasErrors
                    && minDepth == expectedDepth
                    && BracesBalanced(minified.Code);

                var prettyContained = !pretty.HasErrors
                    && prettyDepth == expectedDepth
                    && BracesBalanced(pretty.Code);

                return (minifiedContained && prettyContained)
                    .Label($"source=<{source}> minified=<{minified.Code}> pretty=<{pretty.Code}> " +
                           $"minDepth={minDepth} prettyDepth={prettyDepth} " +
                           $"minErrors={minified.HasErrors} prettyErrors={pretty.HasErrors}");
            });

            RunProperty(property);
        }

        // ==================================================================
        // Task 9.1 - Example-based unit tests for canonical nesting cases.
        //
        // These input -> expected-output tests lock in the exact minified and
        // pretty forms of the canonical nesting patterns from the CSS Nesting
        // spec. They complement the property tests (Property1..12) and the
        // Task73/Task81/Task82 example tests by making the individual canonical
        // forms explicit and traceable. All expected property values are given
        // in already-minified form (#f00 / #00f / 0) so the assertions isolate
        // structural/whitespace behavior and are not perturbed by value
        // minification (e.g. color:red -> color:#f00).
        //
        // Requirements: 3.1, 3.2, 3.3, 3.4, 3.5, 4.1, 4.2, 5.1, 6.2,
        //               7.1, 7.2, 8.4, 8.5, 8.7
        // ==================================================================

        // ------------------------------------------------------------------
        // Requirement 3.1: a standalone '&' nested selector is emitted as a
        // single '&' with nothing added.
        // ------------------------------------------------------------------
        [Test]
        public void Task91_StandaloneAmpersand()
        {
            var result = Uglify.Css(".a{&{color:#f00}}");
            Assert.That(result.HasErrors, Is.False);
            Assert.That(result.Code, Is.EqualTo(".a{&{color:#f00}}"), $"actual <{result.Code}>");
        }

        // ------------------------------------------------------------------
        // Requirement 3.2: '&' joined with a compound selector (&.bar) is
        // emitted with zero whitespace between '&' and the compound selector.
        // ------------------------------------------------------------------
        [Test]
        public void Task91_AmpersandJoinedCompound()
        {
            var result = Uglify.Css(".a{&.bar{color:#f00}}");
            Assert.That(result.HasErrors, Is.False);
            Assert.That(result.Code, Is.EqualTo(".a{&.bar{color:#f00}}"), $"actual <{result.Code}>");
        }

        // ------------------------------------------------------------------
        // Requirement 3.3: '&' repeated with a combinator (& + &) keeps every
        // '&' in source order with the same combinator; minified drops the
        // whitespace around the '+' combinator.
        // ------------------------------------------------------------------
        [Test]
        public void Task91_AmpersandRepeatedWithCombinator()
        {
            var result = Uglify.Css(".a{& + &{color:#f00}}");
            Assert.That(result.HasErrors, Is.False);
            Assert.That(result.Code, Is.EqualTo(".a{&+&{color:#f00}}"), $"actual <{result.Code}>");
        }

        // ------------------------------------------------------------------
        // Requirement 3.4: '&' placed after another selector (.parent &) keeps
        // the trailing '&' and preserves the descendant combinator as one space.
        // ------------------------------------------------------------------
        [Test]
        public void Task91_AmpersandAfterSelectorDescendant()
        {
            var result = Uglify.Css(".a{.parent &{color:#f00}}");
            Assert.That(result.HasErrors, Is.False);
            Assert.That(result.Code, Is.EqualTo(".a{.parent &{color:#f00}}"), $"actual <{result.Code}>");
        }

        // ------------------------------------------------------------------
        // Requirement 3.5: two consecutive nesting selectors (&&) are emitted
        // adjacently with zero whitespace between them.
        // ------------------------------------------------------------------
        [Test]
        public void Task91_DoubledAmpersand()
        {
            var result = Uglify.Css(".a{&&{color:#f00}}");
            Assert.That(result.HasErrors, Is.False);
            Assert.That(result.Code, Is.EqualTo(".a{&&{color:#f00}}"), $"actual <{result.Code}>");
        }

        // ------------------------------------------------------------------
        // Requirement 4.1: relative nested selectors that begin with a child
        // (>), next-sibling (+), or subsequent-sibling (~) combinator keep the
        // leading combinator and never gain an explicit '&'; minified drops the
        // whitespace after the combinator.
        // ------------------------------------------------------------------
        [Test]
        public void Task91_RelativeLeadingChildCombinator()
        {
            var result = Uglify.Css(".a{> .baz{color:#f00}}");
            Assert.That(result.HasErrors, Is.False);
            Assert.That(result.Code, Is.EqualTo(".a{>.baz{color:#f00}}"), $"actual <{result.Code}>");
        }

        [Test]
        public void Task91_RelativeLeadingNextSiblingCombinator()
        {
            var result = Uglify.Css(".a{+ .bar{color:#f00}}");
            Assert.That(result.HasErrors, Is.False);
            Assert.That(result.Code, Is.EqualTo(".a{+.bar{color:#f00}}"), $"actual <{result.Code}>");
        }

        [Test]
        public void Task91_RelativeLeadingSubsequentSiblingCombinator()
        {
            var result = Uglify.Css(".a{~ .qux{color:#f00}}");
            Assert.That(result.HasErrors, Is.False);
            Assert.That(result.Code, Is.EqualTo(".a{~.qux{color:#f00}}"), $"actual <{result.Code}>");
        }

        // ------------------------------------------------------------------
        // Requirement 4.2: a bare compound nested selector (.child) is emitted
        // unchanged with no explicit '&' inserted.
        // ------------------------------------------------------------------
        [Test]
        public void Task91_RelativeBareCompoundSelector()
        {
            var result = Uglify.Css(".a{.child{color:#f00}}");
            Assert.That(result.HasErrors, Is.False);
            Assert.That(result.Code, Is.EqualTo(".a{.child{color:#f00}}"), $"actual <{result.Code}>");
        }

        // ------------------------------------------------------------------
        // Requirement 5.1: a nested selector list (&:hover, &:focus) sharing one
        // block is parsed and, in minified output, joined by a single bare comma
        // with no surrounding whitespace.
        // ------------------------------------------------------------------
        [Test]
        public void Task91_NestedSelectorListMinified()
        {
            var result = Uglify.Css(".a{&:hover, &:focus{color:#f00}}");
            Assert.That(result.HasErrors, Is.False);
            Assert.That(result.Code, Is.EqualTo(".a{&:hover,&:focus{color:#f00}}"), $"actual <{result.Code}>");
        }

        // Requirement 5.1 / 8.7 (pretty): the nested selector list uses the same
        // "comma + space" selector-list formatting as non-nested rules and is
        // indented one level deeper than the parent.
        [Test]
        public void Task91_NestedSelectorListPretty()
        {
            var pretty = Uglify.Css(".a{&:hover, &:focus{color:#f00}}",
                new CssSettings { OutputMode = OutputMode.MultipleLines });
            Assert.That(pretty.HasErrors, Is.False);

            var expected = string.Join("\n",
                ".a",
                "{",
                "    &:hover, &:focus",
                "    {",
                "        color: #f00",
                "    }",
                "}");
            Assert.That(NormalizeNewLines(pretty.Code), Is.EqualTo(expected), $"actual <{pretty.Code}>");
        }

        // ------------------------------------------------------------------
        // Requirements 2.2 / 2.3 / 8.6: a mixture of declarations and nested
        // rules is preserved in source order, and a declaration that immediately
        // follows a nested rule's closing brace gets no separator inserted.
        // ------------------------------------------------------------------
        [Test]
        public void Task91_MixedDeclarationsAndNestedRulesInSourceOrder()
        {
            var result = Uglify.Css(".a{color:#f00;.b{margin:0}padding:0}");
            Assert.That(result.HasErrors, Is.False);
            Assert.That(result.Code, Is.EqualTo(".a{color:#f00;.b{margin:0}padding:0}"), $"actual <{result.Code}>");
        }

        // ------------------------------------------------------------------
        // Requirement 6.2: several explicit levels of nesting are all parsed and
        // preserved (four levels here: .a > .b > .c > .d).
        // ------------------------------------------------------------------
        [Test]
        public void Task91_ExplicitDeepNestingLevels()
        {
            var result = Uglify.Css(".a{.b{.c{.d{color:#f00}}}}");
            Assert.That(result.HasErrors, Is.False);
            Assert.That(result.Code, Is.EqualTo(".a{.b{.c{.d{color:#f00}}}}"), $"actual <{result.Code}>");
        }

        // ------------------------------------------------------------------
        // Requirements 7.1 / 7.2: nesting inside @media, @supports, @layer, and
        // @scope blocks parses and preserves the nested rule inside the at-rule.
        // Minified forms are locked in per at-rule.
        // ------------------------------------------------------------------
        [Test]
        public void Task91_NestingInsideMediaMinified()
        {
            var result = Uglify.Css("@media screen{.a{color:#f00;&:hover{color:#00f}}}");
            Assert.That(result.HasErrors, Is.False);
            Assert.That(result.Code, Is.EqualTo("@media screen{.a{color:#f00;&:hover{color:#00f}}}"), $"actual <{result.Code}>");
        }

        [Test]
        public void Task91_NestingInsideSupportsMinified()
        {
            var result = Uglify.Css("@supports (display:grid){.a{&:hover{color:#f00}}}");
            Assert.That(result.HasErrors, Is.False);
            Assert.That(result.Code, Is.EqualTo("@supports(display:grid){.a{&:hover{color:#f00}}}"), $"actual <{result.Code}>");
        }

        [Test]
        public void Task91_NestingInsideLayerMinified()
        {
            var result = Uglify.Css("@layer base{.a{&:hover{color:#f00}}}");
            Assert.That(result.HasErrors, Is.False);
            Assert.That(result.Code, Is.EqualTo("@layer base{.a{&:hover{color:#f00}}}"), $"actual <{result.Code}>");
        }

        [Test]
        public void Task91_NestingInsideScopeMinified()
        {
            var result = Uglify.Css("@scope (.a) to (.b){.a{&:hover{color:#f00}}}");
            Assert.That(result.HasErrors, Is.False);
            Assert.That(result.Code, Is.EqualTo("@scope(.a) to (.b){.a{&:hover{color:#f00}}}"), $"actual <{result.Code}>");
        }

        [Test]
        public void Task91_NestedScopePreludeAllowsAmpersandSelectors()
        {
            var result = Uglify.Css(".a{@scope (& > .scope) to (& .limit){& .child{color:red}}}");
            Assert.That(result.HasErrors, Is.False);
            Assert.That(result.Code, Is.EqualTo(".a{@scope(&>.scope) to (& .limit){& .child{color:#f00}}}"), $"actual <{result.Code}>");
        }

        [Test]
        public void Task91_NestingSelectorInsideIsPseudoList()
        {
            var result = Uglify.Css(".a{:is(.bar, &.baz){color:red}}");
            Assert.That(result.HasErrors, Is.False);
            Assert.That(result.Code, Is.EqualTo(".a{:is(.bar,&.baz){color:#f00}}"), $"actual <{result.Code}>");
        }

        [Test]
        public void Task91_TypeSelectorBeforeAmpersandRemainsValid()
        {
            var result = Uglify.Css(".a{div&{color:red}}");
            Assert.That(result.HasErrors, Is.False);
            Assert.That(result.Code, Is.EqualTo(".a{div&{color:#f00}}"), $"actual <{result.Code}>");
        }

        [Test]
        public void Task91_NestedLayerInsideLayerPreservesNestedSelector()
        {
            var result = Uglify.Css(".a{@layer base{@layer support{& body{color:red}}}}");
            Assert.That(result.HasErrors, Is.False);
            Assert.That(result.Code, Is.EqualTo(".a{@layer base{@layer support{& body{color:#f00}}}}"), $"actual <{result.Code}>");
        }

        [Test]
        public void Task91_NestedGroupRuleBodiesAllowDirectDeclarations()
        {
            var cases = new (string name, string source, string expected)[]
            {
                ("media", ".a{@media screen{color:red;display:block}}", ".a{@media screen{color:#f00;display:block}}"),
                ("supports", ".a{@supports (display:grid){color:red;display:grid}}", ".a{@supports(display:grid){color:#f00;display:grid}}"),
            };

            foreach (var (name, source, expected) in cases)
            {
                var result = Uglify.Css(source);
                Assert.That(result.HasErrors, Is.False, $"[{name}] actual <{result.Code}>");
                Assert.That(result.Code, Is.EqualTo(expected), $"[{name}] actual <{result.Code}>");
            }
        }

        [Test]
        public void Task91_PseudoElementParentListKeepsNestedAmpersandRule()
        {
            var result = Uglify.Css(".foo,.foo::before{color:red;&{background:blue}}");
            Assert.That(result.HasErrors, Is.False);
            Assert.That(result.Code, Is.EqualTo(".foo,.foo::before{color:#f00;&{background:#00f}}"), $"actual <{result.Code}>");
        }

        [Test]
        public void Task91_NestingSelectorInsideWhereAndNotPseudoLists()
        {
            var cases = new (string name, string source, string expected)[]
            {
                ("where", ".a{:where(.bar, &.baz){color:red}}", ".a{:where(.bar,&.baz){color:#f00}}"),
                ("not", ".a{:not(&.baz){color:red}}", ".a{:not(&.baz){color:#f00}}"),
            };

            foreach (var (name, source, expected) in cases)
            {
                var result = Uglify.Css(source);
                Assert.That(result.HasErrors, Is.False, $"[{name}] actual <{result.Code}>");
                Assert.That(result.Code, Is.EqualTo(expected), $"[{name}] actual <{result.Code}>");
            }
        }

        [Test]
        public void Task91_NestedScopePreludeSupportsAdditionalAmpersandVariants()
        {
            var cases = new (string name, string source, string expected)[]
            {
                ("scope-only", ".a{@scope (&){& .child{color:red}}}", ".a{@scope(&){& .child{color:#f00}}}"),
                ("scope-to", ".a{@scope (&) to (.limit){& .child{color:red}}}", ".a{@scope(&) to (.limit){& .child{color:#f00}}}"),
            };

            foreach (var (name, source, expected) in cases)
            {
                var result = Uglify.Css(source);
                Assert.That(result.HasErrors, Is.False, $"[{name}] actual <{result.Code}>");
                Assert.That(result.Code, Is.EqualTo(expected), $"[{name}] actual <{result.Code}>");
            }
        }

        [Test]
        public void Task91_TopLevelContainerRuleParses()
        {
            var result = Uglify.Css("@container card (width > 30rem){.a{color:red}}");
            Assert.That(result.HasErrors, Is.False);
            Assert.That(result.Code, Is.EqualTo("@container card (width>30rem){.a{color:#f00}}"), $"actual <{result.Code}>");
        }

        [Test]
        public void Task91_NestingInsideContainerMinified()
        {
            var result = Uglify.Css(".card{@container (width > 30rem){& .title{color:red}}}");
            Assert.That(result.HasErrors, Is.False);
            Assert.That(result.Code, Is.EqualTo(".card{@container (width>30rem){& .title{color:#f00}}}"), $"actual <{result.Code}>");
        }

        [Test]
        public void Task91_NestedContainerBodiesAllowDirectDeclarations()
        {
            var result = Uglify.Css(".card{@container sidebar (width > 30rem){color:red;display:block}}");
            Assert.That(result.HasErrors, Is.False);
            Assert.That(result.Code, Is.EqualTo(".card{@container sidebar (width>30rem){color:#f00;display:block}}"), $"actual <{result.Code}>");
        }

        [Test]
        public void Task91_ContainerPreludeSupportsLogicalAndStyleQueries()
        {
            var result = Uglify.Css("@container card (inline-size > 30rem) and style(color: green){.a{color:red}}");
            Assert.That(result.HasErrors, Is.False);
            Assert.That(result.Code, Is.EqualTo("@container card (inline-size>30rem) and style(color:green){.a{color:#f00}}"), $"actual <{result.Code}>");
        }

        [Test]
        public void Task91_ContainerBodyPreservesDeclarationsAndNestedRulesInSourceOrder()
        {
            var result = Uglify.Css(".card{@container sidebar ((width > 30rem) and (height > 20rem)){color:red;& .title{color:blue}display:block}}");
            Assert.That(result.HasErrors, Is.False);
            Assert.That(result.Code, Is.EqualTo(".card{@container sidebar ((width>30rem) and (height>20rem)){color:#f00;& .title{color:#00f}display:block}}"), $"actual <{result.Code}>");
        }

        [Test]
        public void Task91_TopLevelAmpersandSelectorParses()
        {
            var result = Uglify.Css("&{color:red}");
            Assert.That(result.HasErrors, Is.False);
            Assert.That(result.Code, Is.EqualTo("&{color:#f00}"), $"actual <{result.Code}>");
        }

        [Test]
        public void Task91_TopLevelAmpersandPseudoSelectorParses()
        {
            var result = Uglify.Css("&:hover{color:red}");
            Assert.That(result.HasErrors, Is.False);
            Assert.That(result.Code, Is.EqualTo("&:hover{color:#f00}"), $"actual <{result.Code}>");
        }

        [Test]
        public void Task91_TopLevelAmpersandInsideSelectorListPseudoParses()
        {
            var result = Uglify.Css(":is(&,.foo){color:red}");
            Assert.That(result.HasErrors, Is.False);
            Assert.That(result.Code, Is.EqualTo(":is(&,.foo){color:#f00}"), $"actual <{result.Code}>");
        }

        // Requirements 7.1 / 8.7 (pretty): a nested rule inside an @media block is
        // indented one level deeper than its parent style rule, which is itself
        // indented one level inside the at-rule block.
        [Test]
        public void Task91_NestingInsideMediaPretty()
        {
            var pretty = Uglify.Css("@media screen{.a{&:hover{color:#f00}}}",
                new CssSettings { OutputMode = OutputMode.MultipleLines });
            Assert.That(pretty.HasErrors, Is.False);

            var expected = string.Join("\n",
                "@media screen",
                "{",
                "    .a",
                "    {",
                "        &:hover",
                "        {",
                "            color: #f00",
                "        }",
                "    }",
                "}");
            Assert.That(NormalizeNewLines(pretty.Code), Is.EqualTo(expected), $"actual <{pretty.Code}>");
        }

        // Requirements 7.2 / 8.7 (pretty): the same one-level-per-depth
        // indentation applies inside an @layer block.
        [Test]
        public void Task91_NestingInsideLayerPretty()
        {
            var pretty = Uglify.Css("@layer base{.a{&:hover{color:#f00}}}",
                new CssSettings { OutputMode = OutputMode.MultipleLines });
            Assert.That(pretty.HasErrors, Is.False);

            var expected = string.Join("\n",
                "@layer base",
                "{",
                "    .a",
                "    {",
                "        &:hover",
                "        {",
                "            color: #f00",
                "        }",
                "    }",
                "}");
            Assert.That(NormalizeNewLines(pretty.Code), Is.EqualTo(expected), $"actual <{pretty.Code}>");
        }

        // ------------------------------------------------------------------
        // Requirement 8.4: an empty '&' nested rule between two other block items
        // is dropped and no separator is left dangling around the removed rule;
        // the surrounding declarations survive in order.
        // ------------------------------------------------------------------
        [Test]
        public void Task91_RemoveEmptyBlocks_EmptyNestedRuleBetweenDeclarations()
        {
            var result = Uglify.Css(".a{color:red;&:hover{}margin:0}");
            Assert.That(result.HasErrors, Is.False);
            Assert.That(result.Code, Is.EqualTo(".a{color:#f00;margin:0}"), $"actual <{result.Code}>");
        }

        // Requirement 8.4: an empty nested rule is dropped while its non-empty
        // sibling nested rule is kept.
        [Test]
        public void Task91_RemoveEmptyBlocks_EmptyNestedRuleDroppedNonEmptySiblingKept()
        {
            var result = Uglify.Css(".a{&:hover{}&:focus{color:red}}");
            Assert.That(result.HasErrors, Is.False);
            Assert.That(result.Code, Is.EqualTo(".a{&:focus{color:#f00}}"), $"actual <{result.Code}>");
        }

        // ------------------------------------------------------------------
        // Requirement 8.5: emptiness cascades - a deep chain of rules whose only
        // leaf is an empty block collapses entirely, leaving no output.
        // ------------------------------------------------------------------
        [Test]
        public void Task91_RemoveEmptyBlocks_EmptyParentCascadesThroughDepth()
        {
            var result = Uglify.Css(".a{.b{.c{}}}");
            Assert.That(result.HasErrors, Is.False);
            Assert.That(result.Code, Is.EqualTo(string.Empty), $"actual <{result.Code}>");
        }

        // Requirement 8.5: an empty nested rule inside an '&' rule that also has a
        // declaration is dropped, but the '&' rule survives because of its own
        // declaration.
        [Test]
        public void Task91_RemoveEmptyBlocks_EmptyNestedRuleInsideAmpersandRuleKeptForDeclaration()
        {
            var result = Uglify.Css(".a{&{color:red;.b{}}}");
            Assert.That(result.HasErrors, Is.False);
            Assert.That(result.Code, Is.EqualTo(".a{&{color:#f00}}"), $"actual <{result.Code}>");
        }

        // ==================================================================
        // Task 9.2 - Error-case unit tests.
        //
        // Example-based tests asserting that malformed nesting constructs are
        // reported as parse errors and (where the streaming model allows it)
        // that the offending token/selector does not leak into the output.
        // These complement the property tests Property8 (invalid nested selector
        // lists, 5.5/5.6) and the Task73 at-rule EOF/malformed tests by making
        // each individual error case explicit and traceable.
        //
        // Requirements: 1.5, 3.6, 4.5, 5.5, 5.6, 2.6, 6.5, 7.6
        // ==================================================================

        // Returns true when the result reports at least one error whose code matches the
        // given CssErrorCode. The UglifyError.ErrorCode string is formatted as "CSS{number}"
        // (see CssParser.ReportError), so the expected string is derived from the enum value.
        static bool ReportsErrorCode(UglifyResult result, CssErrorCode code)
            => result.Errors != null
               && result.Errors.Any(e => e.ErrorCode == "CSS" + ((int)code & 0xffff));

        // ------------------------------------------------------------------
        // Requirements 1.5 / 3.6: a nesting selector ('&') encountered where a
        // declaration value/term is expected is reported as a parse error
        // (CssErrorCode.UnexpectedNestingSelector) and the offending '&' is not
        // emitted into the output.
        // ------------------------------------------------------------------
        [Test]
        public void Task92_AmpersandInsideDeclarationValue_ReportsErrorAndDropsAmpersand()
        {
            // Each source places '&' where a value/term is expected: as the whole value
            // (color:&) and embedded in a dimension value (width:10&px).
            var sources = new[] { ".a{color:&}", ".a{width:10&px}" };

            foreach (var source in sources)
            {
                var result = Uglify.Css(source);
                var code = result.Code ?? string.Empty;

                Assert.That(result.HasErrors, Is.True,
                    $"expected a parse error for '&' in a declaration value <{source}> but got <{code}>");
                Assert.That(ReportsErrorCode(result, CssErrorCode.UnexpectedNestingSelector), Is.True,
                    $"expected UnexpectedNestingSelector for <{source}> but got codes " +
                    $"[{string.Join(",", result.Errors.Select(e => e.ErrorCode))}]");
                // "no partial output": the offending '&' must not leak into the emitted CSS.
                Assert.That(code.Contains("&"), Is.False,
                    $"the '&' leaked into the output for <{source}>: <{code}>");
            }
        }

        // ------------------------------------------------------------------
        // Requirements 5.5 / 5.6: a nested selector list with an empty selector
        // position (leading comma, trailing comma, or doubled comma) is reported
        // as a parse error (ExpectedSelector) and the whole list fails atomically -
        // none of its selectors leak into the output. Each list embeds the
        // sentinel selectors ".leaksel"/".leaktwo" so any partial emission is
        // detectable.
        // ------------------------------------------------------------------
        [Test]
        public void Task92_EmptySelectorPositionInNestedList_ReportsErrorNoPartialOutput()
        {
            var cases = new (string name, string source)[]
            {
                ("leading comma", ".a{,&.leaksel{color:red}}"),
                ("trailing comma", ".a{&.leaksel,{color:red}}"),
                ("doubled comma",  ".a{&.leaksel,,&.leaktwo{color:red}}"),
            };

            foreach (var (name, source) in cases)
            {
                var result = Uglify.Css(source);
                var code = result.Code ?? string.Empty;

                Assert.That(result.HasErrors, Is.True,
                    $"[{name}] expected a parse error for <{source}> but got <{code}>");
                Assert.That(ReportsErrorCode(result, CssErrorCode.ExpectedSelector), Is.True,
                    $"[{name}] expected ExpectedSelector for <{source}> but got codes " +
                    $"[{string.Join(",", result.Errors.Select(e => e.ErrorCode))}]");
                // Atomic failure: none of the list's selectors may leak into the output.
                Assert.That(code.Contains("leaksel"), Is.False,
                    $"[{name}] a list selector leaked into the output for <{source}>: <{code}>");
                Assert.That(code.Contains("leaktwo"), Is.False,
                    $"[{name}] a list selector leaked into the output for <{source}>: <{code}>");
            }
        }

        // ------------------------------------------------------------------
        // Requirement 4.5: a nested selector that begins with a combinator
        // (>, +, ~) not followed by a valid compound selector is reported as a
        // parse error (ExpectedSelector) and the rule is rejected (nothing from
        // the malformed rule is emitted).
        // ------------------------------------------------------------------
        [Test]
        public void Task92_LeadingCombinatorWithNoFollowingSelector_ReportsErrorAndRejectsRule()
        {
            var cases = new (string name, string source)[]
            {
                ("child, space then brace", ".a{> {color:red}}"),
                ("child, immediate brace",  ".a{>{color:red}}"),
                ("next-sibling then brace", ".a{+ {color:red}}"),
            };

            foreach (var (name, source) in cases)
            {
                var result = Uglify.Css(source);
                var code = result.Code ?? string.Empty;

                Assert.That(result.HasErrors, Is.True,
                    $"[{name}] expected a parse error for <{source}> but got <{code}>");
                Assert.That(ReportsErrorCode(result, CssErrorCode.ExpectedSelector), Is.True,
                    $"[{name}] expected ExpectedSelector for <{source}> but got codes " +
                    $"[{string.Join(",", result.Errors.Select(e => e.ErrorCode))}]");
                // The malformed rule is rejected: neither its declaration nor a stray combinator
                // is emitted.
                Assert.That(code, Is.Empty,
                    $"[{name}] the rejected rule leaked output for <{source}>: <{code}>");
            }
        }

        [Test]
        public void Task92_AmpersandImmediatelyBeforeTypeSelector_ReportsErrorAndRejectsRule()
        {
            var result = Uglify.Css(".a{color:red;&div{color:blue}}");
            var code = result.Code ?? string.Empty;

            Assert.That(result.HasErrors, Is.True,
                $"expected a parse error for invalid '&div' nested selector but got <{code}>");
            Assert.That(ReportsErrorCode(result, CssErrorCode.ExpectedSelector), Is.True,
                $"expected ExpectedSelector for invalid '&div' nested selector but got codes " +
                $"[{string.Join(",", result.Errors.Select(e => e.ErrorCode))}]");
            Assert.That(code, Is.EqualTo(".a{color:#f00;}"));
        }

        [Test]
        public void Task92_InvalidNestedSelectorRecoversToFollowingDeclaration()
        {
            var result = Uglify.Css(".a{&div{color:red}background:blue}");
            var code = result.Code ?? string.Empty;

            Assert.That(result.HasErrors, Is.True,
                $"expected a parse error for invalid '&div' nested selector but got <{code}>");
            Assert.That(ReportsErrorCode(result, CssErrorCode.ExpectedSelector), Is.True,
                $"expected ExpectedSelector for invalid '&div' nested selector but got codes " +
                $"[{string.Join(",", result.Errors.Select(e => e.ErrorCode))}]");
            Assert.That(code, Is.EqualTo(".a{background:#00f}"));
        }

        [Test]
        public void Task92_InvalidNestedSelectorRecoversToFollowingValidNestedRule()
        {
            var result = Uglify.Css(".a{&div{color:red}&.ok{color:blue}}");
            var code = result.Code ?? string.Empty;

            Assert.That(result.HasErrors, Is.True,
                $"expected a parse error for invalid '&div' nested selector but got <{code}>");
            Assert.That(ReportsErrorCode(result, CssErrorCode.ExpectedSelector), Is.True,
                $"expected ExpectedSelector for invalid '&div' nested selector but got codes " +
                $"[{string.Join(",", result.Errors.Select(e => e.ErrorCode))}]");
            Assert.That(code, Is.EqualTo(".a{&.ok{color:#00f}}"));
        }

        // ------------------------------------------------------------------
        // Requirements 2.6 / 6.5 / 7.6: reaching end-of-input while a nested
        // block is still unterminated (missing its closing brace) is reported as
        // a parse error (UnexpectedEndOfFile).
        //
        // NOTE: because the parser streams each declaration as it is recognized,
        // the declarations parsed before EOF are already present in the output;
        // an unterminated nested block therefore reports an error but does NOT
        // roll back the partial text (matching the existing at-rule EOF tests in
        // Task73_UnterminatedAtRuleBlockWithNestedRuleAtEof_ReportsError). The
        // asserted contract for this case is the parse error itself.
        // ------------------------------------------------------------------
        [Test]
        public void Task92_UnterminatedNestedBlockAtEof_ReportsError()
        {
            var cases = new (string name, string source)[]
            {
                ("bare compound nested rule", ".a{.b{color:red}"),
                ("ampersand nested rule",     ".a{&:hover{color:red}"),
                ("deep nested rule",          ".a{.b{.c{color:red}}"),
            };

            foreach (var (name, source) in cases)
            {
                var result = Uglify.Css(source);

                Assert.That(result.HasErrors, Is.True,
                    $"[{name}] expected a parse error for the EOF-truncated nested block <{source}> " +
                    $"but got <{result.Code}>");
                Assert.That(ReportsErrorCode(result, CssErrorCode.UnexpectedEndOfFile), Is.True,
                    $"[{name}] expected UnexpectedEndOfFile for <{source}> but got codes " +
                    $"[{string.Join(",", result.Errors.Select(e => e.ErrorCode))}]");
            }
        }

        // ==================================================================
        // Task 10.1 - Regression guard for non-nested behavior.
        //
        // Model + generator for whole *non-nested* stylesheets: CSS that
        // contains no nesting selector ('&') and no nested rules, so it must be
        // handled exactly as the pre-nesting parser handled it.
        // ==================================================================

        // Base forms for a top-level (non-nested) selector. Each generated rule appends a
        // unique class (".rN") so no two rules share a selector (preventing any rule/selector
        // merging) and so the selector's source form is byte-for-byte its own minified form.
        // A leading "" yields a bare unique class (".rN"); a type/attribute base yields e.g.
        // "div.rN" / "[data-x].rN"; an id base yields "#id.rN". None contains '&' or a
        // descendant combinator, so no whitespace inside the selector is ever significant.
        static readonly string[] NonNestedSelectorBases = { "", "div", "#id", "[data-x]" };

        // Declarations already written in their minified form, keyed by a distinct property
        // name. Because every property differs and every value is already minimal
        // (#f00 / 0 / block / 1px), the minifier neither merges, drops, reorders, nor rewrites
        // any of them, so a rule's expected minified body is just the selected declarations
        // joined by ';'.
        static readonly string[] NonNestedDeclarations =
        {
            "color:#f00",
            "margin:0",
            "padding:0",
            "display:block",
            "border:1px",
        };

        // Insignificant-whitespace fragments injected into a non-nested rule's source at
        // positions the minifier must normalize away (around braces, the declaration colon,
        // and the declaration-separating semicolon). "" exercises the already-tight case.
        static readonly string[] NonNestedWs = { "", " ", "\n", "\t" };

        // Specification of one generated non-nested rule.
        sealed class NonNestedRuleSpec
        {
            public int BaseIdx;   // index into NonNestedSelectorBases
            public bool IsList;   // emit a two-selector list ".rN,.sN"
            public int DeclMask;  // non-zero bitmask selecting NonNestedDeclarations (>=1 bit)
            public string[] Ws;   // whitespace fragments consumed by position
        }

        static string NonNestedWsAt(string[] ws, int index)
            => (ws != null && index >= 0 && index < ws.Length) ? ws[index] : string.Empty;

        static Gen<NonNestedRuleSpec> NonNestedRuleGen()
        {
            var declBits = (1 << NonNestedDeclarations.Length) - 1;
            return from baseIdx in Gen.Choose(0, NonNestedSelectorBases.Length - 1)
                   from isList in Gen.Elements(new[] { false, true })
                   from mask in Gen.Choose(1, declBits) // >= 1 declaration always
                   from ws in Gen.ListOf(Gen.Elements(NonNestedWs))
                   select new NonNestedRuleSpec
                   {
                       BaseIdx = baseIdx,
                       IsList = isList,
                       DeclMask = mask,
                       Ws = ws.ToArray(),
                   };
        }

        // Builds the (source, expected-minified) pair for a whole non-nested stylesheet.
        // The source pads insignificant positions with arbitrary whitespace; the expected
        // form is the tight, canonical minified stylesheet (unique selectors concatenated
        // with no separators inserted between rules).
        static (string source, string expected) BuildNonNestedStylesheet(IReadOnlyList<NonNestedRuleSpec> rules)
        {
            var src = new StringBuilder();
            var exp = new StringBuilder();

            for (var i = 0; i < rules.Count; i++)
            {
                var rule = rules[i];
                var ws = rule.Ws;

                var baseForm = NonNestedSelectorBases[rule.BaseIdx];
                var selector = baseForm + ".r" + i;
                if (rule.IsList)
                    selector += ",.s" + i;

                // Selected declarations, in array order (never reordered by the minifier).
                var decls = new List<string>();
                for (var j = 0; j < NonNestedDeclarations.Length; j++)
                {
                    if (((rule.DeclMask >> j) & 1) != 0)
                        decls.Add(NonNestedDeclarations[j]);
                }

                // Optional whitespace between top-level rules is insignificant (removed).
                src.Append(NonNestedWsAt(ws, 0));
                src.Append(selector).Append(NonNestedWsAt(ws, 1)).Append('{');
                exp.Append(selector).Append('{');

                for (var d = 0; d < decls.Count; d++)
                {
                    if (d > 0)
                    {
                        // Whitespace around the ';' separator is removed by the minifier.
                        src.Append(NonNestedWsAt(ws, 2)).Append(';').Append(NonNestedWsAt(ws, 3));
                        exp.Append(';');
                    }

                    // Split each declaration at its ':' so whitespace can be injected around
                    // the colon (removed by the minifier), exercising declaration parsing.
                    var decl = decls[d];
                    var colon = decl.IndexOf(':');
                    var prop = decl.Substring(0, colon);
                    var val = decl.Substring(colon + 1);

                    src.Append(prop).Append(NonNestedWsAt(ws, 4)).Append(':').Append(NonNestedWsAt(ws, 5)).Append(val);
                    exp.Append(decl);
                }

                src.Append(NonNestedWsAt(ws, 6)).Append('}');
                exp.Append('}');
            }

            return (src.ToString(), exp.ToString());
        }

        // Known non-nested inputs that contain a genuine syntax error. Each is free of any
        // nesting selector or nested rule, so the pre-nesting parser reported an error for
        // each and the nesting-aware parser must report the same. Used to exercise 9.3.
        static readonly string[] NonNestedSyntaxErrors =
        {
            "}",                    // stray closing brace at top level
            "{color:red}",          // rule with no selector
            ".a{color:red",         // unterminated top-level block at EOF
            ".a{",                  // empty unterminated block at EOF
            "@",                    // lone at sign
            ".a{color:red}}",       // extra closing brace
        };

        // Discriminated case: either a constructed valid stylesheet (with an exact expected
        // minified baseline) or a known non-nested syntax error.
        sealed class NonNestedCase
        {
            public string Source;
            public string ExpectedMinified; // null for the syntax-error cases
            public bool ExpectValid;
        }

        static Gen<NonNestedCase> NonNestedCaseGen()
        {
            var validGen =
                from rules in Gen.NonEmptyListOf(NonNestedRuleGen())
                let capped = rules.Take(5).ToList()
                let built = BuildNonNestedStylesheet(capped)
                select new NonNestedCase { Source = built.source, ExpectedMinified = built.expected, ExpectValid = true };

            var errorGen =
                from err in Gen.Elements(NonNestedSyntaxErrors)
                select new NonNestedCase { Source = err, ExpectedMinified = null, ExpectValid = false };

            // Mostly valid stylesheets, with syntax-error cases mixed in so both the
            // byte-for-byte baseline (9.1/9.2) and the same-error-set guarantee (9.3) are
            // exercised within the single property.
            return Gen.Frequency(
                Tuple.Create(4, validGen),
                Tuple.Create(1, errorGen));
        }

        // Two results report the "same set of parse errors" when their error lists match on
        // error code and source position (start/end line and column), in order.
        static bool SameErrorSet(UglifyResult a, UglifyResult b)
        {
            var ea = (a.Errors ?? Enumerable.Empty<UglifyError>())
                .Select(e => (e.ErrorCode, e.StartLine, e.StartColumn, e.EndLine, e.EndColumn))
                .ToList();
            var eb = (b.Errors ?? Enumerable.Empty<UglifyError>())
                .Select(e => (e.ErrorCode, e.StartLine, e.StartColumn, e.EndLine, e.EndColumn))
                .ToList();

            if (ea.Count != eb.Count)
                return false;
            for (var i = 0; i < ea.Count; i++)
            {
                if (!ea[i].Equals(eb[i]))
                    return false;
            }
            return true;
        }

        static void AssertMinify(string source, string expected)
        {
            var result = Uglify.Css(source);
            Assert.That(result.HasErrors, Is.False, string.Join(Environment.NewLine, result.Errors.Select(e => e.ToString())));
            Assert.That(result.Code, Is.EqualTo(expected));
        }

        [Test]
        public void TypeSelectorFollowedByClassIsParsedAsNestedRule()
        {
            AssertMinify(".a{div.foo{color:red}}", ".a{div.foo{color:#f00}}");
        }

        [Test]
        public void TypeSelectorFollowedByPseudoIsParsedAsNestedRule()
        {
            AssertMinify(".a{div:hover{color:red}}", ".a{div:hover{color:#f00}}");
        }

        [Test]
        public void NamespaceQualifiedTypeSelectorIsParsedAsNestedRule()
        {
            AssertMinify("@namespace svg \"urn:x\";.a{svg|rect{color:red}}", "@namespace svg \"urn:x\";.a{svg|rect{color:#f00}}");
        }

        [Test]
        public void MalformedNestedRuleRecoversToFollowingDeclarationWhenBoundedByOwnBlock()
        {
            var result = Uglify.Css(".a{color:red;&,{x:y}background:blue}");

            Assert.That(result.HasErrors, Is.True);
            Assert.That(ReportsErrorCode(result, CssErrorCode.ExpectedSelector), Is.True);
            Assert.That(result.Code, Is.EqualTo(".a{color:#f00;background:#00f}"));
        }

        // ------------------------------------------------------------------
        // Feature: css-nesting, Property 13: Non-nested output is unchanged -
        // For any CSS input containing no '&' and no nested rules, the output under given
        // CssSettings is byte-for-byte identical to the pre-nesting parser's output for the
        // same input and settings, in both minified and pretty modes, and the same set of
        // parse errors is reported.
        //
        // Because the nesting feature is merged into CssParser (no separate pre-nesting parser
        // is available), the pre-nesting output is captured two ways: (1) a byte-for-byte
        // canonical minified baseline constructed for each generated valid non-nested
        // stylesheet, and (2) determinism/idempotence of the transform - the nesting code
        // paths must never perturb non-nested output or its error set. For valid input the
        // minified output must equal the captured baseline, neither mode may report errors or
        // introduce a '&', and re-parsing each output reproduces it unchanged (idempotence);
        // for known non-nested syntax errors the error must still be reported and the same
        // error set (codes + source positions) is produced deterministically across modes.
        // Validates: Requirements 9.1, 9.2, 9.3
        // ------------------------------------------------------------------
        [Test]
        public void Property13_NonNestedOutputIsUnchanged()
        {
            var prettySettings = new CssSettings { OutputMode = OutputMode.MultipleLines };

            var property = Prop.ForAll(Arb.From(NonNestedCaseGen()), (NonNestedCase c) =>
            {
                var min1 = Uglify.Css(c.Source);
                var min2 = Uglify.Css(c.Source);
                var pretty1 = Uglify.Css(c.Source, prettySettings);
                var pretty2 = Uglify.Css(c.Source, prettySettings);

                // 9.3 (and stability): parsing the same non-nested input twice must produce the
                // same output and the same set of parse errors (codes + positions) in each mode.
                var minDeterministic = min1.Code == min2.Code && SameErrorSet(min1, min2);
                var prettyDeterministic = pretty1.Code == pretty2.Code && SameErrorSet(pretty1, pretty2);

                bool ok;
                if (c.ExpectValid)
                {
                    var minCode = min1.Code ?? string.Empty;
                    var prettyCode = pretty1.Code ?? string.Empty;

                    // 9.1 / 9.2: valid non-nested input parses without error and minifies to the
                    // exact captured baseline; no '&' is ever introduced.
                    var noErrors = !min1.HasErrors && !pretty1.HasErrors;
                    var baselineMatch = minCode == c.ExpectedMinified;
                    var noAmpersand = !minCode.Contains('&') && !prettyCode.Contains('&');

                    // 9.1 (both modes): re-parsing each output reproduces it unchanged, i.e. the
                    // non-nested transform is idempotent in both minified and pretty modes.
                    var idempotentMin = (Uglify.Css(minCode).Code ?? string.Empty) == minCode;
                    var idempotentPretty = (Uglify.Css(prettyCode, prettySettings).Code ?? string.Empty) == prettyCode;

                    ok = minDeterministic && prettyDeterministic && noErrors && baselineMatch
                         && noAmpersand && idempotentMin && idempotentPretty;
                }
                else
                {
                    // 9.3: a non-nested syntax error is still reported (in both modes) and its
                    // error set is produced deterministically.
                    ok = minDeterministic && prettyDeterministic && min1.HasErrors && pretty1.HasErrors;
                }

                return ok.Label(
                    $"source=<{c.Source}> expectValid={c.ExpectValid} " +
                    $"min=<{min1.Code}> minErrors={min1.HasErrors} " +
                    $"pretty=<{pretty1.Code}> prettyErrors={pretty1.HasErrors} " +
                    $"expectedMin=<{c.ExpectedMinified}>");
            });

            RunProperty(property);
        }

        // ------------------------------------------------------------------
        // Feature: css-nesting, peek-replay invariant -
        // The block-body classifier peeks (and buffers) a bounded window of tokens to tell a
        // declaration apart from a nested rule. Those buffered tokens are later *replayed*
        // instead of being re-scanned by the parser, so the replay must reproduce EXACTLY what a
        // fresh scan would have produced. Two pieces of machinery make that true and are the ones
        // most likely to regress silently: NormalizePeekedToken (re-applies zero-reduction of
        // length units to buffered number tokens) and TryScanBufferedReplacementToken (reassembles
        // a %replacement% token from its buffered pieces). These example cases lock their output.
        // ------------------------------------------------------------------
        [Test]
        public void PeekReplay_PreservesZeroReductionAndReplacementTokens()
        {
            var cases = new[]
            {
                // lengths must still zero-reduce after flowing through the look-ahead buffer
                Tuple.Create(".a{width:0px}", ".a{width:0}"),
                Tuple.Create(".a{margin:0em}", ".a{margin:0}"),
                Tuple.Create(".a{height:0rem}", ".a{height:0}"),
                Tuple.Create(".a{top:0vh}", ".a{top:0}"),

                // non-length zeros are NOT reduced and must round-trip verbatim
                Tuple.Create(".a{width:0%}", ".a{width:0%}"),
                Tuple.Create(".a{x:0deg}", ".a{x:0deg}"),
                Tuple.Create(".a{padding:0s}", ".a{padding:0s}"),

                // %replacement% tokens must be reassembled from their buffered pieces
                Tuple.Create(".a{color:%NAME%}", ".a{color:%NAME%}"),
                Tuple.Create(".a{color:%NAME:red%}", ".a{color:%NAME:red%}"),
                Tuple.Create(".a{color:%a.b.c%}", ".a{color:%a.b.c%}"),
                Tuple.Create(".a{color:%NAME:%}", ".a{color:%NAME:%}"),

                // and both kinds must survive when surrounded by other block items (which change
                // the buffer state at the point each value is scanned)
                Tuple.Create(".a{width:0px;color:%NAME%}", ".a{width:0;color:%NAME%}"),
                Tuple.Create(".a{color:%NAME%;width:0px}", ".a{color:%NAME%;width:0}"),
                Tuple.Create(".a{.n{x:y}width:0px}", ".a{.n{x:y}width:0}"),
            };

            foreach (var pair in cases)
            {
                var result = Uglify.Css(pair.Item1);
                Assert.That(result.HasErrors, Is.False, $"unexpected errors for <{pair.Item1}>");
                Assert.That(result.Code, Is.EqualTo(pair.Item2), $"peek-replay output mismatch for <{pair.Item1}>");
            }
        }

        // Declaration values that exercise the buffered-replay path (zero-reducible lengths,
        // non-reducible zeros, and %replacement% tokens).
        static readonly string[] ReplayProbeDeclarations =
        {
            "width:0px", "margin:0em", "height:0rem", "top:0vh",
            "width:0%", "padding:0s", "x:0deg",
            "color:%NAME%", "background:%THEME:red%", "content:%a.b.c%",
        };

        // Self-terminated sibling items placed BEFORE the probe to vary the look-ahead buffer
        // state. Declarations carry their own ';'; nested rules and nested at-rules do not need a
        // separator before the following item.
        static readonly string[] ReplaySiblingItems =
        {
            "color:red;", "display:block;", "margin:0;", "width:0px;",
            ".n{x:y}", "&:hover{color:blue}", "@media screen{color:red}",
        };

        // Minify a single declaration on its own and return the text inside the rule braces --
        // i.e. the probe's canonical minified form with a minimal look-ahead buffer.
        static string MinifiedInner(string declaration)
        {
            var code = Uglify.Css(".probe{" + declaration + "}").Code ?? string.Empty;
            var open = code.IndexOf('{');
            var close = code.LastIndexOf('}');
            return (open < 0 || close <= open) ? string.Empty : code.Substring(open + 1, close - open - 1);
        }

        // ------------------------------------------------------------------
        // Feature: css-nesting, Property 14: Look-ahead replay is transparent -
        // A probe declaration placed LAST in a block is preceded by an arbitrary list of other
        // items. Whatever state those items leave the classification look-ahead buffer in, the
        // probe's minified text must be identical to minifying the probe on its own -- what
        // precedes a value in the buffer must never perturb that value's replayed output.
        // ------------------------------------------------------------------
        [Test]
        public void Property14_LookaheadReplayIsTransparent()
        {
            var gen = from probe in Gen.Elements(ReplayProbeDeclarations)
                      from prefix in Gen.ListOf(Gen.Elements(ReplaySiblingItems))
                      select Tuple.Create(probe, prefix.ToList());

            var property = Prop.ForAll(Arb.From(gen), (Tuple<string, List<string>> t) =>
            {
                var probe = t.Item1;
                var expectedProbe = MinifiedInner(probe);

                var source = ".a{" + string.Concat(t.Item2) + probe + "}";
                var result = Uglify.Css(source);
                var code = result.Code ?? string.Empty;

                var ok = !result.HasErrors
                    && expectedProbe.Length > 0
                    && code.EndsWith(expectedProbe + "}", StringComparison.Ordinal);

                return ok.Label($"source=<{source}> code=<{code}> expectedProbe=<{expectedProbe}> errors={result.HasErrors}");
            });

            RunProperty(property);
        }

        // ------------------------------------------------------------------
        // Feature: css-nesting, @container prelude spacing (minified + pretty) -
        // The @container prelude is emitted by a self-contained token re-spacer (it does NOT reuse
        // the @media query machinery, whose grammar cannot express container names, style()
        // queries, or recursively nested conditions, and whose pretty-mode spacing differs).
        // These characterization tests lock the current prelude spacing in BOTH output modes so a
        // future change to that re-spacer cannot silently alter @container output.
        // ------------------------------------------------------------------
        [Test]
        public void Container_PrettyOutputPreservesLogicalPreludeSpacing()
        {
            var pretty = Uglify.Css(
                "@container card (width > 400px) and (height < 500px){.a{color:red}}",
                new CssSettings { OutputMode = OutputMode.MultipleLines });
            Assert.That(pretty.HasErrors, Is.False);

            var expected = string.Join("\n",
                "@container card (width>400px) and (height<500px)",
                "{",
                "    .a",
                "    {",
                "        color: #f00",
                "    }",
                "}");
            Assert.That(NormalizeNewLines(pretty.Code), Is.EqualTo(expected), $"actual <{pretty.Code}>");
        }

        [Test]
        public void Container_PrettyOutputPreservesStyleQuerySpacing()
        {
            var pretty = Uglify.Css(
                "@container style(--accent: blue){.a{color:red}}",
                new CssSettings { OutputMode = OutputMode.MultipleLines });
            Assert.That(pretty.HasErrors, Is.False);

            var expected = string.Join("\n",
                "@container style(--accent:blue)",
                "{",
                "    .a",
                "    {",
                "        color: #f00",
                "    }",
                "}");
            Assert.That(NormalizeNewLines(pretty.Code), Is.EqualTo(expected), $"actual <{pretty.Code}>");
        }

        [Test]
        public void Container_PrettyOutputPreservesNestedConditionSpacing()
        {
            var pretty = Uglify.Css(
                "@container ((min-width:100px) and (min-height:100px)){.a{color:red}}",
                new CssSettings { OutputMode = OutputMode.MultipleLines });
            Assert.That(pretty.HasErrors, Is.False);

            var expected = string.Join("\n",
                "@container ((min-width:100px) and (min-height:100px))",
                "{",
                "    .a",
                "    {",
                "        color: #f00",
                "    }",
                "}");
            Assert.That(NormalizeNewLines(pretty.Code), Is.EqualTo(expected), $"actual <{pretty.Code}>");
        }

        [Test]
        public void Container_MinifiedPreservesRangeAndNotSpacing()
        {
            // range syntax keeps operators tight; 'not'/'or' combinators keep a single space
            var range = Uglify.Css("@container (400px <= width <= 700px){.a{color:red}}");
            Assert.That(range.HasErrors, Is.False);
            Assert.That(range.Code, Is.EqualTo("@container (400px<=width<=700px){.a{color:#f00}}"), $"actual <{range.Code}>");

            var notOr = Uglify.Css("@container not (width > 400px){.a{color:red}}");
            Assert.That(notOr.HasErrors, Is.False);
            Assert.That(notOr.Code, Is.EqualTo("@container not (width>400px){.a{color:#f00}}"), $"actual <{notOr.Code}>");
        }
    }
}
