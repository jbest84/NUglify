using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUglify.JavaScript;

namespace NUglify.Tests.JavaScript.Conformance
{
    internal enum ConformanceProfile { NoOptimization, Minify }

    internal sealed class CorpusCase
    {
        public CorpusCase() { }
        public CorpusCase(string path, string group, string negativePhase, params string[] flags)
        {
            Path = path; Group = group; NegativePhase = negativePhase; Flags = flags;
            NegativeType = negativePhase == null ? null : "SyntaxError";
            ParseExpectation = negativePhase == "parse" ? "reject" : "accept";
        }
        public string Path { get; set; }
        public string Group { get; set; }
        public string[] Flags { get; set; } = Array.Empty<string>();
        public string NegativePhase { get; set; }
        public string NegativeType { get; set; }
        public string ParseExpectation { get; set; }

        public JavaScriptSourceMode SourceMode => Flags.Contains("module")
            ? JavaScriptSourceMode.Module : JavaScriptSourceMode.Program;

        public static bool IsFixture(string path) => path.Contains("_FIXTURE", StringComparison.Ordinal);

        public IEnumerable<string> Variants()
        {
            if (IsFixture(Path)) yield break;
            if (Flags.Contains("module")) { yield return "module"; yield break; }
            if (Flags.Contains("raw")) { yield return "raw"; yield break; }
            if (!Flags.Contains("onlyStrict")) yield return "non-strict";
            if (!Flags.Contains("noStrict")) yield return "strict";
        }

        public string Expand(string source, string variant)
        {
            if (!Variants().Contains(variant))
                throw new InvalidDataException($"Invalid variant {variant} for {Path}.");
            return variant == "strict" ? "\"use strict\";\n" + source : source;
        }
    }

    internal sealed class ConformanceCorpus
    {
        readonly string root;
        // Only syntax-relevant metadata is copied from the pinned Test262 frontmatter.
        // Runtime/resolution negatives still have valid top-level syntax.
        public CorpusCase[] Cases { get; } = new[]
        {
            new CorpusCase("test/language/comments/hashbang/escaped-hashbang.js", "raw-source", "parse", "raw"),
            new CorpusCase("test/language/comments/hashbang/not-empty.js", "raw-source", null, "raw"),
            new CorpusCase("test/language/comments/hashbang/preceding-directive-prologue-sc.js", "raw-source", "parse", "raw"),
            new CorpusCase("test/language/expressions/arrow-function/array-destructuring-param-strict-body.js", "arrow-functions", "parse", "generated"),
            new CorpusCase("test/language/expressions/arrow-function/arrow/concisebody-lookahead-assignmentexpression-1.js", "arrow-functions", null),
            new CorpusCase("test/language/expressions/arrow-function/arrow/concisebody-lookahead-assignmentexpression-2.js", "arrow-functions", null),
            new CorpusCase("test/language/expressions/arrow-function/dflt-params-arg-val-undefined.js", "arrow-functions", null, "generated"),
            new CorpusCase("test/language/expressions/arrow-function/dflt-params-duplicates.js", "arrow-functions", "parse", "generated"),
            new CorpusCase("test/language/expressions/arrow-function/expression-body-implicit-return.js", "arrow-functions", null),
            new CorpusCase("test/language/expressions/arrow-function/object-literal-return-requires-body-parens.js", "arrow-functions", null),
            new CorpusCase("test/language/expressions/arrow-function/syntax/arrowparameters-bindingidentifier-concisebody-functionbody.js", "arrow-functions", null),
            new CorpusCase("test/language/expressions/arrow-function/syntax/arrowparameters-bindingidentifier-lineterminator-concisebody-functionbody.js", "arrow-functions", null),
            new CorpusCase("test/language/expressions/arrow-function/syntax/arrowparameters-cover-concisebody-functionbody.js", "arrow-functions", null),
            new CorpusCase("test/language/expressions/arrow-function/syntax/arrowparameters-cover-lineterminator-concisebody-functionbody.js", "arrow-functions", null),
            new CorpusCase("test/language/expressions/arrow-function/syntax/early-errors/asi-restriction-invalid.js", "arrow-functions", "parse"),
            new CorpusCase("test/language/expressions/assignment/dstr/array-elem-init-assignment.js", "destructuring", null, "generated"),
            new CorpusCase("test/language/expressions/assignment/dstr/array-elem-init-evaluation.js", "destructuring", null, "generated"),
            new CorpusCase("test/language/expressions/assignment/dstr/array-elem-init-simple-no-strict.js", "destructuring", null, "generated", "noStrict"),
            new CorpusCase("test/language/expressions/assignment/dstr/array-elem-init-yield-ident-invalid.js", "destructuring", "parse", "generated", "onlyStrict"),
            new CorpusCase("test/language/expressions/assignment/dstr/array-elem-init-yield-ident-valid.js", "destructuring", null, "generated", "noStrict"),
            new CorpusCase("test/language/expressions/assignment/dstr/array-elem-nested-array-invalid.js", "destructuring", "parse", "generated"),
            new CorpusCase("test/language/expressions/function/dflt-params-abrupt.js", "parameter-defaults", null, "generated"),
            new CorpusCase("test/language/expressions/function/dflt-params-arg-val-undefined.js", "parameter-defaults", null, "generated"),
            new CorpusCase("test/language/expressions/function/dflt-params-duplicates.js", "parameter-defaults", "parse", "generated"),
            new CorpusCase("test/language/expressions/function/dflt-params-ref-later.js", "parameter-defaults", null, "generated"),
            new CorpusCase("test/language/expressions/optional-chaining/call-expression-super-no-base.js", "optional-chaining", "parse"),
            new CorpusCase("test/language/expressions/optional-chaining/call-expression.js", "optional-chaining", null),
            new CorpusCase("test/language/expressions/optional-chaining/early-errors-tail-position-op-template-string-esi.js", "optional-chaining", "parse"),
            new CorpusCase("test/language/expressions/optional-chaining/early-errors-tail-position-op-template-string.js", "optional-chaining", "parse"),
            new CorpusCase("test/language/expressions/optional-chaining/member-expression.js", "optional-chaining", null),
            new CorpusCase("test/language/expressions/optional-chaining/optional-call-preserves-this.js", "optional-chaining", null),
            new CorpusCase("test/language/expressions/optional-chaining/runtime-semantics-evaluation.js", "optional-chaining", null),
            new CorpusCase("test/language/expressions/optional-chaining/short-circuiting.js", "optional-chaining", null),
            new CorpusCase("test/language/expressions/optional-chaining/static-semantics-simple-assignment.js", "optional-chaining", "parse"),
            new CorpusCase("test/language/expressions/optional-chaining/update-expression-prefix.js", "optional-chaining", "parse"),
            new CorpusCase("test/language/expressions/tagged-template/cache-differing-raw-strings.js", "tagged-templates", null),
            new CorpusCase("test/language/expressions/tagged-template/call-expression-context-no-strict.js", "tagged-templates", null, "noStrict"),
            new CorpusCase("test/language/expressions/tagged-template/call-expression-context-strict.js", "tagged-templates", null, "onlyStrict"),
            new CorpusCase("test/language/expressions/tagged-template/chained-application.js", "tagged-templates", null),
            new CorpusCase("test/language/expressions/tagged-template/invalid-escape-sequences.js", "tagged-templates", null),
            new CorpusCase("test/language/expressions/tagged-template/member-expression-context.js", "tagged-templates", null),
            new CorpusCase("test/language/expressions/template-literal/invalid-hexidecimal-character-escape-sequence-truncated-1.js", "template-literals", "parse"),
            new CorpusCase("test/language/expressions/template-literal/invalid-legacy-octal-escape-sequence-8.js", "template-literals", "parse"),
            new CorpusCase("test/language/expressions/template-literal/literal-expr-obj.js", "template-literals", null),
            new CorpusCase("test/language/expressions/template-literal/tv-character-escape-sequence.js", "template-literals", null),
            new CorpusCase("test/language/expressions/template-literal/tv-hex-escape-sequence.js", "template-literals", null),
            new CorpusCase("test/language/expressions/template-literal/tv-line-continuation.js", "template-literals", null),
            new CorpusCase("test/language/expressions/template-literal/tv-line-terminator-sequence.js", "template-literals", null),
            new CorpusCase("test/language/expressions/template-literal/tv-template-middle.js", "template-literals", null),
            new CorpusCase("test/language/expressions/yield/invalid-left-hand-side.js", "yield", "parse"),
            new CorpusCase("test/language/expressions/yield/rhs-regexp.js", "yield", null),
            new CorpusCase("test/language/expressions/yield/rhs-template-middle.js", "yield", null),
            new CorpusCase("test/language/module-code/early-dup-export-dflt.js", "modules", "parse", "module"),
            new CorpusCase("test/language/module-code/early-lex-and-var.js", "modules", "parse", "module"),
            new CorpusCase("test/language/module-code/early-strict-mode.js", "modules", "parse", "module"),
            new CorpusCase("test/language/module-code/eval-export-dflt-cls-anon-semi.js", "modules", null, "module"),
            new CorpusCase("test/language/module-code/instn-resolve-empty-export.js", "modules", "resolution", "module"),
            new CorpusCase("test/language/statements/class/static-init-expr-this.js", "class-static-blocks", null),
            new CorpusCase("test/language/statements/class/static-init-invalid-arguments.js", "class-static-blocks", "parse"),
            new CorpusCase("test/language/statements/class/static-init-invalid-await.js", "class-static-blocks", "parse"),
            new CorpusCase("test/language/statements/class/static-init-invalid-lex-var.js", "class-static-blocks", "parse"),
            new CorpusCase("test/language/statements/class/static-init-invalid-return.js", "class-static-blocks", "parse"),
            new CorpusCase("test/language/statements/class/static-init-invalid-super-call.js", "class-static-blocks", "parse"),
            new CorpusCase("test/language/statements/class/static-init-scope-lex-close.js", "class-static-blocks", null),
            new CorpusCase("test/language/statements/class/static-init-scope-var-open.js", "class-static-blocks", null),
            new CorpusCase("test/language/statements/class/static-init-sequence.js", "class-static-blocks", null),
            new CorpusCase("test/language/statements/class/static-init-statement-list-optional.js", "class-static-blocks", null),
            new CorpusCase("test/language/statements/for-of/head-const-init.js", "for-of", "parse"),
            new CorpusCase("test/language/statements/for-of/head-let-bound-names-dup.js", "for-of", "parse"),
            new CorpusCase("test/language/statements/for-of/head-let-destructuring.js", "for-of", null),
            new CorpusCase("test/language/statements/for-of/head-let-fresh-binding-per-iteration.js", "for-of", null),
            new CorpusCase("test/language/statements/for-of/head-let-init.js", "for-of", "parse"),
            new CorpusCase("test/language/statements/for-of/head-lhs-cover.js", "for-of", null),
            new CorpusCase("test/language/statements/for-of/head-lhs-invalid-asnmt-ptrn-ary.js", "for-of", "parse"),
            new CorpusCase("test/language/statements/for-of/head-lhs-invalid-asnmt-ptrn-obj.js", "for-of", "parse"),
            new CorpusCase("test/language/statements/for-of/head-lhs-member.js", "for-of", null),
            new CorpusCase("test/language/statements/for-of/head-lhs-non-asnmt-trgt.js", "for-of", "parse"),
        };

        ConformanceCorpus(string directory) => root = Path.Combine(directory, "Test262");
        public string Source(CorpusCase test) => new UTF8Encoding(false, true).GetString(
            File.ReadAllBytes(Path.Combine(root, test.Path)));
        public CorpusCase Case(string path) => Cases.Single(test => test.Path == path);

        public static ConformanceCorpus Load(string directory)
        {
            var corpus = new ConformanceCorpus(directory);
            foreach (var test in corpus.Cases) ValidateMetadata(test);
            return corpus;
        }

        static readonly string[] SupportedFlags =
        {
            "onlyStrict", "noStrict", "module", "raw", "async", "generated",
            "CanBlockIsFalse", "CanBlockIsTrue", "non-deterministic"
        };
        internal static void ValidateMetadata(CorpusCase test)
        {
            if (string.IsNullOrWhiteSpace(test.Path) || CorpusCase.IsFixture(test.Path)
                || string.IsNullOrWhiteSpace(test.Group) || test.Flags == null
                || test.Flags.Except(SupportedFlags).Any() || test.Flags.Distinct().Count() != test.Flags.Length
                || (test.Flags.Contains("onlyStrict") && test.Flags.Contains("noStrict"))
                || (test.Flags.Contains("raw") && test.Flags.Any(flag => flag == "onlyStrict" || flag == "module"))
                || (test.Flags.Contains("module") && test.Flags.Any(flag => flag == "onlyStrict" || flag == "noStrict"))
                || (test.NegativePhase != null && test.NegativePhase != "parse" && test.NegativePhase != "runtime" && test.NegativePhase != "resolution")
                || ((test.NegativePhase == null) != (test.NegativeType == null))
                || (test.NegativePhase == "parse" && test.NegativeType != "SyntaxError")
                || test.ParseExpectation != (test.NegativePhase == "parse" ? "reject" : "accept"))
                throw new InvalidDataException("Invalid syntax-test metadata: " + test.Path);
        }
    }
}
