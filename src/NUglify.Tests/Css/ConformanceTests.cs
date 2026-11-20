using System;
using System.Collections.Generic;
using System.Linq;
using NUglify.Css;
using NUnit.Framework;

namespace NUglify.Tests.Css
{
    [TestFixture, Category("CssConformance")]
    public sealed partial class ConformanceTests
    {
        // CSS parsing tests, pinned at 203ce36bffd617db7f118c551e32794561fb273d.
        // Inputs are adapted from the indicated pair; output goldens are NUglify
        // serialization of the independently reviewed token/recovery expectations.
        public sealed class CssCase
        {
            public string Id { get; }
            public CssType Mode { get; }
            public string Source { get; }
            public string Minify { get; }
            public string Pretty { get; }
            public int[] Errors { get; }
            public bool DecodeEscapes { get; set; }
            public CssCase(string id, CssType mode, string source, string minify, string pretty, params int[] errors)
            { Id = id; Mode = mode; Source = source; Minify = minify; Pretty = pretty; Errors = errors; }
        }

        static readonly CssCase[] Inputs =
        {
            // stylesheet.json pair 0 (zero-based): Unchanged input.
            new CssCase("EmptyStylesheet", CssType.FullStyleSheet, "", "", ""),
            // stylesheet.json pair 1 (zero-based): Unchanged input.
            new CssCase("BareSelector", CssType.FullStyleSheet, "foo", "", "", 1066),
            // stylesheet.json pair 2 (zero-based): Unchanged input.
            new CssCase("SelectorWithoutBlock", CssType.FullStyleSheet, "foo 4", "", "", 1031, 1025),
            // stylesheet.json pair 3 (zero-based): Unchanged input.
            new CssCase("AtRuleAtEof", CssType.FullStyleSheet, "@foo", "@foo", "@foo\n"),
            // stylesheet.json pair 4 (zero-based): Unchanged input.
            new CssCase("AtRuleWithComment", CssType.FullStyleSheet, "@foo bar; \t/* comment */", "@foo bar;", "@foo bar;\n"),
            // stylesheet.json pair 8 (zero-based): Unchanged input.
            new CssCase("SelectorComments", CssType.FullStyleSheet, " /**/ div > p { color: #aaa;  } /**/ ", "div>p{color:#aaa}", "div>p\n{\n    color: #aaa;\n}"),
            // stylesheet.json pair 12 (zero-based): Unchanged input.
            new CssCase("FollowingEmptyRule", CssType.FullStyleSheet, "div { color: #aaa; } p{}", "div{color:#aaa}p{}", "div\n{\n    color: #aaa;\n}\np\n{}"),
            // stylesheet.json pair 13 (zero-based): Unchanged input.
            new CssCase("TrailingCdc", CssType.FullStyleSheet, "div {} -->", "div{}-->", "div\n{}-->"),
            // declaration_list.json pair 0 (zero-based): Unchanged input.
            new CssCase("EmptyDeclarations", CssType.DeclarationList, "", "", ""),
            // declaration_list.json pair 1 (zero-based): Unchanged input.
            new CssCase("EmptySemicolons", CssType.DeclarationList, ";; /**/ ; ;", "", ";;;;"),
            // declaration_list.json pair 2 (zero-based): Unchanged input.
            new CssCase("DeclarationsAndPriority", CssType.DeclarationList, "a:b; c:d 42!important;\n", "a:b;c:d 42!important", "a: b;\nc: d 42!important;"),
            // declaration_list.json pair 3 (zero-based): Appended a valid --after declaration to verify recovery.
            new CssCase("MissingColonRecovery", CssType.DeclarationList, "z;a:b;--after:ok", "a:b;--after:ok", "a: b;\n--after: ok;", 1035),
            // declaration_list.json pair 4 (zero-based): Appended a valid --after declaration to verify recovery.
            new CssCase("LiteralBang", CssType.DeclarationList, "z:x!;a:b;--after:ok", "z:x!;a:b;--after:ok", "z: x!;\na: b;\n--after: ok;"),
            // declaration_list.json pair 5 (zero-based): Appended a valid --after declaration to verify recovery.
            new CssCase("InvalidPropertyRecovery", CssType.DeclarationList, "a:b; c+:d;--after:ok", "a:b;--after:ok", "a: b;\n--after: ok;", 1035),
            // one_declaration.json pair 3 (zero-based): Mapped the single declaration to NUglify DeclarationList mode. Appended a valid following declaration.
            new CssCase("MissingColonAtEof", CssType.DeclarationList, "foo;--after:ok", "--after:ok", "--after: ok;", 1035),
            // one_declaration.json pair 8 (zero-based): Mapped the single declaration to NUglify DeclarationList mode. Appended a valid following declaration.
            new CssCase("InvalidPropertyTokens", CssType.DeclarationList, "foo.. 9000;--after:ok", "--after:ok", "--after: ok;", 1035),
            // one_declaration.json pair 9 (zero-based): Mapped the single declaration to NUglify DeclarationList mode. Renamed foo to --foo so arbitrary/empty component values have defined custom-property semantics.
            new CssCase("EmptyCustomProperty", CssType.DeclarationList, "--foo:", "--foo:", "--foo: ;"),
            // one_declaration.json pair 15 (zero-based): Mapped the single declaration to NUglify DeclarationList mode.
            new CssCase("PriorityCase", CssType.DeclarationList, "foo: 9000  !Important", "foo:9000!important", "foo: 9000!important;"),
            // one_declaration.json pair 16 (zero-based): Mapped the single declaration to NUglify DeclarationList mode.
            new CssCase("PriorityComments", CssType.DeclarationList, "foo: 9000  ! /**/\t IMPORTant /**/\f", "foo:9000!important", "foo: 9000!important;"),
            // one_declaration.json pair 17 (zero-based): Mapped the single declaration to NUglify DeclarationList mode.
            new CssCase("NonAsciiPriority", CssType.DeclarationList, "foo: 9000  /* Dotted capital I */!İmportant", "foo:9000!İmportant", "foo: 9000!İmportant;"),
            // one_declaration.json pair 18 (zero-based): Mapped the single declaration to NUglify DeclarationList mode. Renamed foo to --foo so arbitrary/empty component values have defined custom-property semantics.
            new CssCase("PriorityBangSuffix", CssType.DeclarationList, "--foo: 9000  !important!", "--foo:9000!important!", "--foo: 9000!important!;"),
            // one_declaration.json pair 19 (zero-based): Mapped the single declaration to NUglify DeclarationList mode.
            new CssCase("ImportantWithoutBang", CssType.DeclarationList, "foo: 9000  important", "foo:9000 important", "foo: 9000 important;"),
            // one_declaration.json pair 20 (zero-based): Mapped the single declaration to NUglify DeclarationList mode.
            new CssCase("ImportantIdentifier", CssType.DeclarationList, "foo:important", "foo:important", "foo: important;"),
            // blocks_contents.json pair 0 (zero-based): Wrapped block contents in .x{...}.
            new CssCase("EmptyBlock", CssType.FullStyleSheet, ".x{;; /**/ ; ;}", ".x{}", ".x\n{;;;;\n}"),
            // blocks_contents.json pair 1 (zero-based): Wrapped block contents in .x{...}.
            new CssCase("BlockDeclarations", CssType.FullStyleSheet, ".x{a:b; c:d 42!important;\n}", ".x{a:b;c:d 42!important}", ".x\n{\n    a: b;\n    c: d 42!important;\n}"),
            // blocks_contents.json pair 2 (zero-based): Wrapped block contents in .x{...}. Appended a valid following declaration.
            new CssCase("BlockMissingColonRecovery", CssType.FullStyleSheet, ".x{z;a:b;--after:ok}", ".x{a:b;--after:ok}", ".x\n{\n    a: b;\n    --after: ok;\n}", 1035),
            // blocks_contents.json pair 3 (zero-based): Wrapped block contents in .x{...}. Appended a valid following declaration.
            new CssCase("BlockLiteralBang", CssType.FullStyleSheet, ".x{z:x!;a:b;--after:ok}", ".x{z:x!;a:b;--after:ok}", ".x\n{\n    z: x!;\n    a: b;\n    --after: ok;\n}"),
            // blocks_contents.json pair 4 (zero-based): Wrapped block contents in .x{...}. Appended a valid following declaration.
            new CssCase("BlockInvalidPropertyRecovery", CssType.FullStyleSheet, ".x{a:b; c+:d;--after:ok}", ".x{a:b;--after:ok}", ".x\n{\n    a: b;\n    --after: ok;\n}", 1035),
            // blocks_contents.json pair 8 (zero-based): Wrapped block contents in .x{...}.
            new CssCase("NestedRuleAfterDeclaration", CssType.FullStyleSheet, ".x{z:x;a b{c:d;;e:f}}", ".x{z:x;a b{c:d;e:f}}", ".x\n{\n    z: x;\n    a b\n    {\n        c: d;;\n        e: f;\n    }\n}"),
            // blocks_contents.json pair 10 (zero-based): Wrapped block contents in .x{...}.
            new CssCase("NestedPseudoRule", CssType.FullStyleSheet, ".x{a:hover {c:1}}", ".x{a:hover{c:1}}", ".x\n{\n    a:hover\n    {\n        c: 1;\n    }\n}"),
            // blocks_contents.json pair 11 (zero-based): Wrapped block contents in .x{...}.
            new CssCase("DeclarationAfterNestedRule", CssType.FullStyleSheet, ".x{z:x;a b{c:d}e:f}", ".x{z:x;a b{c:d}e:f}", ".x\n{\n    z: x;\n    a b\n    {\n        c: d;\n    }\n    e: f;\n}"),
            // component_value_list.json pair 2 (zero-based): Wrapped component values in a custom property followed by --after:ok.
            new CssCase("IdentifierValue", CssType.FullStyleSheet, ".x{--sample:red;--after:ok}", ".x{--sample:red;--after:ok}", ".x\n{\n    --sample: red;\n    --after: ok;\n}"),
            // component_value_list.json pair 3 (zero-based): Wrapped component values in a custom property followed by --after:ok.
            new CssCase("IdentifierWhitespace", CssType.FullStyleSheet, ".x{--sample:  \t\t\r\n\nRed ;--after:ok}", ".x{--sample:Red;--after:ok}", ".x\n{\n    --sample: Red;\n    --after: ok;\n}"),
            // component_value_list.json pair 12 (zero-based): Used the selector component list as a selector and added a sentinel declaration.
            new CssCase("EscapedStringNewlines", CssType.FullStyleSheet, "p[example=\"\\\nfoo(int x) {\\\n   this.x = x;\\\n}\\\n\"]{--after:ok}", "p[example=\"foo(int x) {   this.x = x;}\"]{--after:ok}", "p[example=\"foo(int x) {   this.x = x;}\"]\n{\n    --after: ok;\n}"),
            // component_value_list.json pair 27 (zero-based): Wrapped component values in a custom property followed by --after:ok.
            new CssCase("Numbers", CssType.FullStyleSheet, ".x{--sample:12 +34 -45 .67 +.89 -.01 2.3 +45.0 -0.67;--after:ok}", ".x{--sample:12 +34 -45 .67 +.89 -.01 2.3 +45.0 -0.67;--after:ok}", ".x\n{\n    --sample: 12 +34 -45 .67 +.89 -.01 2.3 +45.0 -0.67;\n    --after: ok;\n}"),
            // component_value_list.json pair 28 (zero-based): Wrapped component values in a custom property followed by --after:ok.
            new CssCase("ExponentNumbers", CssType.FullStyleSheet, ".x{--sample:12e2 +34e+1 -45E-0 .68e+3 +.79e-1 -.01E2 2.3E+1 +45.0e6 -0.67e0;--after:ok}", ".x{--sample:12e2 +34e+1 -45E-0 .68e+3 +.79e-1 -.01E2 2.3E+1 +45.0e6 -0.67e0;--after:ok}", ".x\n{\n    --sample: 12e2 +34e+1 -45E-0 .68e+3 +.79e-1 -.01E2 2.3E+1 +45.0e6 -0.67e0;\n    --after: ok;\n}"),
            // component_value_list.json pair 32 (zero-based): Wrapped component values in a custom property followed by --after:ok.
            new CssCase("Percentages", CssType.FullStyleSheet, ".x{--sample:12% +34% -45% .67% +.89% -.01% 2.3% +45.0% -0.67%;--after:ok}", ".x{--sample:12% +34% -45% .67% +.89% -.01% 2.3% +45.0% -0.67%;--after:ok}", ".x\n{\n    --sample: 12% +34% -45% .67% +.89% -.01% 2.3% +45.0% -0.67%;\n    --after: ok;\n}"),
            // component_value_list.json pair 33 (zero-based): Wrapped component values in a custom property followed by --after:ok.
            new CssCase("ExponentPercentages", CssType.FullStyleSheet, ".x{--sample:12e2% +34e+1% -45E-0% .68e+3% +.79e-1% -.01E2% 2.3E+1% +45.0e6% -0.67e0%;--after:ok}", ".x{--sample:12e2% +34e+1% -45E-0% .68e+3% +.79e-1% -.01E2% 2.3E+1% +45.0e6% -0.67e0%;--after:ok}", ".x\n{\n    --sample: 12e2% +34e+1% -45E-0% .68e+3% +.79e-1% -.01E2% 2.3E+1% +45.0e6% -0.67e0%;\n    --after: ok;\n}"),
            // component_value_list.json pair 35 (zero-based): Wrapped component values in a custom property followed by --after:ok.
            new CssCase("Dimensions", CssType.FullStyleSheet, ".x{--sample:12px +34px -45px .67px +.89px -.01px 2.3px +45.0px -0.67px;--after:ok}", ".x{--sample:12px +34px -45px .67px +.89px -.01px 2.3px +45.0px -0.67px;--after:ok}", ".x\n{\n    --sample: 12px +34px -45px .67px +.89px -.01px 2.3px +45.0px -0.67px;\n    --after: ok;\n}"),
            // component_value_list.json pair 36 (zero-based): Wrapped component values in a custom property followed by --after:ok.
            new CssCase("ExponentDimensions", CssType.FullStyleSheet, ".x{--sample:12e2px +34e+1px -45E-0px .68e+3px +.79e-1px -.01E2px 2.3E+1px +45.0e6px -0.67e0px;--after:ok}", ".x{--sample:12e2px +34e+1px -45E-0px .68e+3px +.79e-1px -.01E2px 2.3E+1px +45.0e6px -0.67e0px;--after:ok}", ".x\n{\n    --sample: 12e2px +34e+1px -45E-0px .68e+3px +.79e-1px -.01E2px 2.3E+1px +45.0e6px -0.67e0px;\n    --after: ok;\n}"),
        };

        public static IEnumerable<TestCaseData> Cases()
        {
            foreach (var input in Inputs.Concat(BoundaryInputs))
                foreach (var pretty in new[] { false, true })
                    yield return new TestCaseData(input, pretty)
                        .SetName($"CssConformance.{input.Id}.{(pretty ? "Pretty" : "Minify")}");
        }

        static CssSettings Settings(CssType mode, bool pretty, bool decodeEscapes = false)
        {
            var settings = pretty ? CssSettings.Pretty() : new CssSettings();
            settings.CssType = mode;
            settings.CommentMode = CssComment.None;
            settings.ColorNames = CssColor.NoSwap;
            settings.AbbreviateHexColor = false;
            settings.MinifyExpressions = false;
            settings.FixIE8Fonts = false;
            settings.DecodeEscapes = decodeEscapes;
            settings.RemoveEmptyBlocks = false;
            settings.AllowEmbeddedAspNetBlocks = false;
            settings.IgnoreRazorEscapeSequence = true;
            settings.WarningLevel = 1;
            return settings;
        }

        [TestCaseSource(nameof(Cases))]
        public void SyntaxRecoveryAndOutput(CssCase input, bool pretty)
        {
            var result = Uglify.Css(input.Source, Settings(input.Mode, pretty, input.DecodeEscapes));
            var context = $"{input.Id}; mode: {input.Mode}; profile: {(pretty ? "Pretty" : "Minify")}\nSource:\n{input.Source}\nDiagnostics:\n"
                + string.Join("\n", result.Errors);
            Assert.Multiple(() =>
            {
                Assert.That(result.Errors.Select(error => error.ErrorNumber), Is.EqualTo(input.Errors), context);
                Assert.That(result.HasErrors, Is.EqualTo(input.Errors.Length != 0), context);
                Assert.That(result.Code, Is.EqualTo(pretty ? input.Pretty : input.Minify), context);
            });
            // Recovery output is a valid stylesheet/declaration list even when input was malformed.
            var reparsed = Uglify.Css(result.Code, Settings(input.Mode, pretty, input.DecodeEscapes));
            Assert.That(reparsed.Errors, Is.Empty, context + "\nCheck: output reparse\n" + string.Join("\n", reparsed.Errors));
            Assert.That(reparsed.Code, Is.EqualTo(result.Code), context + "\nCheck: idempotence");
        }
        // Adjacent minimized regressions protect the fixes beyond the imported examples.
        [TestCase("--n:45.0;z-index:var(--n)", "--n:45.0;z-index:var(--n)", CssType.DeclarationList)]
        [TestCase("--n:0px;width:var(--n)", "--n:0px;width:var(--n)", CssType.DeclarationList)]
        [TestCase("--n:0.00px;--next:12.0%;width:var(--n)", "--n:0.00px;--next:12.0%;width:var(--n)", CssType.DeclarationList)]
        [TestCase(".x{a:b;--n:45.0;a:hover{z-index:var(--n)}--next:1.0}", ".x{a:b;--n:45.0;a:hover{z-index:var(--n)}--next:1.0}", CssType.FullStyleSheet)]
        [TestCase("width:1.5e+2%;--after:ok", "width:1.5e+2%;--after:ok", CssType.DeclarationList)]
        [TestCase("--x:1e+2future;--after:ok", "--x:1e+2future;--after:ok", CssType.DeclarationList)]
        [TestCase("--x:1.50future;--after:ok", "--x:1.50future;--after:ok", CssType.DeclarationList)]
        [TestCase("width:1e+2future;--after:ok", "width:1e+2future;--after:ok", CssType.DeclarationList)]
        [TestCase("volume:+23dB;voices:12st", "volume:+23dB;voices:12st", CssType.DeclarationList)]
        [TestCase("--x:!IMPORTANTx var(--fallback);--after:ok", "--x:!IMPORTANTx var(--fallback);--after:ok", CssType.DeclarationList)]
        [TestCase("--x:1!IMPORTANT var(--fallback);--after:ok", "--x:1!IMPORTANT var(--fallback);--after:ok", CssType.DeclarationList)]
        [TestCase("--x:1!IMPORTANT!;--after:ok", "--x:1!IMPORTANT!;--after:ok", CssType.DeclarationList)]
        [TestCase("foo:9000 !/**/ iMpOrTaNt;--after:ok", "foo:9000!important;--after:ok", CssType.DeclarationList)]
        public void NumericReplayAndPriorityBoundaries(string source, string expected, CssType mode)
        {
            var result = Uglify.Css(source, Settings(mode, false));
            Assert.Multiple(() =>
            {
                Assert.That(result.Errors, Is.Empty, () => string.Join("\n", result.Errors));
                Assert.That(result.Code, Is.EqualTo(expected));
            });
            var reparsed = Uglify.Css(result.Code, Settings(mode, false));
            Assert.That(reparsed.Errors, Is.Empty);
            Assert.That(reparsed.Code, Is.EqualTo(expected));
        }

        [TestCase(".x{broken;--n:45.0;a:hover{z-index:var(--n)}--after:ok}",
            ".x{--n:45.0;a:hover{z-index:var(--n)}--after:ok}", new[] { 1035 })]
        [TestCase("a 4{--bad:no}.ok{--after:ok}", ".ok{--after:ok}", new[] { 1031, 1025 })]
        public void MalformedConstructDoesNotLeakIntoFollowingContent(string source, string expected, int[] errors)
        {
            var result = Uglify.Css(source, Settings(CssType.FullStyleSheet, false));
            Assert.That(result.Errors.Select(error => error.ErrorNumber), Is.EqualTo(errors));
            Assert.That(result.Code, Is.EqualTo(expected));
            var reparsed = Uglify.Css(result.Code, Settings(CssType.FullStyleSheet, false));
            Assert.That(reparsed.Errors, Is.Empty);
            Assert.That(reparsed.Code, Is.EqualTo(expected));
        }

        [TestCase("--x:'Hello", "--x:'Hello'", new[] { 1002 })]
        [TestCase("font-family:'bad\ncolor:red;color:green", "color:green", new[] { 1002, 1036 })]
        public void StringRecoveryDistinguishesEofFromNewline(string source, string expected, int[] errors)
        {
            var result = Uglify.Css(source, Settings(CssType.DeclarationList, false));
            Assert.That(result.Errors.Select(error => error.ErrorNumber), Is.EqualTo(errors));
            Assert.That(result.Code, Is.EqualTo(expected));
            var reparsed = Uglify.Css(result.Code, Settings(CssType.DeclarationList, false));
            Assert.That(reparsed.Errors, Is.Empty);
            Assert.That(reparsed.Code, Is.EqualTo(expected));
        }
    }
}
