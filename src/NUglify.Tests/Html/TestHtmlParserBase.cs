// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license. 
// See the license.txt file in the project root for more information.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUglify.Html;
using NUnit.Framework;

namespace NUglify.Tests.Html
{
    public abstract class TestHtmlParserBase
    {
	    string currentHtml;
        protected string input;
        protected string output;
        List<UglifyError> messages;

        protected void equal(string result, string expected, params string[] expectedMessages)
        {
            var normalizedResult = NormalizeLineEndings(result);
            var normalizedExpected = NormalizeLineEndings(expected);

            // Print diagnostic only if there will be an error
            var resultMessages = messages?.Select(t => t.ToString()).ToArray() ?? new string[] { };

            if (normalizedExpected != normalizedResult || !((IStructuralEquatable)expectedMessages).Equals(resultMessages, EqualityComparer<string>.Default))
            {
                Console.Out.WriteLine();
                Console.Out.WriteLine("*******************");
                Console.Out.WriteLine($">>> Testing <<<");
                Console.Out.WriteLine();
                Console.Out.WriteLine(currentHtml);

                if (messages != null)
                {
                    foreach (var message in messages)
                    {
                        Console.Out.WriteLine(message);
                    }
                }

                Console.Out.WriteLine("======");
                Console.Out.WriteLine("OUTPUT");
                Console.Out.WriteLine("------");
                Console.Out.WriteLine(result);

                Console.Out.WriteLine("========");
                Console.Out.WriteLine("EXPECTED");
                Console.Out.WriteLine("--------");
                Console.Out.WriteLine(expected);
            }

            Assert.That(normalizedResult, Is.EqualTo(normalizedExpected));
            Assert.That(resultMessages, Is.EqualTo(expectedMessages));
        }

        protected string minify(string html, HtmlSettings settings = null)
        {
            currentHtml = html;
            var result = Uglify.Html(html, settings);

            var text = result.Code;
            messages = result.Errors;
            return text;
        }

        static string NormalizeLineEndings(string text)
        {
            return text?.Replace("\r\n", "\n").Replace("\r", "\n");
        }
    }
}
