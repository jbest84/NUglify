# Implementation Plan: CSS Nesting

## Overview

This plan implements CSS Nesting (W3C CSS Nesting Module Level 1) support in NUglify's CSS pipeline. The work extends `CssScanner` to tokenize the `&` nesting selector and recognize `@layer`/`@scope`, and extends `CssParser` to parse nested style rules, relative selectors, nested selector lists, arbitrary depth, and nesting inside conditional/grouping at-rules. Implementation is in C# and reuses the existing streaming/waypoint output model so non-nested output stays byte-for-byte identical (Requirement 9).

Property-based tests use FsCheck (the established .NET PBT library) with NUnit, matching the existing test setup. Each correctness property from the design becomes its own property test sub-task; each is tagged with a `// Feature: css-nesting, Property {number}` comment and runs a minimum of 100 iterations.

Test sub-tasks are required and must be implemented alongside the core implementation sub-tasks.

## Tasks

- [x] 1. Add scanner token types and tokenize the nesting selector
  - [x] 1.1 Add new token types to the `TokenType` enum
    - Add `NestingSelector`, `LayerSymbol`, and `ScopeSymbol` to the `TokenType` enum in `src/NUglify/Css/CssToken.cs`
    - _Requirements: 1.1_

  - [x] 1.2 Emit a `NestingSelector` token for `&` in `CssScanner.NextToken`
    - Add a `case '&'` branch in `CssScanner.NextToken` that consumes one `&` and emits a single `TokenType.NestingSelector` token using the current context, instead of falling through to `ScanIdent`
    - Ensure two consecutive `&` characters produce two separate adjacent tokens, and that adjacency (no whitespace) with neighboring tokens like `&.bar` / `.parent&` is preserved
    - Ensure `&` inside string literals and comments is not reached (consumed by existing `ScanString`/`ScanComment` before the `&` case)
    - _Requirements: 1.1, 1.2, 1.3, 1.4_

  - [x] 1.3 Write property test for nesting selector token count
    - **Property 1: Nesting selector token count**
    - **Validates: Requirements 1.1, 1.2, 1.3**

  - [x] 1.4 Write property test for ampersand inside literals
    - **Property 2: Ampersand in a literal is never a nesting token**
    - **Validates: Requirements 1.4**

- [x] 2. Recognize `@layer` and `@scope` at-keywords in the scanner
  - [x] 2.1 Add `@layer` and `@scope` recognition in `ScanAtKeyword`
    - In `CssScanner.ScanAtKeyword`, recognize `layer` and `scope` keywords and emit `TokenType.LayerSymbol` / `TokenType.ScopeSymbol` respectively, mirroring how existing at-keyword symbols (e.g. media, supports) are recognized
    - _Requirements: 7.2_

- [x] 3. Add nesting error codes and error-reporting scaffolding
  - [x] 3.1 Add `UnexpectedNestingSelector` error code and message
    - Add `UnexpectedNestingSelector` to the `CssErrorCode` enum in `src/NUglify/Css/CssErrorCode.cs`
    - Add the corresponding resource string in `src/NUglify/Css/CssStrings.resx` (and regenerate `CssStrings.Designer.cs` if needed)
    - _Requirements: 1.5, 3.6_

- [x] 4. Implement nested selector parsing
  - [x] 4.1 Implement `ParseNestedSelector` with `&` handling
    - Add `ParseNestedSelector` in `CssParser` that appends `&` when the current token is `NestingSelector` and continues the compound/complex selector using the existing `ParseSimpleSelector`/`ParseCombinator`
    - Support standalone `&`, joined (`&.bar`, `&:hover`) with zero added whitespace, repeated (`& + &`) preserving combinators, `&` after another selector (`.parent &`), and doubled (`&&`) with zero whitespace
    - Support relative selectors: a nested selector beginning with a combinator (`>`, `+`, `~`) or a bare compound selector is accepted with an implied leading `&`, emitting exactly what was written with no explicit `&` inserted
    - Report `ExpectedSelector` when a leading combinator is not followed by a valid compound selector, and fail the rule
    - _Requirements: 3.1, 3.2, 3.3, 3.4, 3.5, 4.1, 4.2, 4.3, 4.4, 4.5_

  - [x] 4.2 Implement `ParseNestedSelectorList`
    - Add `ParseNestedSelectorList` in `CssParser` that parses comma-separated nested selectors, allowing optional whitespace around commas, and associates the shared declaration block with each parsed selector
    - Emit selectors separated by a single comma with no surrounding whitespace in minified output, and using the existing selector-list formatting in pretty output
    - Fail the entire list atomically (discard buffered list output, emit no selectors) and report `ExpectedSelector` on any invalid selector or empty selector position (leading, trailing, or doubled comma)
    - _Requirements: 5.1, 5.2, 5.3, 5.4, 5.5, 5.6_

  - [x] 4.3 Write property test for nesting selector emitted verbatim in position
    - **Property 5: Nesting selector emitted verbatim in position**
    - **Validates: Requirements 3.1, 3.2, 3.3, 3.4, 3.5**

  - [x] 4.4 Write property test for relative nested selectors
    - **Property 6: Relative nested selectors keep their leading form**
    - **Validates: Requirements 4.1, 4.2, 4.3, 4.4**

  - [x] 4.5 Write property test for nested selector list membership
    - **Property 7: Nested selector list membership**
    - **Validates: Requirements 5.1, 5.2, 5.4, 8.2**

  - [x] 4.6 Write property test for atomic failure of invalid nested selector lists
    - **Property 8: Invalid nested selector list fails atomically**
    - **Validates: Requirements 5.5, 5.6**

- [x] 5. Implement nested rule parsing and block-body classification
  - [x] 5.1 Implement `ParseNestedRule`
    - Add `ParseNestedRule` in `CssParser` that pushes a waypoint (for `RemoveEmptyBlocks`), parses the nested selector list via `ParseNestedSelectorList`, and on `{` calls the existing `ParseDeclarationBlock` so nesting recurses to arbitrary depth
    - Emit using the same `NewLine`/`Indent`/`Append` helpers as `ParseRule` so pretty-mode indentation nests one level per depth
    - _Requirements: 2.1, 6.1, 6.2, 6.3, 6.4, 8.7_

  - [x] 5.2 Implement block-body classification in `ParseBlockBody`
    - Refactor the existing `ParseDeclarationList` loop into a `ParseBlockBody` helper (retaining the declaration-only fast path for byte-for-byte fidelity) that classifies each item as a declaration, a nested rule, or a nested at-rule
    - Route `NestingSelector` and leading combinator (`>`, `+`, `~`) items to `ParseNestedRule`; route recognized at-rule symbols to their at-rule parsers; use bounded lookahead into a buffered waypoint to disambiguate a leading identifier (declaration `prop:value` vs nested type-selector rule ending in `{`); route other selector-starting tokens (`.`, `#`, `[`, `:`, `*`) to `ParseNestedRule`
    - Preserve relative source order of declarations and nested rules by streaming each item as recognized (no bucketing)
    - Report a parse error and reject the enclosing rule (discard its waypoint) when an item matches neither a valid declaration nor a valid nested rule, and report EOF errors for unterminated nested blocks
    - _Requirements: 2.1, 2.2, 2.3, 2.4, 2.5, 2.6_

  - [x] 5.3 Enforce nesting-selector position validity in declaration parsing
    - In `ParseDeclaration`/`ParseExpr`, when a `NestingSelector` token is encountered where a term/value is expected, report `CssErrorCode.UnexpectedNestingSelector` identifying the token and recover via `SkipToEndOfDeclaration`
    - _Requirements: 1.5, 3.6_

  - [x] 5.4 Write property test for source order preservation
    - **Property 3: Source order preservation**
    - **Validates: Requirements 2.2, 2.3, 8.6**

  - [x] 5.5 Write property test for nesting structure round-trip
    - **Property 4: Nesting structure round-trip**
    - **Validates: Requirements 2.1, 6.4, 7.4**

  - [x] 5.6 Write property test for arbitrary depth preservation
    - **Property 9: Arbitrary depth is preserved**
    - **Validates: Requirements 6.2, 6.3, 6.4**

- [x] 6. Checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 7. Implement at-rule support for nesting
  - [x] 7.1 Wire nested-rule support into `@media` and `@supports`
    - Ensure the existing `ParseMedia`/`ParseSupports` block-body rule loops route into `ParseRule`/`ParseNestedRule` so nested rules and `&` work inside `@media`/`@supports` blocks
    - _Requirements: 7.1, 7.3_

  - [x] 7.2 Implement `ParseLayer` and `ParseScope`
    - Add `ParseLayer` and `ParseScope` in `CssParser`: parse the at-rule prelude (`@layer name;` statement form and `@layer name { ... }` block form; `@scope (start) to (end) { ... }`), and for the block form run the same body loop used by `@media`/`@supports` so contained style rules and their nested rules parse correctly
    - Add `ParseLayer`/`ParseScope` to the stylesheet-level and block-level dispatch chains
    - _Requirements: 7.2, 7.5_

  - [x] 7.3 Handle at-rule block errors and preserve containment on output
    - Ensure at-rule parsers report `ExpectedClosingBrace`/`UnexpectedEndOfFile` on malformed or EOF-truncated blocks and preserve containment of nested rules within the at-rule braces in both minified and pretty output
    - _Requirements: 7.4, 7.6_

  - [x] 7.4 Write property test for at-rule containment
    - **Property 10: At-rule containment is preserved**
    - **Validates: Requirements 7.1, 7.2, 7.3, 7.4, 7.5**

- [x] 8. Implement minified/pretty output invariants and empty-block removal
  - [x] 8.1 Emit correct whitespace for nested rules in minified and pretty output
    - Ensure minified output emits no whitespace immediately inside or outside braces, none around the declaration colon or selector-list commas, none around `>`/`+`/`~` combinators, and exactly one space for the descendant combinator; a declaration following a nested rule's closing brace gets no separator inserted
    - Ensure pretty output indents each nested rule one level deeper than its parent's declarations using the existing indentation unit
    - _Requirements: 8.1, 8.2, 8.3, 8.6, 8.7_

  - [x] 8.2 Support `RemoveEmptyBlocks` for nested rules
    - Ensure that when `RemoveEmptyBlocks` is enabled, a nested rule whose block becomes empty is omitted (via `PopWaypoint` discarding text), and a parent rule left with an empty block after such removal is itself omitted
    - _Requirements: 8.4, 8.5_

  - [x] 8.3 Write property test for minified output whitespace invariants
    - **Property 11: Minified output whitespace invariants**
    - **Validates: Requirements 8.1, 8.2, 8.3, 8.6**

  - [x] 8.4 Write property test for empty-block removal
    - **Property 12: Empty-block removal**
    - **Validates: Requirements 8.4, 8.5**

- [x] 9. Example-based and error-case unit tests
  - [x] 9.1 Write example-based unit tests for canonical nesting cases
    - In `src/NUglify.Tests/Css/Nesting.cs`, add input→expected minified/pretty tests for: standalone `&`, `&.bar`, `& + &`, `.parent &`, `&&`; relative selectors `> .baz`, `+ .bar`, `~ .qux`, bare `.child { ... }`; nested selector lists `&:hover, &:focus { ... }`; mixed declarations and nested rules in source order and a declaration immediately after a nested rule's closing brace; a few explicit deep-nesting levels; nesting inside `@media`, `@supports`, `@layer`, `@scope`; pretty-mode indentation; and `RemoveEmptyBlocks` dropping empty nested rules and empty parents
    - _Requirements: 3.1, 3.2, 3.3, 3.4, 3.5, 4.1, 4.2, 5.1, 6.2, 7.1, 7.2, 8.4, 8.5, 8.7_

  - [x] 9.2 Write error-case unit tests
    - Add tests asserting a parse error and no partial output for: `&` inside a declaration value; leading/trailing/double comma in a nested list; a leading combinator with no following selector; and an unterminated nested block at EOF
    - _Requirements: 1.5, 3.6, 4.5, 5.5, 5.6, 2.6, 6.5, 7.6_

- [x] 10. Regression protection for non-nested behavior
  - [x] 10.1 Write property test for unchanged non-nested output
    - **Property 13: Non-nested output is unchanged**
    - **Validates: Requirements 9.1, 9.2, 9.3**

  - [x] 10.2 Run and reconcile the existing CSS test suite
    - Run the full existing `NUglify.Tests` CSS suite to confirm non-nested output and error sets are unchanged; review and update any pre-existing `@layer`/`@scope` pass-through tests to reflect the new real parsing
    - _Requirements: 9.1, 9.2, 9.3_

- [x] 11. Final checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- All tasks, including test sub-tasks, are required and must be completed.
- Each task references specific requirements (granular sub-requirements) for traceability.
- Property tests use FsCheck with a minimum of 100 iterations and are tagged with `// Feature: css-nesting, Property {number}`.
- Checkpoints ensure incremental validation at natural breaks.
- Property 13 and the existing suite together guard Requirement 9 (byte-for-byte non-nested fidelity).

## Task Dependency Graph

```json
{
  "waves": [
    { "id": 0, "tasks": ["1.1", "3.1"] },
    { "id": 1, "tasks": ["1.2", "2.1"] },
    { "id": 2, "tasks": ["1.3", "1.4", "4.1"] },
    { "id": 3, "tasks": ["4.2", "4.3", "4.4"] },
    { "id": 4, "tasks": ["5.1", "4.5", "4.6"] },
    { "id": 5, "tasks": ["5.2", "5.3"] },
    { "id": 6, "tasks": ["5.4", "5.5", "5.6", "7.1", "7.2"] },
    { "id": 7, "tasks": ["7.3", "8.1", "8.2"] },
    { "id": 8, "tasks": ["7.4", "8.3", "8.4", "9.1", "9.2"] },
    { "id": 9, "tasks": ["10.1", "10.2"] }
  ]
}
```
