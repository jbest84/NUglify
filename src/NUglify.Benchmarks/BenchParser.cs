// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license. 
// See the license.txt file in the project root for more information.

using System;
using System.IO;
using System.Net.Http;
using BenchmarkDotNet.Attributes;
using NUglify.Html;

namespace NUglify.Benchmarks
{
    public class BenchParser
    {
        private static readonly HttpClient s_httpClient = new HttpClient();

	    readonly string html;

        public BenchParser()
        {
            var htmlFile = Path.Combine(Path.GetDirectoryName(typeof(BenchMinifier).Assembly.Location), @"..\HTMLStandard.htm");
            if (!File.Exists(htmlFile))
            {
                Console.WriteLine("Downloading https://html.spec.whatwg.org/");
                html = s_httpClient.GetStringAsync("https://html.spec.whatwg.org/").GetAwaiter().GetResult();
                File.WriteAllText(htmlFile, html);
            }
            else
            {
                html = File.ReadAllText(htmlFile);
            }
        }

        [Benchmark]
        public void BenchNUglify()
        {
            var parser = new HtmlParser(html);
            parser.Parse();
        }

        [Benchmark]
        public void BenchAngleSharp()
        {
            var parser = new AngleSharp.Parser.Html.HtmlParser();
            parser.Parse(html);
        }

        [Benchmark]
        public void BenchHtmlAgility()
        {
            var doc = new HtmlAgilityPack.HtmlDocument();
            doc.LoadHtml(html);
        }
    }
}
