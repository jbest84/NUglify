using System.Linq;
using NUglify.Css;
using NUnit.Framework;

namespace NUglify.Tests.Css
{
    public sealed partial class ConformanceTests
    {
        // Local cases target gaps in the pinned generic Syntax corpus and existing
        // Nesting/MathFunctions snapshots. Expectations concern syntax, recovery,
        // tokens, and source order, not browser property-value validation.
        static readonly CssCase[] BoundaryInputs =
        {
            new CssCase("Boundary.DeclarationListOrder", CssType.DeclarationList,
                "display:block;display:grid;--after:ok", "display:block;display:grid;--after:ok",
                "display: block;\ndisplay: grid;\n--after: ok;"),
            new CssCase("Boundary.StylesheetOrder", CssType.FullStyleSheet,
                ".x{display:block;display:grid;--after:ok}", ".x{display:block;display:grid;--after:ok}",
                ".x\n{\n    display: block;\n    display: grid;\n    --after: ok;\n}"),
            new CssCase("Boundary.DeclarationListRecovery", CssType.DeclarationList,
                "--before:ok;broken;--after:ok", "--before:ok;--after:ok",
                "--before: ok;\n--after: ok;", 1035),
            new CssCase("Boundary.StyleRecovery", CssType.FullStyleSheet,
                ".x{--before:ok;broken;--after:ok}.tail{--tail:ok}", ".x{--before:ok;--after:ok}.tail{--tail:ok}",
                ".x\n{\n    --before: ok;\n    --after: ok;\n}\n.tail\n{\n    --tail: ok;\n}", 1035),
            new CssCase("Boundary.FontFaceRecovery", CssType.FullStyleSheet,
                "@font-face{font-family:test;broken;src:url(font.woff2)}.tail{--tail:ok}",
                "@font-face{font-family:test;src:url(font.woff2)}.tail{--tail:ok}",
                "@font-face\n{\n    font-family: test;\n    src: url(font.woff2);\n}\n.tail\n{\n    --tail: ok;\n}", 1035),
            new CssCase("Boundary.PageRecovery", CssType.FullStyleSheet,
                "@page{margin:1cm;broken;size:A4}.tail{--tail:ok}", "@page{margin:1cm;size:A4}.tail{--tail:ok}",
                "@page\n{\n    margin: 1cm;\n    size: A4;\n}\n.tail\n{\n    --tail: ok;\n}", 1035),
            new CssCase("Boundary.DeclarationVersusPseudoRule", CssType.FullStyleSheet,
                ".x{a:b;a:hover{--inner:ok}a:c;--after:ok}", ".x{a:b;a:hover{--inner:ok}a:c;--after:ok}",
                ".x\n{\n    a: b;\n    a:hover\n    {\n        --inner: ok;\n    }\n    a: c;\n    --after: ok;\n}"),
            new CssCase("Boundary.GroupRuleRecovery", CssType.FullStyleSheet,
                "@media screen{.x{broken;--after:ok}}.tail{--tail:ok}", "@media screen{.x{--after:ok}}.tail{--tail:ok}",
                "@media screen\n{\n    .x\n    {\n        --after: ok;\n    }\n}\n.tail\n{\n    --tail: ok;\n}", 1035),
            // WPT 0eefed92899544e837b8263db492a6235fa858a1:
            // css/css-nesting/mixed-declarations-rules.html: CSS style block;
            // omit unrelated width/height, retain the declaration/group/declaration order.
            new CssCase("Boundary.WptMixedDeclarations", CssType.FullStyleSheet,
                "div{background-color:blue;@media all{background-color:red}background-color:green}",
                "div{background-color:blue;@media all{background-color:red}background-color:green}",
                "div\n{\n    background-color: blue;\n    @media all\n    {\n        background-color: red\n    }\n    background-color: green;\n}"),
            new CssCase("Boundary.SupportsScopeAndOrder", CssType.FullStyleSheet,
                ".x{--before:ok;@supports (display:grid){display:grid;&:hover{--inner:ok}}--after:ok}",
                ".x{--before:ok;@supports(display:grid){display:grid;&:hover{--inner:ok}}--after:ok}",
                ".x\n{\n    --before: ok;\n    @supports(\n    display: grid)\n    {\n    display: grid;\n    &:hover\n    {\n        --inner: ok;\n    }}\n    --after: ok;\n}"),
            new CssCase("Boundary.ContainerTokenQuery", CssType.FullStyleSheet,
                "@container card style(--theme: dark){.x{--before:ok;&:hover{--inner:ok}--after:ok}}",
                "@container card style(--theme:dark){.x{--before:ok;&:hover{--inner:ok}--after:ok}}",
                "@container card style(--theme:dark)\n{\n    .x\n    {\n        --before: ok;\n        &:hover\n        {\n            --inner: ok;\n        }\n        --after: ok;\n    }\n}"),

            // WPT css/css-syntax/serialize-consecutive-tokens.html:
            // testTokenPairs("foo", "bar"), ("foo", "bar()"), ("123", "bar"),
            // and ("123", "%"). Embed the pre-substitution token pairs directly
            // in --x with a comment boundary; assert tokens, not computed CSSOM values.
            new CssCase("Boundary.WptIdentifierComment", CssType.DeclarationList,
                "--x:foo/**/bar;--after:ok", "--x:foo/**/bar;--after:ok", "--x: foo/**/bar;\n--after: ok;"),
            new CssCase("Boundary.WptFunctionComment", CssType.DeclarationList,
                "--x:foo/**/bar();--after:ok", "--x:foo/**/bar();--after:ok", "--x: foo/**/bar();\n--after: ok;"),
            new CssCase("Boundary.WptNumberIdentifierComment", CssType.DeclarationList,
                "--x:123/**/bar;--after:ok", "--x:123/**/bar;--after:ok", "--x: 123/**/bar;\n--after: ok;"),
            new CssCase("Boundary.WptNumberPercentComment", CssType.DeclarationList,
                "--x:123/**/%;--after:ok", "--x:123/**/%;--after:ok", "--x: 123/**/%;\n--after: ok;"),
            new CssCase("Boundary.StringCommentText", CssType.DeclarationList,
                "--x:\"a/* keep */b\";--after:ok", "--x:\"a/* keep */b\";--after:ok", "--x: \"a/* keep */b\";\n--after: ok;"),
            new CssCase("Boundary.QuotedUrl", CssType.DeclarationList,
                "background:url(\"a b.png\");--after:ok", "background:url(\"a b.png\");--after:ok", "background: url(\"a b.png\");\n--after: ok;"),
            new CssCase("Boundary.UrlEscape", CssType.DeclarationList,
                @"background:url(a\)b.png);--after:ok", @"background:url(a\)b.png);--after:ok", "background: url(a\\)b.png);\n--after: ok;"),
            new CssCase("Boundary.UrlCommentText", CssType.DeclarationList,
                "background:url(a/*b*/c.png);--after:ok", "background:url(a/*b*/c.png);--after:ok", "background: url(a/*b*/c.png);\n--after: ok;"),
            new CssCase("Boundary.EscapeTerminator", CssType.DeclarationList,
                @"--x:\31 a;--after:ok", @"--x:\31 a;--after:ok", "--x: \\31 a;\n--after: ok;"),
            new CssCase("Boundary.EscapeRealWhitespace", CssType.DeclarationList,
                @"--x:\31  a;--after:ok", @"--x:\31  a;--after:ok", "--x: \\31  a;\n--after: ok;"),
            new CssCase("Boundary.DecodedEscapeTerminator", CssType.DeclarationList,
                @"--x:\61 b;--after:ok", "--x:ab;--after:ok", "--x: ab;\n--after: ok;") { DecodeEscapes = true },
            new CssCase("Boundary.DecodedEscapeWhitespace", CssType.DeclarationList,
                @"--x:\61  b;--after:ok", "--x:a b;--after:ok", "--x: a b;\n--after: ok;") { DecodeEscapes = true },
            new CssCase("Boundary.DecodedEscapedDelimiter", CssType.DeclarationList,
                @"--x:\3b ;--after:ok", @"--x:\3b;--after:ok", "--x: \\3b;\n--after: ok;") { DecodeEscapes = true },
            new CssCase("Boundary.ExponentSeparatedUnit", CssType.DeclarationList,
                "--x:1e+2/**/px;--after:ok", "--x:1e+2/**/px;--after:ok", "--x: 1e+2/**/px;\n--after: ok;"),
            new CssCase("Boundary.ExponentSeparatedDigits", CssType.DeclarationList,
                "--x:1/**/2;--after:ok", "--x:1/**/2;--after:ok", "--x: 1/**/2;\n--after: ok;"),
            new CssCase("Boundary.MathRequiredWhitespace", CssType.DeclarationList,
                "width:calc(1px /* left */ + /* right */ 2px);--after:ok", "width:calc(1px + 2px);--after:ok", "width: calc(1px + 2px);\n--after: ok;"),
            new CssCase("Boundary.LfBadString", CssType.DeclarationList,
                "font-family:'bad\ncolor:red;--after:ok", "--after:ok", "--after: ok;", 1002, 1036),
            new CssCase("Boundary.CrBadString", CssType.DeclarationList,
                "font-family:'bad\rcolor:red;--after:ok", "--after:ok", "--after: ok;", 1002, 1036),
            new CssCase("Boundary.CrlfBadString", CssType.DeclarationList,
                "font-family:'bad\r\ncolor:red;--after:ok", "--after:ok", "--after: ok;", 1002, 1036),
            // CSS Syntax 3 input preprocessing maps form feed to LF. The scanner
            // previously failed to advance on this character; fix before running.
            new CssCase("Boundary.FormFeedBadString", CssType.DeclarationList,
                "font-family:'bad\fcolor:red;--after:ok", "--after:ok", "--after: ok;", 1002, 1036),
            new CssCase("Boundary.EscapedCrlf", CssType.DeclarationList,
                "--x:'a\\\r\nb';--after:ok", "--x:'ab';--after:ok", "--x: 'ab';\n--after: ok;"),
            new CssCase("Boundary.EscapedFormFeed", CssType.DeclarationList,
                "--x:'a\\\fb';--after:ok", "--x:'ab';--after:ok", "--x: 'ab';\n--after: ok;"),
            new CssCase("Boundary.StringControlCharacter", CssType.DeclarationList,
                "--x:'a\u0001b';--after:ok", "--x:'a\\1 b';--after:ok", "--x: 'a\\1 b';\n--after: ok;"),

            // WPT css/css-variables/variable-declaration-07.html (CC0): original
            // style block in declaration-list mode; retain duplicate --a and color
            // declarations. Only token preservation/order is asserted; no substitution.
            new CssCase("Boundary.WptEmptyFallback", CssType.DeclarationList,
                "color:red;--a:crimson;--b:green;--a:var(--b,);color:var(--a)",
                "color:red;--a:crimson;--b:green;--a:var(--b,);color:var(--a)",
                "color: red;\n--a: crimson;\n--b: green;\n--a: var(--b,);\ncolor: var(--a);"),
            new CssCase("Boundary.NestedFallback", CssType.DeclarationList,
                "--x:var(--missing,var(--also-missing,red,blue));--after:ok",
                "--x:var(--missing,var(--also-missing,red,blue));--after:ok",
                "--x: var(--missing,var(--also-missing,red,blue));\n--after: ok;"),
            new CssCase("Boundary.OpaqueCustomBlocks", CssType.FullStyleSheet,
                ".x{--payload:{a:b;c:[1,2]};--after:ok}", ".x{--payload:{a:b;c:[1,2]};--after:ok}",
                ".x\n{\n    --payload: {a:b;c:[1,2]};\n    --after: ok;\n}"),
            new CssCase("Boundary.CustomFunctionTokens", CssType.DeclarationList,
                "--x:FUTURE([1,2],{a:b;c:d});--after:ok", "--x:FUTURE([1,2],{a:b;c:d});--after:ok",
                "--x: FUTURE([1,2],{a:b;c:d});\n--after: ok;"),
            // WPT css/selectors/parsing/parse-is-where.html: assert_valid(true,
            // "{}(div + bar, div ~ .baz)"). Substitute each pseudo and wrap in a rule.
            new CssCase("Boundary.WptIsCombinators", CssType.FullStyleSheet,
                ":is(div + bar, div ~ .baz){--after:ok}", ":is(div+bar,div~.baz){--after:ok}",
                ":is(div+bar,div~.baz)\n{\n    --after: ok;\n}"),
            new CssCase("Boundary.WptWhereCombinators", CssType.FullStyleSheet,
                ":where(div + bar, div ~ .baz){--after:ok}", ":where(div+bar,div~.baz){--after:ok}",
                ":where(div+bar,div~.baz)\n{\n    --after: ok;\n}"),
            new CssCase("Boundary.MathVariableFallback", CssType.DeclarationList,
                "width:calc(var(--width,var(--backup,10px)) + 2px);--after:ok",
                "width:calc(var(--width,var(--backup,10px)) + 2px);--after:ok",
                "width: calc(var(--width,var(--backup,10px)) + 2px);\n--after: ok;"),

            // Adjacent controls for the defects above, selected after the first run.
            new CssCase("Boundary.DecodedEscapeCrlf", CssType.DeclarationList,
                "--x:\\61\r\nb;--after:ok", "--x:ab;--after:ok", "--x: ab;\n--after: ok;") { DecodeEscapes = true },
            new CssCase("Boundary.DecodedEscapeCrlfWhitespace", CssType.DeclarationList,
                "--x:\\61\r\n b;--after:ok", "--x:a b;--after:ok", "--x: a b;\n--after: ok;") { DecodeEscapes = true },
            new CssCase("Boundary.OrdinaryIdentifierComment", CssType.DeclarationList,
                "font-family:foo/**/bar;--after:ok", "font-family:foo/**/bar;--after:ok", "font-family: foo/**/bar;\n--after: ok;"),
            new CssCase("Boundary.OrdinaryNumberComment", CssType.DeclarationList,
                "future:123/**/px;--after:ok", "future:123/**/px;--after:ok", "future: 123/**/px;\n--after: ok;"),
            new CssCase("Boundary.SelectorCommentWhitespace", CssType.FullStyleSheet,
                ".a/**/ .b{--x:ok}.a/**/.b{--x:other}", ".a .b{--x:ok}.a.b{--x:other}",
                ".a .b\n{\n    --x: ok;\n}\n.a.b\n{\n    --x: other;\n}"),
            new CssCase("Boundary.CustomComponentMismatch", CssType.DeclarationList,
                "--x:{a:b];--after:ok", "--after:ok", "--after: ok;", 1019, 1036),
            new CssCase("Boundary.CustomComponentBadString", CssType.DeclarationList,
                "--x:F('bad\nmore);--after:ok", "--after:ok", "--after: ok;", 1002, 1036),
            new CssCase("Boundary.CustomComponentEof", CssType.DeclarationList,
                "--x:F([1", "--x:F([1])", "--x: F([1]);", 1066),
            new CssCase("Boundary.MathRightVariable", CssType.DeclarationList,
                "width:calc(1px/**/+/**/var(--x));--after:ok",
                "width:calc(1px+var(--x));--after:ok", "width: calc(1px+var(--x));\n--after: ok;"),
            new CssCase("Boundary.MathLaterVariable", CssType.DeclarationList,
                "width:calc(1px/**/+/**/2px + var(--x));--after:ok",
                "width:calc(1px+/**/2px + var(--x));--after:ok", "width: calc(1px+/**/2px + var(--x));\n--after: ok;"),
        };

        [TestCase(false), TestCase(true)]
        public void CommentBoundariesDoNotSupplyRequiredMathWhitespace(bool pretty)
        {
            const string source = "width:calc(1px/**/+/**/2px);--after:ok";
            var result = Uglify.Css(source, Settings(CssType.DeclarationList, pretty));
            Assert.Multiple(() =>
            {
                Assert.That(result.Errors.Select(error => error.ErrorNumber), Is.EqualTo(new[] { 1065 }));
                Assert.That(result.HasErrors, Is.True);
                Assert.That(result.Code, Is.EqualTo(pretty
                    ? "width: calc(1px+/**/2px);\n--after: ok;"
                    : "width:calc(1px+/**/2px);--after:ok"));
            });
            var reparsed = Uglify.Css(result.Code, Settings(CssType.DeclarationList, pretty));
            Assert.That(reparsed.Errors.Select(error => error.ErrorNumber), Is.EqualTo(new[] { 1065 }));
            Assert.That(reparsed.Code, Is.EqualTo(result.Code));
        }

        // Minimized from generated escaped-code-point failures; each CSS escape
        // resolves independently, including adjacent UTF-16 surrogate escapes.
        [TestCase("\\0 ", "\\0 ")]
        [TestCase("\\000000", "\\0 ")]
        [TestCase("\\d800 ", "\ufffd")]
        [TestCase("\\dfff ", "\ufffd")]
        [TestCase("\\110000 ", "\ufffd")]
        [TestCase("\\ffffff ", "\ufffd")]
        [TestCase("\\1f600 ", "\U0001f600")]
        [TestCase("\\d83d\\de00 ", "\ufffd\ufffd")]
        public void DecodedEscapedCodePointsUseCssReplacementRules(string escape, string decoded)
        {
            var test = new CssCase("EscapedCodePoint", CssType.DeclarationList,
                "--x:" + escape + "b;--after:ok", "--x:" + decoded + "b;--after:ok",
                "--x: " + decoded + "b;\n--after: ok;") { DecodeEscapes = true };
            SyntaxRecoveryAndOutput(test, false);
            SyntaxRecoveryAndOutput(test, true);
        }

        [TestCase("--x:F(//**/*);--after:ok", "--x:F(//**/*);--after:ok", "--x: F(//**/*);\n--after: ok;")]
        [TestCase("--before:ok;// ordinary extension comment\n--after:ok", "--before:ok;--after:ok", "--before: ok;\n--after: ok;")]
        public void SlashBeforeBlockCommentRetainsTokensAndOrdinaryLineCommentStillWorks(string source, string minify, string pretty)
        {
            var test = new CssCase("SlashCommentBoundary", CssType.DeclarationList, source, minify, pretty);
            SyntaxRecoveryAndOutput(test, false);
            SyntaxRecoveryAndOutput(test, true);
        }
    }
}
