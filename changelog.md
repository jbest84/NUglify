# Changelog

## v1.21.18 (8 July 2026)
- Added support for CSS Media Queries Level 4.
- Fixed `let`/`const` scope handling for `InvertIfReturn`.
- Fixed binding default values scope handling.

 ## v1.21.17 (29 July 2025)
 - Fixed bug with import, export and with

 ## v1.21.16 (29 July 2025)
 - Fixed bug with async import and modules

## v1.21.15 (7 April 2025)
- Added CSS Clamp support

## v1.21.14 (31 March 2025)
- Fixed bug with null coalesce and operator precidence

## v1.21.13 (24 February 2025)
- Fixed issue where 2 getters with the same name where 1 of the getters is static resulted in "JS1323: Duplicate class element name"

## v1.21.12 (21 February 2025)
- Fixed issue where null coalescing operator combined with "||" or "&&" resulted in syntax errors.

## v1.21.11 (10 December 2024)
- Fixed bug with :has/:is/:where nested selectors

## v1.21.10 (4 November 2024)
- Fixed bug with missing final semicolons in css

## v1.21.9 (24 June 2024)
- Adds support for css custom identifiers

## v1.21.8 (11 June 2024)
- Added support for 'of' keyword in :nth-child and :nth-last-child
- Fixed bug with JavaScript Object Destructuring with default values

## v1.21.7 (24 April 2024)
 - Fixes bug parsing arrow and anonymous functions as object properties within object destructuring.

## v1.21.4 (05 February 2024)
 - Fixes NullReferenceException when parsing object literal with computed property name expression starting with (

## v1.21.3 (04 February 2024)
- Fixed bug with escaped css identifiers

## v1.21.2 (16 December 2023)
- Fixed bug with JS variables named `module`

## v1.21.0 (6 October 2023)
- Added option to disable crunching hex and rga colors

## v1.20.7 (11 May 2023)
- Fixes bug with export async

## v1.20.6 (4 April 2023)
- Fixes bug with delete keyword

## v1.20.5 (26 Jan 2023)
- Fixes bug with duplicate property names

## v1.20.4 (13 December 2022)
- Fixes bug with border: 0 none

## v1.20.3 (23 November 2022)
- Fixes spread bug
- Optimises compression of @supports()
- Optimises !important
- Optimises border:0

## v1.20.2 (21 July 2022)
- Fixes bug with empty css variable declarations

## v1.20.0 (8 April 2022)
- Addes support for HTMLSettings.TagsCaseSensitive

## v1.19.2 (1 April 2022)
- Added support for `new.target.prototype`

## v1.19.1 (30 March 2022)
- Stopped zeros being reduced for css properties

## v1.19.0 (16 March 2022)
- Fixed bug with boolean handling inside JSON

## v1.18.0 (16 March 2022)
- Added support for compressing `<script type="application/json">`

## v1.17.14 (8 March 2022)
- Fixed bug with String.raw``

## v1.17.13 (8 March 2022)
- Fixed bug with yield on the LHS of binary expressions
- Fixed bug with leading underscores in css identifiers
- Fixed bug with yields inside unary expressions and missing parenthesis

## v1.17.10 (24 January 2022)
- Adds support for css variables inside rgba functions

## v1.17.9
- Fixes bug with object destructuring parenthesis

## v1.17.8
- Fixes bug with rest spread call expressions inside object literals

## v1.17.7
- Fixes bug with `await f() === "string"`
- Fixes bug with escaping of dollar sign in template literals

## v1.17.6 (13 January 2021)
- Fixes use of spaces instead of commas in CSS RGB values

## v1.17.5 (10 January 2022)
- Fixes bug with `\$` inside template literals

## v1.17.4
- Skipped due to deployment error

## v1.17.3 (17 December 2021)
- Fixes bug with escape characters in template literals

## v1.17.2 (14 December 2021)
- Fixes :has(> .bla) type function selectors - beware this may still not be perfect but its considerably better than it was

## v1.17.1 (14 December 2021)
- Adds support for #rgba and #rrggbbaa css colors and adds better invalid css color detection

## v1.17.0 (14 December 2021)
- Fixes exception thrown on lambda syntax errors
- Fixes bug with catch parameters inside lexical scopes. Note this removes a syntax error check for ancient IE versions.

## v1.16.6
- Skipped due to deployment error

## v1.16.5
- Skipped due to deployment error

## v1.16.4 (21 November 2021)
- Allows whitespace around Knoockout comments when retaining them in minified HTML

## v1.16.3 (21 November 2021)
- Fixes bug with whitespace around :is() css selectors

## v1.16.2
- Skipped version

## v1.16.1 (5 October 2021)
- Fixes bugs with assignment operator precendence

## v1.16.0 (20 Aug 2021)
- Adds support for private class members

## v1.15.0 (18 Aug 2021)
- Adds support for Class Fields

## v1.14.0 (13 Aug 2021)
- Adds support for CSS case (in)sensitive attribute selectors

## v1.13.15 (5 Aug 2021)
- Added net50 target framework

## v1.13.14 (26 Jul 2021)
- Fixed bug with object literal implicit method duplicates

## v1.13.13 (25 Jul 2021)
- Fixed bug with methods in object initializers

## v1.13.12 (11 Jun 2021)
- Fixed bug with optional catch bindings
- Fixed bug with static and non static class member method duplicates

## v1.13.11 (21 May 2021)
- Fixed bug with `for..of`

## v1.13.10 (7 May 2021)
- Fixed support for multiple negated conditions in css @supports condition 

## v1.13.9 (28 April 2021)
- Fixes bug with object destructing and rest spread in arrow function parameters

## v1.13.8 (15 Mar 2021)
- Fixes bug with javascript use of "of" as an identifier

## v1.13.7 (15 Mar 2021)
- Fixes bug with javascript pretty-print formatting and empty object initializers {}

## v1.13.6 (9 Mar 2021)
- Fixes bug with json script elements in HTML e.g. application/ld+json

## v1.13.4-5
- Publishing error, versions do not exist

## v1.13.3 (22 Feb 2021)
- Fixes false errors reported with use of rest spread operator in object literals

## v1.13.2 (29 Jan 2021)
- Fixed over-minificaiton of aria-hidden="true"

## v1.13.1 (2 Jan 2021)
- Improved HTML formatting when prettifying

## v1.13.0 (1 Jan 2021)
- Improved ObjectLiteral formatting when outputting "prettyified" code

## v1.12.3 (30 December 2020)
- Fixed html comments onto new lines when prettifying

## v1.12.2 (30 December 2020)
- Attempted to fix unsupported computed method names on object initializers. Beware of bugs with this, please report any you find. There shouldn't be any regressions but this may not fully work.

## v1.12.1 (29 December 2020)
- Improves unused setter parameter handling

## v.12.0 (28 December 2020)
- Changed defaults for HTML minification for embedded JS and CSS to make them output prettily

## v1.11.9 (28 December 2020)
- Fixes bug with HtmlToText and trailing newlines

## v1.11.8 (28 December 2020)
- Fixes object destructuring syntax and default values in arrow function arguments.

## v1.11.7 (28 December 2020)
- Fixes but methods called `set` or `get` on classes
- Fixed bug with `for(let x in y)` and `for([x] of y)` and scope lookup crunching

## v1.11.6 (28 December 2020)
- Fixes tagged template literals
- Fixed bug with unused setter `value` parameters being removed

## v1.11.5 (10 December 2020)
- Fixes bug with `<br>`s when HtmlToText()ing

## v1.11.4 (2 December 2020)
- Fixed bug with computed property names in object literals not getting processed

## v1.11.3 (27 November 2020)
- Fixes bugs with escaped slashes in JSON and SourceMaps

## v1.11.1 (25 November 2020)
- Fixes bug with object literal properties and singly referenced lexical declarations

## v1.11.0 (14 November 2020)
- Adds support for `<% %>` ASP tags in HTML Parser

## v1.10.0 (11 November 2020)
- Re-adds long lost support for Minify JS Attributes within HTML
- Fixes bugs with Attriute CSS Settings when minifying HTML
- Fixes bug with stripping Javascript attributes from HTML
- Fixes many bugs with HTML minification and processing, mainly around whitespace
- Removes public `HtmlWriterToHtml.Writer` properly
- Fixes inheritance issues with `HtmlWriterToHtml`
- Performance improvements to HtmlMinification
- Fixes leading whitespace with Prettifying HTML
- Fixes bug with style attributes spanning multiple lines
- Adds support for custom indent character when prettifying CSS and JS, use `CommonSettings.Indent` instead of `CommonSettings.IndentSize`

## v1.9.11 (7 November 2020)
- Made contents of `<script>` and `<style>` elements fully indented when outputting pretty HTML

## 1.9.10 (22 October 2020)
- Improved readme and docs
- Improved HTMLSettings comments
- Added ability to control indentation when prettifying HTML
- Deprecated `HtmlSettings.TagsWithNonCollapsableWhitespaces` in favour of the correct spelling `TagsWithNonCollapsibleWhitespaces`

## 1.9.9 (14 October 2020)
- Fixes bug with PreserveFunctionNames

## 1.9.8 (13 October 2020)
- Fixes bug with dynamic import expressions

## 1.9.7 (7 October 2020)
- Fixes bug with single-lined blocks with a lexical declaration
- Fixes bug with arrow functions returning && and || logical BinaryExpressions

## 1.9.6 (1 October 2020)
- Fixes bug with Optional Chaining

## 1.9.5 (18 September 2020)
- ~Adds support for Optional Chaining~ This is buggy
- Adds support for Numeric Separators
- Adds support for Logical Assignment operators
- Ensures `globalThis` works properly
- Adds basic support for BigInt (minification of BigInt syntax is not yet optimal)

## 1.9.4 (15 September 2020)
- Fixes bug with `crlf` immediately before a closing template literal

## 1.9.3 (15 September 2020)
- Fixes bug with single argument functions with a default parameter value

## 1.9.2 (9 September 2020)
- ~Fixes bug with `crlf` immediately before a closing template literal~ this wasnt properly fixed, see 1.9.4 for the real fix
- Fixes bug with `async` shortand object initializer syntax

## 1.9.1 (25 August 2020)
- Improves handling of rogue closing tags in HTML, manifesting at least in how HtmlToText behaves. There shouldn't be any regressions but we've gone up a minor revision just in case.

## 1.8.1 (24 August 2020)
- Removes deprecated lineCount from SourceMaps
- Adds `MakePathsRelative` true/false to `V3SourceMap` to control path munging
- Fixes JS bug with getters and setters, primarily when used within `defineProperty`

## 1.7.2 (24 August 2020)
- Adds support for HTML attribute removal
- Adds support for HTML attribute reordering

## 1.7.1 (24 August 2020)
- Changes (fixes) how HTML5 comments are handled. Previously valid/invalid comments may now be handled differently (correctly)

## 1.6.6 (21 August 2020)
- Fixes overloading of `async` keyword

## 1.6.5 (19 August 2020)
- Adds Exponent `**` and Exponent Assign `**=` support
- Adds Null Coalesce `??` operator support

## 1.6.4 (6 July 2020)
- Fixes issue with const name redeclaration

## 1.6.3 (10 June 2020)
- Fixes further module variable bugs ([(PR #113](https://github.com/xoofx/NUglify/pull/113))

## 1.6.2 (10 June 2020)
- Fixes module variable bugs ([(PR #112](https://github.com/xoofx/NUglify/pull/112))

## 1.6.1 (10 June 2020)
- Updates Nuget package details

## 1.6.0 (10 June 2020)
- Fix for whitespace preceeding a textarea ([(PR #103)](https://github.com/xoofx/NUglify/pull/103))
- Include application/ld+json in list of processable javascript type blocks ([(PR #102)](https://github.com/xoofx/NUglify/pull/102))
- Fixes infinite loop when dealing with malformed switch statements  ([(PR #99](https://github.com/xoofx/NUglify/pull/99))
- Fixes arrow function parenthesis issue ([(PR #98](https://github.com/xoofx/NUglify/pull/98))
- Fixes arrow functions + sourcemaps bug ([(PR #97](https://github.com/xoofx/NUglify/pull/97))
- Fixes spread operator bug ([(PR #91](https://github.com/xoofx/NUglify/pull/91))
- Fixes issue with mutliple await return ([(PR #90](https://github.com/xoofx/NUglify/pull/90))
- Fixes issue with parameterless arrow functions ([(PR #89](https://github.com/xoofx/NUglify/pull/89))
- Fixes bug with KeyFrames in css ([(PR #107](https://github.com/xoofx/NUglify/pull/107))
- Fixes bug with ShortBooleanAttribute=true incorrectly removes value tags from inputs ([(PR #108](https://github.com/xoofx/NUglify/pull/108))
- Fixes bug with css calc, flex and 0px ([(PR #109](https://github.com/xoofx/NUglify/pull/109))

## 1.5.14 (9 Mar 2020)
- Fix async/await JS minifier
- Add support for CSS @supports
- Adding a options to turoff escape decoding in CSS files.

## 1.5.13 (04 Jun 2019)
- Allow async function expressions ([(PR #65)](https://github.com/xoofx/NUglify/pull/65))
- Fix for(let x of y.prop) ([(PR #69)](https://github.com/xoofx/NUglify/pull/69))
- Fix #71 "Invalid arrow-function arguments" error on Firefox ([(PR #72)](https://github.com/xoofx/NUglify/pull/72))

## 1.5.12 (21 Aug 2018)
- ECMAScript 6 Support for computed names in object property definitions
- Add support for async and await.
- Add support for netstandard2.0

## 1.5.11 (6 May 2018)
- Support for ES6 arrow functions ([PRs](https://github.com/xoofx/NUglify/pulls?utf8=%E2%9C%93&q=is%3Apr+author%3Asamjudson+created%3A%3C2018-05-07))

## 1.5.10 (5 Apr 2018)
- Support for CSS variables ([(PR #46)](https://github.com/xoofx/NUglify/pull/46))
- Implementing Razor escape support in CSS ([(PR #41)](https://github.com/xoofx/NUglify/pull/41))

## 1.5.9
- Respect self-closing tags with "RemoveOptionalTags" option disabled

## 1.5.8
- Fix Check for break outside of a loop but inside a label block (#31)

## 1.5.7
- Fix issue in Html compact with pre/textarea tags not keeping whitespaces      

## 1.5.6
- Add new HtmlSettings.KeepTags to selectively keep a list of optional start/end tags even when  settings.RemoveOptionalTags is true

## 1.5.5
- Fix possible NullReferenceException when using HtmlSettings.RemoveJavaScript = true

## 1.5.4
- Fix regression when collapsing spaces introduced previously

## 1.5.3
- Add option HtmlSettings.KeepOneSpaceWhenCollapsing to keep at least one space when collapsing them

## 1.5.2
- Add support for keeping knockout comments
- Don't minify (0,eval)('this') expressions

## 1.5.1
- Fix bug while minimizing HTML style CSS attribute

## 1.5.0
- Add support to dotnet RTM, switch to netstandard1.6
