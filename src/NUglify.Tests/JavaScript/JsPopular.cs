// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license. 
// See the license.txt file in the project root for more information.

using System;
using System.Net.Http;
using NUglify.JavaScript;
using NUnit.Framework;

namespace NUglify.Tests.JavaScript
{
    /// <summary>
    /// Tests NUglify Javsscript minifier on several popular web frameworks 
    /// We mainly test that parser is working without any errors.
    /// </summary>
    [TestFixture]
    public class JsPopular
    {
        private static readonly HttpClient s_httpClient = new HttpClient();

        [Test]
        public void TestJQuery()
        {
            var jqueryVersion = "jquery-2.2.3.js";
            AssertCompile($"https://code.jquery.com/{jqueryVersion}", jqueryVersion);
        }

        [Test]
        public void TestAngular()
        {
            var file = "angular-1.5.3.js";
            AssertCompile($"https://ajax.googleapis.com/ajax/libs/angularjs/1.5.3/angular.js", file);
        }

        [Test]
        public void TestHandlebars()
        {
            var file = "handlebars-v4.0.5.js";
            AssertCompile($"http://builds.handlebarsjs.com.s3.amazonaws.com/{file}", file);
        }

        // Not working anymore?
        //[Test]
        //public void TestReact()
        //{
        //    var file = "react-15.0.0.js";
        //    AssertCompile($"https://fb.me/{file}", file);
        //    file = "react-dom-15.0.0.js";
        //    AssertCompile($"https://fb.me/{file}", file);
        //}

        [Test]
        public void TestEmber()
        {
            var file = "ember.prod-v2.4.4.js";
            AssertCompile($"http://builds.emberjs.com/tags/v2.4.4/ember.prod.js", file);
        }

        static void AssertCompile(string url, string file)
        {
            // https://ajax.googleapis.com/ajax/libs/angularjs/1.5.3/angular.js
            var jqueryText = s_httpClient.GetStringAsync(url).GetAwaiter().GetResult();
            var result = Uglify.Js(jqueryText, file, new CodeSettings()
            {
                PreserveImportantComments = false,
                StripDebugStatements = true,
                LineBreakThreshold = int.MaxValue,
            });
            foreach (var error in result.Errors)
            {
                Console.WriteLine(error.ToString());
            }
            Assert.That(!result.HasErrors);
        }
    }
}
