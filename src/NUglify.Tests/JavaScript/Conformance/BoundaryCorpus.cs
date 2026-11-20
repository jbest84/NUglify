using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUglify.JavaScript;

namespace NUglify.Tests.JavaScript.Conformance
{
    internal sealed class BoundaryCase
    {
        public string Id { get; set; }
        public string Path { get; set; }
        public JavaScriptSourceMode Mode { get; set; }
        public string Expectation { get; set; }
        public string PairPath { get; set; }
        public string Variant => Mode == JavaScriptSourceMode.Module ? "module" : "script";
    }

    internal sealed class BoundaryCorpus
    {
        readonly string root;
        public BoundaryCase[] Cases { get; private set; }
        BoundaryCorpus(string directory) => root = Path.Combine(directory, "Boundary");
        public string Text(string path) => new UTF8Encoding(false, true).GetString(
            File.ReadAllBytes(Path.Combine(root, path)));

        static BoundaryCase Engine(string id, bool valid) => new BoundaryCase
        {
            Id = id, Path = "Adapted/" + id + ".js", Mode = JavaScriptSourceMode.Program,
            Expectation = valid ? "accept" : "reject"
        };

        static readonly BoundaryCase[] EngineCases =
        {
            Engine("spidermonkey/arrow-for-head", true),
            Engine("spidermonkey/arrow-template-substitution", true),
            Engine("spidermonkey/generator-arrow-regexp", true),
            Engine("spidermonkey/nested-arrow-regexp", true),
            Engine("v8/arrow-regexp-body", true),
            Engine("v8/nullish-associativity", true),
            Engine("v8/nullish-with-or-left", false),
            Engine("v8/nullish-with-or-right", false),
            Engine("v8/optional-call", true),
            Engine("v8/optional-call-tag", false),
            Engine("v8/optional-index", true),
            Engine("v8/optional-index-tag", false),
            Engine("v8/optional-member", true),
            Engine("v8/optional-member-tag", false),
        };

        public static BoundaryCorpus Load(string directory)
        {
            var corpus = new BoundaryCorpus(directory);
            var cases = new List<BoundaryCase>();
            foreach (var kind in new[] { "early", "fail", "pass-explicit", "pass" })
                foreach (var file in Directory.GetFiles(Path.Combine(corpus.root, "ParserTests", kind), "*.js")
                    .OrderBy(file => file, StringComparer.Ordinal))
                {
                    var name = Path.GetFileName(file);
                    var pair = kind == "pass" ? "ParserTests/pass-explicit/" + name : null;
                    if (pair != null && !File.Exists(Path.Combine(corpus.root, pair)))
                        throw new FileNotFoundException("Missing explicit parser pair", pair);
                    cases.Add(new BoundaryCase
                    {
                        Id = "parser/" + kind + "/" + name, Path = "ParserTests/" + kind + "/" + name,
                        Mode = name.EndsWith(".module.js", StringComparison.Ordinal)
                            ? JavaScriptSourceMode.Module : JavaScriptSourceMode.Program,
                        Expectation = kind.StartsWith("pass", StringComparison.Ordinal) ? "accept" : "reject",
                        PairPath = pair
                    });
                }
            corpus.Cases = cases.Concat(EngineCases).ToArray();
            return corpus;
        }
    }
}
