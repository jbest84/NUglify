using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FsCheck;
using NUglify.JavaScript;
using NUglify.JavaScript.Syntax;
using NUnit.Framework;

namespace NUglify.Tests.JavaScript.Conformance
{
    [TestFixture, Category("JavaScriptConformance"), Category("JavaScriptGenerated")]
    internal sealed class GeneratedGrammarTests
    {
        const int SamplesPerSeed = 200;
        const int SecondSeed = 20261118;
        static readonly string[] Trivia = { "", " ", "\t", "/**/", "/*\n*/", "\n", "\u2028" };
        static readonly string[] Newlines = { "\n", "\r\n", "\r", "\u2028", "\u2029", "/*\n*/", "/*\r\n*/", "/*\u2028*/", "/*\u2029*/" };

        sealed record Input(int Form, int Depth, string Trivia, string Name);

        static string Wrap(string text, int depth) => new string('(', depth) + text + new string(')', depth);

        static IEnumerable<Input> Shrink(Input input, bool newline)
        {
            // Preserve the grammar form and its validity while removing incidental complexity.
            if (input.Depth > 0) yield return input with { Depth = 0 };
            var minimalTrivia = newline ? "\n" : "";
            if (input.Trivia != minimalTrivia) yield return input with { Trivia = minimalTrivia };
            if (input.Name != "a") yield return input with { Name = "a" };
        }

        static void Run(int seed, int forms, bool newline, Func<Input, (string Source, bool Valid)> build,
            Action<Input, BlockStatement> structure = null)
        {
            var replay = Environment.GetEnvironmentVariable("NUGLIFY_CONFORMANCE_SEED");
            if (replay != null && (!int.TryParse(replay, out seed) || seed <= 0))
                throw new ArgumentException("NUGLIFY_CONFORMANCE_SEED must be a positive integer.");
            var generator = from form in Gen.Choose(0, forms - 1)
                            from depth in Gen.Choose(0, 3)
                            from trivia in Gen.Elements(newline ? Newlines : Trivia)
                            from name in Gen.Elements("a", "item", "value0", "object")
                            select new Input(form, depth, trivia, name);
            var config = Configuration.QuickThrowOnFailure;
            config.Replay = FsCheck.Random.StdGen.NewStdGen(seed, SecondSeed);
            config.MaxNbOfTest = SamplesPerSeed;
            config.StartSize = 0;
            config.EndSize = 3;
            config.QuietOnSuccess = true;
            string failure = null;
            var exercised = new HashSet<int>();
            var accepted = 0;
            var rejected = 0;
            var property = Prop.ForAll(Arb.From(generator, new Func<Input, IEnumerable<Input>>(input => Shrink(input, newline))), input =>
            {
                var test = build(input);
                try
                {
                    Verify(test.Source, test.Valid, tree => structure?.Invoke(input, tree));
                    exercised.Add(input.Form);
                    if (test.Valid) accepted++; else rejected++;
                    return true;
                }
                catch (Exception error)
                {
                    failure = $"Form: {input.Form}; expected: {(test.Valid ? "accept" : "reject")}\nSource:\n{test.Source}\n\n{error}";
                    throw;
                }
            });
            try
            {
                property.Check(config);
            }
            catch (Exception error)
            {
                var directory = Path.Combine(TestContext.CurrentContext.TestDirectory, "TestResults", "Conformance");
                Directory.CreateDirectory(directory);
                var file = Path.Combine(directory, "GeneratedGrammar-" + TestContext.CurrentContext.Test.ID + ".txt");
                var report = $"{TestContext.CurrentContext.Test.FullName}\nReplay seed: ({seed}, {SecondSeed}); samples: {SamplesPerSeed}\n"
                    + $"Set NUGLIFY_CONFORMANCE_SEED={seed} and rerun this test.\n\n{failure}\n\nFsCheck result:\n{error}";
                File.WriteAllText(file, report);
                TestContext.AddTestAttachment(file, "Seed and shrunk JavaScript counterexample");
                TestContext.Error.WriteLine(report);
                throw;
            }
            TestContext.Progress.WriteLine($"Generated grammar: seed ({seed}, {SecondSeed}), {accepted} valid + {rejected} invalid samples, "
                + $"{exercised.Count}/{forms} forms; both profiles. Depth parameter: 0..3 (up to four inner functions).");
        }

        static void Verify(string source, bool valid, Action<BlockStatement> structure)
        {
            foreach (var profile in Enum.GetValues<ConformanceProfile>())
            {
                var tree = ConformanceRunner.ParseTree(source, profile, JavaScriptSourceMode.Program, out var input);
                Assert.That(input.HasErrors, Is.EqualTo(!valid),
                    () => $"{profile}: input\n{source}\n{ConformanceRunner.Describe(input)}");
                if (!valid) continue;
                if (profile == ConformanceProfile.NoOptimization) structure(tree);
                var emitted = Uglify.Js(source, ConformanceRunner.Settings(profile, JavaScriptSourceMode.Program));
                Assert.That(emitted.HasErrors, Is.False, () => $"{profile}: emit\n{ConformanceRunner.Describe(emitted)}");
                var outputTree = ConformanceRunner.ParseTree(emitted.Code, profile, JavaScriptSourceMode.Program, out var output);
                Assert.That(output.HasErrors, Is.False,
                    () => $"{profile}: output\n{emitted.Code}\n{ConformanceRunner.Describe(output)}");
                // Compare only the explicitly modeled structure with optimization disabled.
                if (profile == ConformanceProfile.NoOptimization) structure(outputTree);
            }
        }

        [TestCase(104729)]
        [TestCase(130363)]
        public void OperatorPrecedenceAndAssociativity(int seed)
        {
            Run(seed, 9, false, input =>
            {
                var (low, high, grouped) = Operators(input.Form);
                var a = Wrap(input.Name, input.Depth);
                var b = Wrap(input.Name + "B", input.Depth);
                var c = Wrap(input.Name + "C", input.Depth);
                // Separate comment trivia from division so '/' + '/*' cannot become '//'.
                var t = " " + input.Trivia + " ";
                var expression = grouped ? $"({a}{t}{low}{t}{b}){t}{high}{t}{c}" : $"{a}{t}{low}{t}{b}{t}{high}{t}{c}";
                return ($"consume({expression});", true);
            }, (input, tree) =>
            {
                var (low, high, grouped) = Operators(input.Form);
                var root = Binary(Argument(tree), Token(grouped ? high : low));
                var nested = Binary(grouped ? root.Operand1 : root.Operand2, Token(grouped ? low : high));
                Name(grouped ? root.Operand2 : root.Operand1, input.Name + (grouped ? "C" : ""));
                Name(nested.Operand1, input.Name + (grouped ? "" : "B"));
                Name(nested.Operand2, input.Name + (grouped ? "B" : "C"));
            });
        }

        static (string Low, string High, bool Grouped) Operators(int form) => form switch
        {
            0 => ("+", "*", false), 1 => ("-", "/", false), 2 => ("+", "%", false),
            3 => ("+", "*", true), 4 => ("-", "/", true),
            5 => ("||", "&&", false), 6 => ("||", "&&", true),
            7 => ("**", "**", false), 8 => ("**", "**", true),
            _ => throw new ArgumentOutOfRangeException(nameof(form))
        };

        static JSToken Token(string op) => op switch
        {
            "+" => JSToken.Plus, "-" => JSToken.Minus, "*" => JSToken.Multiply,
            "/" => JSToken.Divide, "%" => JSToken.Modulo, "||" => JSToken.LogicalOr,
            "&&" => JSToken.LogicalAnd, "**" => JSToken.Exponent,
            _ => throw new ArgumentOutOfRangeException(nameof(op))
        };

        // These helpers assert specific generated productions, not arbitrary AST equivalence.
        static AstNode Ungroup(AstNode node)
        {
            while (node is GroupingOperator group) node = group.Operand;
            return node;
        }

        static AstNode Argument(BlockStatement tree)
        {
            Assert.That(tree.Count, Is.EqualTo(1));
            Assert.That(tree[0], Is.TypeOf<CallExpression>());
            var call = (CallExpression)tree[0];
            Assert.That(call.Arguments.Count, Is.EqualTo(1));
            Name(call.Function, "consume");
            return call.Arguments[0];
        }

        static BinaryExpression Binary(AstNode node, JSToken token)
        {
            node = Ungroup(node);
            Assert.That(node, Is.TypeOf<BinaryExpression>());
            var binary = (BinaryExpression)node;
            Assert.That(binary.OperatorToken, Is.EqualTo(token));
            return binary;
        }

        static void Name(AstNode node, string expected)
        {
            node = Ungroup(node);
            Assert.That(node, Is.TypeOf<LookupExpression>());
            Assert.That(((LookupExpression)node).Name, Is.EqualTo(expected));
        }

        [TestCase(104729)]
        [TestCase(130363)]
        public void OptionalChainsNullishTargetsAndTemplates(int seed)
        {
            Run(seed, 16, false, input =>
            {
                var obj = Wrap(input.Name, input.Depth);
                var t = input.Trivia;
                return input.Form switch
                {
                    0 => ($"consume({obj}{t}?.field);", true),
                    1 => ($"consume(({obj}{t}?.field).next);", true),
                    2 => ($"{obj}{t}?.field = other;", false),
                    3 => ($"({obj}{t}?.field).next = other;", true),
                    4 => ($"{obj}{t}?.method`text`;", false),
                    5 => ($"({obj}{t}?.method)`text`;", true),
                    6 => ($"consume({obj}{t}?? other || third);", false),
                    7 => ($"consume(({obj}{t}?? other) || third);", true),
                    8 => ($"consume({obj}{t}|| other ?? third);", false),
                    9 => ($"consume({obj}{t}|| (other ?? third));", true),
                    10 => ($"for ([{obj}{t}.field] of values) use({input.Name});", true),
                    11 => ($"for ([({obj}{t},other)] of values) use({input.Name});", false),
                    12 => ($"for (const {input.Name}{t}=0 of values) use({input.Name});", false),
                    13 => ($"for (const {input.Name}{t} of values) use({input.Name});", true),
                    14 => ("tag`\\u{}${" + obj + "}\\xG`;", true),
                    15 => ("`\\u{}`;" + t, false),
                    _ => throw new ArgumentOutOfRangeException()
                };
            }, (input, tree) =>
            {
                if (input.Form != 1) return;
                var member = Ungroup(Argument(tree)) as MemberExpression;
                Assert.That(member, Is.Not.Null);
                Assert.That(member.Name, Is.EqualTo("next"));
                Assert.That(member.OptionalChaining, Is.False);
                // Here grouping terminates the chain and must not be normalized away.
                Assert.That(member.Root, Is.TypeOf<GroupingOperator>());
                var optional = Ungroup(member.Root) as MemberExpression;
                Assert.That(optional, Is.Not.Null);
                Assert.That(optional.OptionalChaining, Is.True);
                Assert.That(optional.Name, Is.EqualTo("field"));
                Name(optional.Root, input.Name);
            });
        }

        [TestCase(104729)]
        [TestCase(130363)]
        public void RestrictedNewlinesAndFunctionContexts(int seed)
        {
            Run(seed, 16, true, input =>
            {
                var n = input.Name;
                var t = input.Trivia;
                string Nested(string body)
                {
                    for (var i = 0; i <= input.Depth; i++) body = $"function nested{i}(){{{body}}}";
                    return body;
                }
                return input.Form switch
                {
                    0 => ($"function f(){{return{t}{Wrap(n, input.Depth)};}}", true),
                    1 => ($"function f(){{throw{t}{n};}}", false),
                    2 => ($"var fn=(){t}=>{{}};", false),
                    3 => ($"var fn=()=>{{}}{t}(()=>{{}})();", true),
                    4 => ($"async function f(){{await {n};}}", true),
                    5 => ($"async function f(){{{Nested($"await {n};")}}}", false),
                    6 => ($"function* f(){{yield {n};}}", true),
                    7 => ($"function* f(){{{Nested($"yield {n};")}}}", false),
                    8 => ($"function f(){{return {Wrap(n, input.Depth)};}}", true),
                    9 => ($"return {Wrap(n, input.Depth)};", false),
                    10 => ($"'use strict';with({n})use({n});", false),
                    11 => ($"with({n})use({n});", true),
                    12 => ($"class C{{static{{return {n};}}}}", false),
                    13 => ($"class C{{static{{{Nested($"return {n};")}}}}}", true),
                    14 => ("class C{static{use(arguments);}}", false),
                    15 => ($"class C{{static{{{Nested("return arguments;")}}}}}", true),
                    _ => throw new ArgumentOutOfRangeException()
                };
            }, (input, tree) =>
            {
                if (input.Form != 0) return;
                Assert.That(tree[0], Is.TypeOf<FunctionObject>());
                var body = ((FunctionObject)tree[0]).Body;
                Assert.That(body[0], Is.TypeOf<ReturnStatement>());
                Assert.That(((ReturnStatement)body[0]).Operand, Is.Null, "The line terminator ends return before its operand.");
            });
        }
    }
}
