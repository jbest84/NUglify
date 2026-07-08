// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license. 
// See the license.txt file in the project root for more information.

using System;
using System.IO;
using System.Net.Http;
using BenchmarkDotNet.Attributes;
using NUglify.Html;
using ZetaProducerHtmlCompressor;

namespace NUglify.Benchmarks
{
    public class BenchMinifier
    {
        private static readonly HttpClient s_httpClient = new HttpClient();

	    readonly string html;

        public BenchMinifier()
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
            var result = Uglify.Html(html, new HtmlSettings() { MinifyJs = false, MinifyCss = false });
            var size = result.Code.Length;
            //File.WriteAllText(
            //    Path.Combine(Path.GetDirectoryName(typeof(Program).Assembly.Location), "HTMLStandard.min.htm"),
            //    result.Code);
        }

        [Benchmark]
        public void BenchHtmlCompressor()
        {
            var compressor = new HtmlContentCompressor();
            var result = compressor.Compress(html);
            var size = result.Length;
            //File.WriteAllText(
            //    Path.Combine(Path.GetDirectoryName(typeof(Program).Assembly.Location), "HTMLStandard.min3.htm"),
            //    result);
        }

        // WebMarkupMin doesn't work

        //[Benchmark]
        //public void BenchWebMarkupMin()
        //{
        //    var minifier = new WebMarkupMin.Core.HtmlMinifier();
        //    var result = minifier.Minify(html);
        //    var size = result.MinifiedContent.Length;

        //    File.WriteAllText(
        //        Path.Combine(Path.GetDirectoryName(typeof(Program).Assembly.Location), "HTMLStandard.min2.htm"),
        //        result.MinifiedContent);
        //}
    }
}
