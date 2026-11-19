using System.Collections.Generic;
using System.Linq;
using NUglify.JavaScript;
using NUglify.JavaScript.Syntax;
using NUnit.Framework;

namespace NUglify.Tests.JavaScript.Conformance
{
    internal static class ConformanceRunner
    {
        public static CodeSettings Settings(ConformanceProfile profile, JavaScriptSourceMode mode)
        {
            // Keep language checks separate from NUglify's preprocessing extensions.
            // Strictness comes from the source directive or module goal, not an override.
            return new CodeSettings
            {
                MinifyCode = profile == ConformanceProfile.Minify,
                SourceMode = mode,
                StrictMode = false,
                StripDebugStatements = false,
                IgnorePreprocessorDefines = true,
                IgnoreConditionalCompilation = true,
                AllowEmbeddedAspNetBlocks = false,
                WarningLevel = 4
            };
        }

        public static UglifyResult Parse(string source, ConformanceProfile profile, JavaScriptSourceMode mode, string file = "conformance.js", bool strict = false)
        {
            ParseTree(source, profile, mode, out var result, file, strict);
            return result;
        }

        public static BlockStatement ParseTree(string source, ConformanceProfile profile, JavaScriptSourceMode mode, out UglifyResult result,
            string file = "conformance.js", bool strict = false)
        {
            var errors = new List<UglifyError>();
            var parser = new JSParser();
            parser.CompilerError += (sender, args) => errors.Add(args.Error);
            // Do not print invalid input or turn an exception into an expected rejection.
            var settings = Settings(profile, mode);
            settings.StrictMode = strict;
            var tree = parser.Parse(new DocumentContext(source) { FileContext = file }, settings);
            result = new UglifyResult(null, errors);
            return tree;
        }

        public static string Describe(UglifyResult result) => string.Join("\n", result.Errors.Select(error => error.ToString()));

        public static void Verify(string expected, string actual, UglifyResult result, string context)
        {
            Assert.That(actual, Is.EqualTo(expected),
                () => $"{context}\nExpected: {expected}; actual: {actual}\n{Describe(result)}");
        }
    }
}
