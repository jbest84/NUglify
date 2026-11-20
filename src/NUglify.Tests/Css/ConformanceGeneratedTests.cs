using System;
using System.Collections.Generic;
using System.Linq;
using FsCheck;
using NUglify.Css;
using NUnit.Framework;

namespace NUglify.Tests.Css
{
    public sealed partial class ConformanceTests
    {
        sealed record GeneratedInput(int Form, int Depth, int Context, string Trivia, string Name);
        sealed record GeneratedCase(string Source, string Minify, string Pretty, CssType Mode,
            bool DecodeEscapes = false, int[] Errors = null, bool ErrorsSurviveReparse = false);

        static readonly string[] BoundaryComments = { "/**/", "/* text */", "/**//**/", "/*\n*/" };

        static IEnumerable<GeneratedInput> ShrinkBoundary(GeneratedInput input)
        {
            // Never change the production/context or turn a comment into whitespace:
            // the same token/recovery condition must survive every shrink.
            if (input.Depth > 0) yield return input with { Depth = 0 };
            if (input.Trivia != "/**/") yield return input with { Trivia = "/**/" };
            if (input.Name != "b") yield return input with { Name = "b" };
        }

        static void RunBoundaries(int seed, int forms, Func<GeneratedInput, GeneratedCase> build)
        {
            var generator = from form in Gen.Choose(0, forms - 1)
                            from depth in Gen.Choose(0, 3)
                            from context in Gen.Choose(0, 3)
                            from trivia in Gen.Elements(BoundaryComments)
                            from name in Gen.Elements("b", "c", "d")
                            select new GeneratedInput(form, depth, context, trivia, name);
            var seen = new HashSet<int>();
            var contexts = new HashSet<int>();
            string failure = null;
            var property = Prop.ForAll(Arb.From(generator, new Func<GeneratedInput, IEnumerable<GeneratedInput>>(ShrinkBoundary)), input =>
            {
                var test = build(input);
                try
                {
                    VerifyGenerated(test, input);
                    seen.Add(input.Form);
                    contexts.Add(input.Context);
                    return true;
                }
                catch (Exception error)
                {
                    failure = $"{input}\nSource:\n{test.Source}\n\n{error}";
                    throw;
                }
            });
            GeneratedChecks.Run(property, seed, () => failure);
            Assert.That(seen.Count, Is.EqualTo(forms), "Every production must be exercised by this seed.");
            Assert.That(contexts.Count, Is.EqualTo(4), "Every parsing context must be exercised by this seed.");
            TestContext.Progress.WriteLine($"CSS boundaries: {seen.Count}/{forms} forms, {contexts.Count}/4 contexts; Minify and Pretty.");
        }

        static void VerifyGenerated(GeneratedCase test, GeneratedInput input)
        {
            foreach (var pretty in new[] { false, true })
            {
                var expectedErrors = test.Errors ?? Array.Empty<int>();
                var expected = pretty ? test.Pretty : test.Minify;
                var details = $"{input}; mode: {test.Mode}; profile: {(pretty ? "Pretty" : "Minify")}; DecodeEscapes: {test.DecodeEscapes}\n"
                    + "Settings: CommentMode=None; ColorNames=NoSwap; AbbreviateHexColor=false; MinifyExpressions=false; FixIE8Fonts=false; "
                    + "RemoveEmptyBlocks=false; AllowEmbeddedAspNetBlocks=false; IgnoreRazorEscapeSequence=true; WarningLevel=1\n"
                    + $"Source:\n{test.Source}\nExpected errors: {string.Join(",", expectedErrors)}\nExpected:\n{expected}";
                UglifyResult result;
                try { result = Uglify.Css(test.Source, Settings(test.Mode, pretty, test.DecodeEscapes)); }
                catch (Exception error) { throw new AssertionException(details + "\nParser threw:\n" + error); }
                details += $"\nActual:\n{result.Code}\nDiagnostics:\n{string.Join("\n", result.Errors)}";
                Assert.That(result.Errors.Select(error => error.ErrorNumber), Is.EqualTo(expectedErrors), details);
                Assert.That(result.HasErrors, Is.EqualTo(expectedErrors.Length > 0), details);
                Assert.That(result.Code, Is.EqualTo(expected), details);
                var reparsed = Uglify.Css(result.Code, Settings(test.Mode, pretty, test.DecodeEscapes));
                Assert.That(reparsed.Errors.Select(error => error.ErrorNumber),
                    Is.EqualTo(test.ErrorsSurviveReparse ? expectedErrors : Array.Empty<int>()), details + "\nReparse");
                Assert.That(reparsed.Code, Is.EqualTo(result.Code), details + "\nIdempotence");
            }
        }

        // This printer handles only the four explicitly modeled wrappers. Expected
        // declarations are built independently of parser output, in their source order.
        static GeneratedCase InContext(GeneratedInput input, string source, string minify, string pretty,
            bool decode = false, int[] errors = null, bool errorsSurvive = false)
        {
            var mode = input.Context == 0 ? CssType.DeclarationList : CssType.FullStyleSheet;
            var minPrefix = input.Context switch { 0 => "", 1 => ".x{", 2 => "@font-face{", _ => "@media screen{.x{" };
            var minSuffix = input.Context switch { 0 => "", 3 => "}}", _ => "}" };
            var prettyPrefix = input.Context switch { 0 => "", 1 => ".x\n{\n", 2 => "@font-face\n{\n", _ => "@media screen\n{\n    .x\n    {\n" };
            var prettySuffix = input.Context switch { 0 => "", 2 => "\n}\n", 3 => "\n    }\n}", _ => "\n}" };
            var indent = new string(' ', input.Context == 0 ? 0 : input.Context == 3 ? 8 : 4);
            return new GeneratedCase(minPrefix + source + minSuffix, minPrefix + minify + minSuffix,
                prettyPrefix + string.Join("\n", pretty.Split('\n').Select(line => indent + line)) + prettySuffix,
                mode, decode, errors, errorsSurvive);
        }

        static GeneratedCase ValueCase(GeneratedInput input, string source, string expected, bool decode = false)
            => InContext(input, $"--x:{source};--after:{input.Name}", $"--x:{expected};--after:{input.Name}",
                $"--x: {expected};\n--after: {input.Name};", decode);

        [TestCase(104729), TestCase(130363), Category("CssGenerated")]
        public void GeneratedCommentTokenBoundaries(int seed)
            => RunBoundaries(seed, 13, BuildCommentBoundary);

        static GeneratedCase BuildCommentBoundary(GeneratedInput input)
        {
            var name = input.Name;
            var number = (input.Depth + 1).ToString();
            // CSS Syntax serialization requires separation for each of these pairs;
            // a comment preserves token identity without introducing a whitespace token.
            var (left, right) = input.Form switch
            {
                0 => (name, name + "b"), 1 => (name, name + "b()"),
                2 => (number, name), 3 => (number, "%"), 4 => (number, "2"),
                5 => (number + "px", name), 6 => ("#" + name, name),
                7 => ("@" + name, name), 8 => (name, "(" + name + ")"),
                9 => (number, ".5"), 10 => ("/", "*"), 11 => ("@", name),
                12 => ("#", name), _ => throw new ArgumentOutOfRangeException()
            };
            return ValueCase(input, $"F({left}{input.Trivia}{right})", $"F({left}/**/{right})");
        }

        [TestCase(104729), TestCase(130363), Category("CssGenerated")]
        public void GeneratedEscapeTerminators(int seed)
            => RunBoundaries(seed, 4, BuildEscapeBoundary);

        static GeneratedCase BuildEscapeBoundary(GeneratedInput input)
        {
            var decode = input.Form >= 2;
            var realSpace = input.Form % 2 == 1;
            var ending = new[] { " ", "\n", "\r\n", "\f" }[input.Depth];
            var source = "\\61" + ending + (realSpace ? " " : "") + input.Name;
            var expected = (decode ? "a" : "\\61 ") + (realSpace ? " " : "") + input.Name;
            return ValueCase(input, source, expected, decode);
        }

        [TestCase(104729), TestCase(130363), Category("CssGenerated")]
        public void GeneratedEscapedCodePoints(int seed)
            => RunBoundaries(seed, 7, BuildCodePointBoundary);

        static GeneratedCase BuildCodePointBoundary(GeneratedInput input)
        {
            // CSS Syntax 3, consume an escaped code point: zero, surrogate and
            // out-of-range escapes independently become U+FFFD; astral values work.
            var escape = new[] { "\\0 ", "\\d800 ", "\\dfff ", "\\110000 ", "\\ffffff ", "\\1f600 ", "\\d83d\\de00 " }[input.Form];
            // Retaining \\0 spelling is token-equivalent to U+FFFD and preserves
            // existing IE hack serialization; its terminator remains significant.
            var value = input.Form == 0 ? "\\0 " : input.Form == 5 ? "\U0001f600" : input.Form == 6 ? "\ufffd\ufffd" : "\ufffd";
            return ValueCase(input, escape + input.Name, value + input.Name, decode: true);
        }

        [TestCase(104729), TestCase(130363), Category("CssGenerated")]
        public void GeneratedOpaqueComponents(int seed)
            => RunBoundaries(seed, 4, BuildOpaqueBoundary);

        static GeneratedCase BuildOpaqueBoundary(GeneratedInput input)
        {
            var value = $"{input.Name}:1.50px;other:var(--missing,);text:'a/*x*/b'";
            var (open, close) = input.Form switch
            {
                0 => ("F(", ")"), 1 => ("[", "]"), 2 => ("{", "}"),
                3 => ("(", ")"), _ => throw new ArgumentOutOfRangeException()
            };
            for (var i = 0; i <= input.Depth; i++) value = open + value + close;
            return ValueCase(input, value, value);
        }

        [TestCase(104729), TestCase(130363), Category("CssGenerated")]
        public void GeneratedDeclarationRecovery(int seed)
            => RunBoundaries(seed, 5, BuildRecoveryBoundary);

        static GeneratedCase BuildRecoveryBoundary(GeneratedInput input)
        {
            var malformed = input.Form == 0 ? "broken" : "font-family:'bad"
                + new[] { "\n", "\r", "\r\n", "\f" }[input.Form - 1] + "discarded:value";
            return InContext(input, $"--before:{input.Name};{malformed};--after:{input.Name}b",
                $"--before:{input.Name};--after:{input.Name}b", $"--before: {input.Name};\n--after: {input.Name}b;",
                errors: input.Form == 0 ? new[] { 1035 } : new[] { 1002, 1036 });
        }

        [TestCase(104729), TestCase(130363), Category("CssGenerated")]
        public void GeneratedMathWhitespace(int seed)
            => RunBoundaries(seed, 4, BuildMathBoundary);

        static GeneratedCase BuildMathBoundary(GeneratedInput input)
        {
            var left = (input.Depth + 1) + "px";
            var variable = "var(--" + input.Name + ")";
            var (source, expected) = input.Form switch
            {
                0 => ($"{left} {input.Trivia} + {input.Trivia} 2px", $"{left} + 2px"),
                1 => ($"{left}{input.Trivia}+{input.Trivia}2px", $"{left}+/**/2px"),
                2 => ($"{left}{input.Trivia}+{input.Trivia}{variable}", $"{left}+{variable}"),
                3 => ($"{left}{input.Trivia}+{input.Trivia}2px + {variable}", $"{left}+/**/2px + {variable}"),
                _ => throw new ArgumentOutOfRangeException()
            };
            return InContext(input, $"width:calc({source});--after:{input.Name}",
                $"width:calc({expected});--after:{input.Name}", $"width: calc({expected});\n--after: {input.Name};",
                errors: input.Form == 1 ? new[] { 1065 } : null, errorsSurvive: input.Form == 1);
        }
    }
}
