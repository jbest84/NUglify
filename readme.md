# NUglify [![CI](https://github.com/jbest84/NUglify/actions/workflows/ci.yml/badge.svg)](https://github.com/jbest84/NUglify/actions/workflows/ci.yml) [![NuGet](https://img.shields.io/nuget/v/NUglify.svg)](https://www.nuget.org/packages/NUglify/)

NUglify provides minify and compression methods for CSS, JavaScript and HTML files.

This repository is a fork of the [Microsoft Ajax Minifier](https://github.com/microsoft/ajaxmin) + additional features (e.g: HTML compressor) 

While dotnet Core is now relying on the **node.js** ecosystem for its client side tooling (e.g: minify), NUglify is **still useful** in scenarios where we need to access this tooling from a .NET application (not necessarily an ASP one) without having to install another developer platform.

The original AjaxMin documentation of the project is available [here](https://github.com/jbest84/NUglify/blob/master/doc/readme.md)

See the [ChangeLog](https://github.com/jbest84/NUglify/blob/master/changelog.md)

## Features

- JS minification
- - Fully ES2020 + ES2021 compliant
- Css minification
- HTML minification
  - Can help to reduce by 5-10% a standard HTML document
  - Supports several minifications methods: remove comments, collapse whitespaces, remove optional tags (p, li...), remove quoted attributes, remove specific attributes, decode HTML entities, compress inline style and script using NUglify
  - No regex involved, full HTML parser
  - Supports HTML5, works best on valid HTML documents (but can still work on invalid documents)
  - Similar to the popular [html-minifier in JS](https://github.com/kangax/html-minifier)
  - Super fast and GC friendly parser and minifier, 10x times faster than existing html compressor for .NET
  - Method `Uglify.HtmlToText` that allows to extract the text from an HTML document
- Compatible with `.NET Framework 4.0`, `.NET Standard 2.0`, and `.NET 10.0`

## Download

NUglify is available as a NuGet package: [![NuGet](https://img.shields.io/nuget/v/NUglify.svg)](https://www.nuget.org/packages/NUglify/)

## Usage

### Basic usage

The main entry point for the API is the `Uglify` class:

For JavaScript:

```csharp
var result = Uglify.Js("var x = 5; var y = 6;");
Console.WriteLine(result.Code);   // prints: var x=5,y=6
```

For Css:

```csharp
var result = Uglify.Css("div { color: #FFF; }");
Console.WriteLine(result.Code);   // prints: div{color:#fff}
```

For Html:

```csharp
var result = Uglify.Html("<div>  <p>This is <em>   a text    </em></p>   </div>");
Console.WriteLine(result.Code);   // prints: <div><p>This is <em>a text</em></div>
```

Extract text from Html:

```csharp
var result = Uglify.HtmlToText("<div>  <p>This is <em>   a text    </em></p>   </div>");
Console.WriteLine(result.Code);   // prints: This is a text
```

### HTML

See the xmldoc comments on the properties of `NUglify.Html.HtmlSettings` for all your HTML minification needs.

For example, to output indented (with tabs) HTML:

```
var htmlSettings = HtmlSettings.Pretty();
htmlSettings.Indent = "\t";
var output = Uglify.Html(input, htmlSettings);
```

### JS

TODO

### CSS

TODO

## Known Issues

See the issues pages on github, however the only real known issue is:

If you overload the `async` keyword as an identifier, like so:

```
function (async) {
    async = 1; // this will work
    async[1] = 2; // this will work
    async(); // this line will be stripped
    async(a,b); // this will end up as: a,b
}
```
you'll see the commented behaviour. This is a difficult job to fix, but PRs welcome. I don't think this is a worth the effort to fix right now, you've got to be a real sadist to be doing this to yourself in the first place.
See #130 if you want to try and fix it yourself.

## Questions

- [ ] Can we collaborate with a project like [Jint](https://github.com/sebastienros/jint) to leverage on a common JavaScript parser infrastructure?
- [ ] Can we utilise [AngleSharp](https://github.com/AngleSharp/AngleSharp) for the HTML/Css minification?

## License

This package includes the repository `license.txt` file, which covers the BSD 2-Clause terms for NUglify and the original Apache 2.0 notice for Microsoft Ajax Minifier.

## Author

Microsoft Ajax Minifier was created and maintained by Ron Logan, with contributions from Eugene Chigirinskiy, Rafael Correa, Kristoffer Henriksson, and Marcin Dobosz.

Nuglify was ported and refactored to github by Alexandre Mutel aka [xoofx](http://xoofx.com)

It is now maintained by Jason Best aka [jbest84](https://github.com/jbest84)

### Previous Maintainers
Andrew Bullock aka [trullock](https://github.com/trullock)


## Credits

The logo Monster is by [Joel McKinney](https://thenounproject.com/joel.mckinney/) from the Noun Project
